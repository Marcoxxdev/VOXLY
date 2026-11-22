using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LiteNetLib;
using LiteNetLib.Utils;
using VoiceShared;

// VOXLY Server by Marcoxxdev

const string DefaultAdminPwd = "ZMIEN-MNIE-haslo-admina";
const string cfgPath = "config.json", chPath = "channels.json", ranksPath = "ranks.json", clientsPath = "clients.json";
const int LobbyId = 1;
var json = new JsonSerializerOptions { WriteIndented = true };

Console.WriteLine("VOXLY Server by Marcoxxdev");
if (!File.Exists(cfgPath))
{
    File.WriteAllText(cfgPath, JsonSerializer.Serialize(new Config(), json));
    Console.WriteLine("Utworzono config.json. Ustaw AdminPassword i uruchom ponownie.");
    return;
}
var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(cfgPath))!;

// ---------- stan serwera (jeden wątek, bez locków) ----------
var channels = LoadChannels();
var clients = LoadClients();   // rejestr klientów (online i offline), klucz = SHA-256 z ClientId; trzyma też stałe rangi i bany
int nextClientId = clients.Count == 0 ? 1 : clients.Values.Max(c => c.Id) + 1;
PruneClients();
var users = new Dictionary<int, User>();
int nextChannelId = channels.Max(c => c.Id) + 1;
byte[] voiceKey = RandomNumberGenerator.GetBytes(32);   // klucz głosu, wydawany po zalogowaniu
var colorRx = new Regex("^#[0-9A-Fa-f]{6}$");

var listener = new EventBasedNetListener();
var server = new NetManager(listener);
var w = new NetDataWriter();

listener.ConnectionRequestEvent += req =>
{
    if (server.ConnectedPeersCount >= cfg.MaxUsers) req.Reject();
    else req.AcceptIfKey(Wire.ProtocolKey);
};
listener.PeerConnectedEvent += peer => users[peer.Id] = new User(peer);
listener.PeerDisconnectedEvent += (peer, info) =>
{
    if (!users.Remove(peer.Id, out var u)) return;
    u.Ctl?.Dispose();
    if (u.LoggedIn)
    {
        Console.WriteLine($"[-] {u.Nick} ({info.Reason})");
        if (u.Rec != null) { u.Rec.LastSeen = NowUnix(); SaveClients(); }
        Broadcast();
    }
};
listener.NetworkReceiveEvent += (peer, r, ch, dm) =>
{
    try { Handle(peer, r); }
    catch (Exception ex) { Console.WriteLine($"Błędny pakiet od {peer.Address}: {ex.Message}"); }
    finally { r.Recycle(); }
};

// ---------- pakiety ----------
void Handle(NetPeer peer, NetPacketReader r)
{
    if (!users.TryGetValue(peer.Id, out var u)) return;
    var type = (Pkt)r.GetByte();
    switch (type)
    {
        case Pkt.Hello:
        {
            if (u.Ctl != null) return;
            var clientPub = r.GetBytesWithLength();
            using var ecdh = Crypto.NewKey();
            u.Ctl = new AesGcm(Crypto.Derive(ecdh, clientPub), 16);
            w.Reset();
            w.Put((byte)Pkt.HelloReply);
            w.PutBytesWithLength(ecdh.ExportSubjectPublicKeyInfo());
            w.Put(cfg.ServerPasswordEnabled);
            peer.Send(w, DeliveryMethod.ReliableOrdered);
            break;
        }
        case Pkt.Audio:
            // [Audio][seq u16][payload] -> [Audio][ID nadawcy][seq u16][payload]
            if (!u.LoggedIn || r.AvailableBytes < 3) return;
            w.Reset();
            w.Put((byte)Pkt.Audio);
            w.Put((ushort)peer.Id);
            w.Put(r.RawData, r.Position, r.AvailableBytes);
            foreach (var o in users.Values)
                if (o.LoggedIn && o.Peer.Id != peer.Id && o.ChannelId == u.ChannelId)
                    o.Peer.Send(w, DeliveryMethod.Unreliable);
            break;
        case Pkt.Secure:
        {
            if (u.Ctl == null) return;
            var plain = Crypto.Open(u.Ctl, r.GetRemainingBytes());
            if (plain != null) OnMsg(u, new NetDataReader(plain));
            break;
        }
    }
}

void OnMsg(User u, NetDataReader r)
{
    var type = (Msg)r.GetByte();
    if (type == Msg.Login) { DoLogin(u, r); return; }
    if (!u.LoggedIn) return;
    switch (type)
    {
        case Msg.Join: DoJoin(u, r); break;
        case Msg.AdminLogin: DoAdminLogin(u, r); break;
        case Msg.CreateChannel: DoCreate(u, r); break;
        case Msg.DeleteChannel: DoDelete(u, r); break;
        case Msg.EditChannel: DoEdit(u, r); break;
        case Msg.MoveChannel: DoMove(u, r); break;
        case Msg.SetRank: DoSetRank(u, r); break;
        case Msg.Kick: DoKick(u, r); break;
        case Msg.SetServerPassword: DoServerPwd(u, r); break;
        case Msg.Chat: DoChat(u, r); break;
        case Msg.SetAvatar: DoAvatar(u, r); break;
        case Msg.WatchClients: DoWatchClients(u, r); break;
        case Msg.SetTitle: DoSetTitle(u, r); break;
        case Msg.ClientAction: DoClientAction(u, r); break;
    }
}

void DoLogin(User u, NetDataReader r)
{
    if (u.LoggedIn) return;
    var nick = r.GetString(Wire.MaxNick).Trim();
    var pwd = r.GetString(Wire.MaxPassword);
    var clientId = r.GetString(Wire.MaxClientId).Trim();
    var avatar = r.GetByte();
    if (cfg.ServerPasswordEnabled && !Pw.Same(pwd, cfg.ServerPassword))
    {
        Send(u, x => { x.Put((byte)Msg.LoginFailed); x.Put(pwd.Length == 0 ? "ServerPasswordRequired" : "ServerPasswordWrong"); });
        Fail(u);
        return;
    }
    var key0 = clientId.Length >= 8 ? Pw.KeyOf(clientId) : null;   // bez ClientId nie ma stałego wpisu w rejestrze
    ClientRecord? existing = null;
    if (key0 != null) clients.TryGetValue(key0, out existing);
    if (existing is { Banned: true })
    {
        Console.WriteLine($"Odrzucono zbanowanego klienta: {nick} ({u.Peer.Address})");
        Send(u, x => { x.Put((byte)Msg.LoginFailed); x.Put("Banned"); });
        return;
    }
    if (nick.Length == 0) nick = "User" + u.Peer.Id;
    // ten sam klient (ten sam ClientId) wraca po zerwaniu połączenia: usuwamy jego starą, "wiszącą" sesję
    if (key0 != null)
        foreach (var old in users.Values.Where(o => o != u && o.LoggedIn && o.Key == key0).ToList())
        { old.LoggedIn = false; old.Peer.Disconnect(); }
    // nick musi być unikalny: nie może go używać nikt online ani żaden inny klient z rejestru (także offline)
    bool taken = users.Values.Any(o => o != u && o.LoggedIn && o.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase))
              || clients.Values.Any(c => c != existing && c.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase));
    if (taken)
    {
        Console.WriteLine($"Odrzucono zajęty nick: {nick} ({u.Peer.Address})");
        Send(u, x => { x.Put((byte)Msg.LoginFailed); x.Put("NickTaken"); });
        return;
    }
    var rec0 = key0 == null ? null : GetOrCreateRecord(key0, nick);
    u.Nick = nick; u.LoggedIn = true; u.ChannelId = LobbyId; u.Fails = 0;
    u.Avatar = avatar <= Wire.AvatarCount ? avatar : (byte)0;
    u.Key = key0; u.Rec = rec0;
    ApplyStoredRank(u);
    if (u.Rec != null) { u.Rec.Nick = u.Nick; u.Rec.LastSeen = NowUnix(); SaveClients(); }
    Console.WriteLine($"[+] {u.Nick} ({u.Peer.Address}){(u.Rank != Rank.User ? " [" + u.Rank + "]" : "")}");
    Send(u, x => { x.Put((byte)Msg.Welcome); x.Put(u.Peer.Id); x.PutBytesWithLength(voiceKey); });
    Broadcast();
}

void DoJoin(User u, NetDataReader r)
{
    int id = r.GetInt(); var pwd = r.GetString(Wire.MaxPassword);
    var ch = channels.FirstOrDefault(c => c.Id == id);
    if (ch == null || u.ChannelId == id) return;
    if (!CanEnter(u, ch)) { Err(u, "RankRequired"); return; }
    if (ch.HasPw && !CanManage(u, id) && !ch.Check(pwd)) { Err(u, "WrongChannelPassword"); Fail(u); return; }
    u.Fails = 0; u.ChannelId = id; Broadcast();
}

void DoAdminLogin(User u, NetDataReader r)
{
    var pwd = r.GetString(Wire.MaxPassword);
    if (cfg.AdminPassword == DefaultAdminPwd || cfg.AdminPassword.Length == 0)
    { Err(u, "AdminNotConfigured"); return; }
    bool ok = Pw.Same(pwd, cfg.AdminPassword);
    if (ok) { u.Rank = Rank.Admin; u.AdminChannelId = -1; u.Fails = 0; StoreRank(u); } else Fail(u);   // hasło daje stałą rangę Admin
    Console.WriteLine($"Logowanie admina {(ok ? "OK" : "BŁĄD")}: {u.Nick}");
    Send(u, x => { x.Put((byte)Msg.AdminResult); x.Put(ok); x.Put(cfg.ServerPasswordEnabled); });
    if (ok) Broadcast();
}

void DoCreate(User u, NetDataReader r)
{
    if (!IsAdmin(u)) { NoPerm(u); return; }
    var name = r.GetString(Wire.MaxChannelName).Trim(); var color = r.GetString(16); var pwd = r.GetString(Wire.MaxPassword);
    byte access = r.GetByte();
    if (name.Length == 0 || !colorRx.IsMatch(color)) { Err(u, "InvalidNameOrColor"); return; }
    if (channels.Count >= Wire.MaxChannels) { Err(u, "ChannelLimit"); return; }
    var c = new ChannelData { Id = nextChannelId++, Name = name, Color = color, Access = (access & 7) | 4 };
    c.SetPassword(pwd);
    channels.Add(c); SaveChannels(); Broadcast();
}

void DoDelete(User u, NetDataReader r)
{
    if (!IsAdmin(u)) { NoPerm(u); return; }
    int id = r.GetInt();
    if (id == LobbyId) { Err(u, "LobbyProtected"); return; }
    if (channels.RemoveAll(c => c.Id == id) == 0) return;
    foreach (var o in users.Values)
    {
        if (o.ChannelId == id) o.ChannelId = LobbyId;
        if (o.Rank == Rank.ChannelAdmin && o.AdminChannelId == id) { o.Rank = Rank.User; o.AdminChannelId = -1; }
    }
    bool ranksChanged = false;
    foreach (var rec in clients.Values.Where(x => x.Rank == Rank.ChannelAdmin && x.AdminChannelId == id))
    { rec.Rank = Rank.User; rec.AdminChannelId = -1; ranksChanged = true; }
    if (ranksChanged) SaveClients();
    foreach (var o in users.Values.ToList()) Evict(o);
    SaveChannels(); Broadcast();
}

void DoEdit(User u, NetDataReader r)
{
    int id = r.GetInt();
    var name = r.GetString(Wire.MaxChannelName).Trim(); var color = r.GetString(16);
    byte mode = r.GetByte(); var pwd = r.GetString(Wire.MaxPassword); byte access = r.GetByte();
    if (!CanManage(u, id)) { NoPerm(u); return; }
    var ch = channels.FirstOrDefault(c => c.Id == id);
    if (ch == null || name.Length == 0 || !colorRx.IsMatch(color)) { Err(u, "InvalidChannelData"); return; }
    ch.Name = name; ch.Color = color;
    if (IsAdmin(u) && id != LobbyId)   // dostęp według rang zmienia tylko Admin; Lobby jest zawsze otwarte
    {
        ch.Access = (access & 7) | 4;
        foreach (var o in users.Values.Where(x => x.LoggedIn && x.ChannelId == id).ToList()) Evict(o);
    }
    if (mode == 1) ch.SetPassword(null);                       // usuń hasło
    else if (mode == 2 && pwd.Length > 0) ch.SetPassword(pwd); // ustaw nowe hasło (0 = bez zmian)
    SaveChannels(); Broadcast();
}

void DoMove(User u, NetDataReader r)
{
    if (!IsAdmin(u)) { NoPerm(u); return; }
    int id = r.GetInt(), idx = r.GetInt();
    int cur = channels.FindIndex(c => c.Id == id);
    if (cur < 0) return;
    var ch = channels[cur];
    channels.RemoveAt(cur);
    channels.Insert(Math.Clamp(idx, 0, channels.Count), ch);
    SaveChannels(); Broadcast();
}

void DoSetRank(User u, NetDataReader r)
{
    if (!IsAdmin(u)) { NoPerm(u); return; }
    int uid = r.GetInt(); var rank = (Rank)r.GetByte(); int cid = r.GetInt();
    if (!users.TryGetValue(uid, out var t) || !t.LoggedIn || rank > Rank.Admin) return;
    if (t == u) { Err(u, "OwnRank"); return; }
    if (rank == Rank.ChannelAdmin && !channels.Any(c => c.Id == cid)) return;
    t.Rank = rank; t.AdminChannelId = rank == Rank.ChannelAdmin ? cid : -1;
    StoreRank(t);
    Evict(t);
    Console.WriteLine($"Ranga {rank} dla {t.Nick} (nadał: {u.Nick}){(t.Key == null ? " - tymczasowa (klient bez ClientId)" : ", zapisana na stałe")}");
    Broadcast();
}

void DoKick(User u, NetDataReader r)
{
    int uid = r.GetInt();
    if (!users.TryGetValue(uid, out var t) || !t.LoggedIn || t.ChannelId == LobbyId) return;
    if (!CanManage(u, t.ChannelId) || (t.Rank == Rank.Admin && !IsAdmin(u))) { NoPerm(u); return; }
    t.ChannelId = LobbyId;
    Err(t, "MovedToLobby", u.Nick);
    Broadcast();
}

void DoServerPwd(User u, NetDataReader r)
{
    if (!IsAdmin(u)) { NoPerm(u); return; }
    bool enabled = r.GetBool(); var pwd = r.GetString(Wire.MaxPassword);
    if (enabled && pwd.Length == 0 && cfg.ServerPassword.Length == 0) { Err(u, "EnterServerPassword"); return; }
    cfg.ServerPasswordEnabled = enabled;
    if (pwd.Length > 0) cfg.ServerPassword = pwd;
    SaveConfig();
    Console.WriteLine($"Hasło serwera {(enabled ? "WŁĄCZONE" : "wyłączone")} (zmienił: {u.Nick})");
    Send(u, x => { x.Put((byte)Msg.AdminResult); x.Put(true); x.Put(cfg.ServerPasswordEnabled); });
}

void DoAvatar(User u, NetDataReader r)
{
    var a = r.GetByte();
    if (a > Wire.AvatarCount || a == u.Avatar) return;
    u.Avatar = a;
    Broadcast();
}

void DoChat(User u, NetDataReader r)
{
    var text = r.GetString(Wire.MaxChat).Trim();
    if (text.Length == 0) return;
    foreach (var o in users.Values.Where(x => x.LoggedIn && x.ChannelId == u.ChannelId))
        Send(o, x => { x.Put((byte)Msg.Chat); x.Put(u.Peer.Id); x.Put(text); });
}

// ---------- pomocnicze ----------
bool IsAdmin(User u) => u.Rank == Rank.Admin;
bool CanManage(User u, int channelId) => IsAdmin(u) || (u.Rank == Rank.ChannelAdmin && u.AdminChannelId == channelId);
bool CanEnter(User u, ChannelData ch) =>
    IsAdmin(u) || (u.Rank == Rank.ChannelAdmin && u.AdminChannelId == ch.Id) || Wire.Allows((byte)ch.Access, u.Rank);

// Jeśli ranga użytkownika nie ma już dostępu do jego kanału (zmiana dostępu lub rangi), przenosimy go do Lobby.
void Evict(User o)
{
    var ch = channels.FirstOrDefault(c => c.Id == o.ChannelId);
    if (ch == null || CanEnter(o, ch)) return;
    o.ChannelId = LobbyId;
    Err(o, "AccessRevoked", ch.Name);
}

void NoPerm(User u) => Err(u, "NoPermission");
void Err(User u, string code, string arg = "") => Send(u, x => { x.Put((byte)Msg.Error); x.Put(code); x.Put(arg); });

// ---------- rejestr klientów ----------
long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

ClientRecord GetOrCreateRecord(string key, string nick)
{
    if (clients.TryGetValue(key, out var rec)) return rec;
    rec = new ClientRecord { Id = nextClientId++, Key = key, Nick = nick, FirstSeen = NowUnix(), LastSeen = NowUnix() };
    clients[key] = rec;
    SaveClients();
    return rec;
}

// Stała ranga: zapisana w rejestrze pod kluczem klienta, wraca przy każdym kolejnym połączeniu.
// Hasło admina (DoAdminLogin) też zapisuje rangę Admin na stałe (cofnięcie: Admin zmienia rangę w oknie zarządzania).
void ApplyStoredRank(User u)
{
    var rec = u.Rec;
    if (rec == null) return;
    if (rec.Rank == Rank.Admin) { u.Rank = Rank.Admin; u.AdminChannelId = -1; }
    else if (rec.Rank == Rank.ChannelAdmin && channels.Any(c => c.Id == rec.AdminChannelId))
    { u.Rank = Rank.ChannelAdmin; u.AdminChannelId = rec.AdminChannelId; }
    else if (rec.Rank != Rank.User) { rec.Rank = Rank.User; rec.AdminChannelId = -1; SaveClients(); }   // nieaktualny wpis (np. usunięty kanał)
}

void StoreRank(User t)
{
    if (t.Rec == null) return;
    t.Rec.Rank = t.Rank; t.Rec.AdminChannelId = t.AdminChannelId;
    SaveClients();
}

List<ClientInfo> ClientInfos(User forWho) =>
    clients.Values.OrderBy(r => r.Id).Select(r =>
    {
        var o = users.Values.FirstOrDefault(x => x.LoggedIn && x.Rec == r);
        return new ClientInfo(r.Id, o?.Nick ?? r.Nick, o != null, o?.ChannelId ?? -1,
            o != null ? o.Rank : r.Rank, o != null ? o.AdminChannelId : r.AdminChannelId,
            r.Banned, r.LastSeen, r.FirstSeen, forWho.Rec == r);
    }).ToList();

void SendClients(User admin)
{
    var inner = new NetDataWriter();
    Wire.WriteClients(inner, ClientInfos(admin));
    Transmit(admin, inner.CopyData());
}

// Admini, którzy mają otwartą stronę „Klienci”, dostają odświeżoną listę po każdej zmianie.
void PushClients()
{
    foreach (var a in users.Values.Where(x => x.LoggedIn && x.Watch && IsAdmin(x)).ToList()) SendClients(a);
}

void DoSetTitle(User u, NetDataReader r)
{
    if (!IsAdmin(u)) { NoPerm(u); return; }
    cfg.ChannelsTitle = r.GetString(Wire.MaxTitle).Trim();
    SaveConfig();
    Console.WriteLine($"Nazwa listy kanałów: \"{cfg.ChannelsTitle}\" (ustawił: {u.Nick})");
    Broadcast();
}

void DoWatchClients(User u, NetDataReader r)
{
    bool watch = r.GetBool();
    if (!IsAdmin(u)) { u.Watch = false; return; }
    u.Watch = watch;
    if (watch) SendClients(u);
}

void DoClientAction(User u, NetDataReader r)
{
    if (!IsAdmin(u)) { NoPerm(u); return; }
    int id = r.GetInt(); byte act = r.GetByte(); var rank = (Rank)r.GetByte(); int cid = r.GetInt();
    var rec = clients.Values.FirstOrDefault(x => x.Id == id);
    if (rec == null) return;
    if (rec == u.Rec) { Err(u, "SelfAction"); return; }
    var online = users.Values.FirstOrDefault(o => o.LoggedIn && o.Rec == rec);
    switch (act)
    {
        case Wire.ActSetRank:
            if (rank > Rank.Admin) return;
            if (rank == Rank.ChannelAdmin && !channels.Any(c => c.Id == cid)) return;
            rec.Rank = rank; rec.AdminChannelId = rank == Rank.ChannelAdmin ? cid : -1;
            if (online != null) { online.Rank = rec.Rank; online.AdminChannelId = rec.AdminChannelId; Evict(online); }
            Console.WriteLine($"Ranga {rank} dla {rec.Nick} (nadał: {u.Nick}), zapisana na stałe");
            break;
        case Wire.ActBan:
            if (rec.Rank == Rank.Admin) { Err(u, "CannotBanAdmin"); return; }
            rec.Banned = true;
            Console.WriteLine($"Ban: {rec.Nick} (zbanował: {u.Nick})");
            if (online != null) { Err(online, "Banned"); online.DisconnectAt = Environment.TickCount64 + 600; }
            break;
        case Wire.ActUnban:
            rec.Banned = false;
            Console.WriteLine($"Odbanowano: {rec.Nick} (przez: {u.Nick})");
            break;
        case Wire.ActForget:
            if (online != null) { Err(u, "ClientOnline"); return; }
            clients.Remove(rec.Key);
            Console.WriteLine($"Usunięto z rejestru: {rec.Nick} (przez: {u.Nick})");
            break;
        case Wire.ActKick:
            if (online == null) return;
            Err(online, "KickedFromServer", u.Nick);
            online.DisconnectAt = Environment.TickCount64 + 600;   // chwila, żeby komunikat dotarł
            break;
        default: return;
    }
    SaveClients();
    Broadcast();   // odświeża też listę klientów adminów
}

void PruneClients()
{
    long limit = NowUnix() - 90L * 86400;   // nieaktywni od 90 dni, bez rangi i bez bana
    var old = clients.Where(kv => kv.Value.Rank == Rank.User && !kv.Value.Banned && kv.Value.LastSeen < limit).Select(kv => kv.Key).ToList();
    foreach (var k in old) clients.Remove(k);
    if (old.Count > 0) { SaveClients(); Console.WriteLine($"Usunięto z rejestru {old.Count} nieaktywnych klientów (ponad 90 dni)."); }
}

void Fail(User u)
{
    if (++u.Fails >= 5) { Console.WriteLine($"Zbyt wiele błędnych haseł: {u.Peer.Address}"); u.Peer.Disconnect(); }
}

void Transmit(User u, byte[] inner)
{
    if (u.Ctl == null) return;
    var o = new NetDataWriter();
    o.Put((byte)Pkt.Secure);
    o.Put(Crypto.Seal(u.Ctl, inner));
    u.Peer.Send(o, DeliveryMethod.ReliableOrdered);
}

void Send(User u, Action<NetDataWriter> build)
{
    var inner = new NetDataWriter();
    build(inner);
    Transmit(u, inner.CopyData());
}

void Broadcast()
{
    var chs = channels.Select(c => new ChannelInfo(c.Id, c.Name, c.Color, c.HasPw, (byte)c.Access)).ToList();
    var us = users.Values.Where(x => x.LoggedIn).Select(x => new UserInfo(x.Peer.Id, x.Nick, x.ChannelId, x.Rank, x.AdminChannelId, x.Avatar)).ToList();
    var inner = new NetDataWriter();
    Wire.WriteState(inner, chs, us, cfg.ChannelsTitle);
    var bytes = inner.CopyData();
    foreach (var o in users.Values.Where(x => x.LoggedIn)) Transmit(o, bytes);
    PushClients();
}

List<ChannelData> LoadChannels()
{
    if (File.Exists(chPath))
    {
        var l = JsonSerializer.Deserialize<List<ChannelData>>(File.ReadAllText(chPath));
        if (l is { Count: > 0 } && l.Any(c => c.Id == LobbyId)) return l;
    }
    var def = new List<ChannelData>
    {
        new() { Id = 1, Name = "Lobby", Color = "#3DDC57" }, new() { Id = 2, Name = "Gaming", Color = "#3B82F6" },
        new() { Id = 3, Name = "Rozmowy", Color = "#A855F7" }, new() { Id = 4, Name = "AFK", Color = "#F5C518" }
    };
    File.WriteAllText(chPath, JsonSerializer.Serialize(def, json));
    return def;
}

Dictionary<string, ClientRecord> LoadClients()
{
    var result = new Dictionary<string, ClientRecord>();
    try
    {
        if (File.Exists(clientsPath))
        {
            var list = JsonSerializer.Deserialize<List<ClientRecord>>(File.ReadAllText(clientsPath)) ?? new();
            foreach (var r in list.Where(x => !string.IsNullOrEmpty(x.Key))) result.TryAdd(r.Key, r);
        }
        else if (File.Exists(ranksPath))   // migracja ze starego ranks.json
        {
            var old = JsonSerializer.Deserialize<Dictionary<string, RankRecord>>(File.ReadAllText(ranksPath)) ?? new();
            long now = NowUnix();
            foreach (var (key, rr) in old)
                result[key] = new ClientRecord { Key = key, Nick = rr.Nick, Rank = rr.Rank, AdminChannelId = rr.AdminChannelId, FirstSeen = now, LastSeen = now };
            Console.WriteLine($"Zaimportowano {old.Count} rang z {ranksPath} do {clientsPath}.");
        }
    }
    catch (Exception ex) { Console.WriteLine($"Nie można odczytać rejestru klientów: {ex.Message}"); }
    int next = result.Count == 0 ? 1 : Math.Max(1, result.Values.Max(r => r.Id) + 1);
    var seen = new HashSet<int>();
    foreach (var r in result.Values)
        if (r.Id <= 0 || !seen.Add(r.Id)) { r.Id = next++; seen.Add(r.Id); }
    try { File.WriteAllText(clientsPath, JsonSerializer.Serialize(result.Values.OrderBy(r => r.Id).ToList(), json)); } catch { }
    return result;
}

void SaveClients()
{
    try { File.WriteAllText(clientsPath, JsonSerializer.Serialize(clients.Values.OrderBy(r => r.Id).ToList(), json)); }
    catch (Exception ex) { Console.WriteLine($"Nie można zapisać {clientsPath}: {ex.Message}"); }
}

void SaveChannels() => File.WriteAllText(chPath, JsonSerializer.Serialize(channels, json));
void SaveConfig() => File.WriteAllText(cfgPath, JsonSerializer.Serialize(cfg, json));

// ---------- start ----------
if (cfg.AdminPassword == DefaultAdminPwd)
    Console.WriteLine("UWAGA: AdminPassword w config.json ma wartość domyślną. Logowanie admina jest zablokowane do czasu jej zmiany.");
server.Start(cfg.Port);
Console.WriteLine($"Serwer działa na UDP {cfg.Port} (max {cfg.MaxUsers} osób, hasło serwera: {(cfg.ServerPasswordEnabled ? "TAK" : "nie")}). Ctrl+C aby zakończyć.");

bool running = true;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; running = false; };
AppDomain.CurrentDomain.ProcessExit += (_, _) => running = false;

long lastCheck = 0;
while (running)
{
    server.PollEvents();
    long now = Environment.TickCount64;
    if (now - lastCheck > 250)
    {
        lastCheck = now;
        // rozłączamy połączenia, które nie zalogowały się w 10 s
        foreach (var u in users.Values.ToList())
        {
            if (!u.LoggedIn && now - u.Since > 10000) u.Peer.Disconnect();
            else if (u.DisconnectAt != 0 && now >= u.DisconnectAt) { u.DisconnectAt = 0; u.Peer.Disconnect(); }
        }
    }
    Thread.Sleep(1);
}
server.Stop();

class User(NetPeer peer)
{
    public NetPeer Peer { get; } = peer;
    public AesGcm? Ctl { get; set; }
    public string Nick { get; set; } = "";
    public string? Key { get; set; }          // SHA-256 z ClientId (stała tożsamość klienta)
    public ClientRecord? Rec { get; set; }    // wpis w rejestrze klientów (null, gdy klient nie ma ClientId)
    public long DisconnectAt { get; set; }    // 0 = brak; inaczej moment (TickCount64), po którym rozłączamy
    public bool Watch { get; set; }           // Admin ma otwartą stronę „Klienci"
    public bool LoggedIn { get; set; }
    public Rank Rank { get; set; } = Rank.User;
    public int AdminChannelId { get; set; } = -1;
    public byte Avatar { get; set; }
    public int ChannelId { get; set; }
    public int Fails { get; set; }
    public long Since { get; } = Environment.TickCount64;
}

class RankRecord
{
    public string Nick { get; set; } = "";   // tylko dla czytelności pliku
    public Rank Rank { get; set; }
    public int AdminChannelId { get; set; } = -1;
}

class ClientRecord
{
    public int Id { get; set; }                       // numer wpisu w rejestrze (używany w poleceniach admina)
    public string Key { get; set; } = "";             // SHA-256 z ClientId
    public string Nick { get; set; } = "";            // ostatni znany nick
    public Rank Rank { get; set; }
    public int AdminChannelId { get; set; } = -1;
    public bool Banned { get; set; }
    public long FirstSeen { get; set; }               // czas uniksowy (s)
    public long LastSeen { get; set; }
}

class ChannelData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3B82F6";
    public int Access { get; set; } = 7;      // maska rang uprawnionych do wejścia (1 = User, 2 = Channel Admin, 4 = Admin)
    public string? Salt { get; set; }
    public string? Hash { get; set; }
    [JsonIgnore] public bool HasPw => Hash != null;

    public void SetPassword(string? pwd)
    {
        if (string.IsNullOrEmpty(pwd)) { Salt = null; Hash = null; return; }
        var s = RandomNumberGenerator.GetBytes(16);
        Salt = Convert.ToBase64String(s);
        Hash = Convert.ToBase64String(Pw.Hash(pwd, s));
    }

    public bool Check(string pwd) =>
        Hash == null || Salt == null ||
        CryptographicOperations.FixedTimeEquals(Pw.Hash(pwd, Convert.FromBase64String(Salt)), Convert.FromBase64String(Hash));
}

static class Pw
{
    public static byte[] Hash(string pwd, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(pwd, salt, 20000, HashAlgorithmName.SHA256, 32);

    public static string KeyOf(string clientId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("voxly-client:" + clientId)));

    public static bool Same(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}

class Config
{
    public int Port { get; set; } = 9050;
    public string AdminPassword { get; set; } = "ZMIEN-MNIE-haslo-admina";
    public int MaxUsers { get; set; } = 30;
    public bool ServerPasswordEnabled { get; set; } = false;
    public string ServerPassword { get; set; } = "";
    public string ChannelsTitle { get; set; } = "";   // własna nazwa listy kanałów (pusta = domyślna); ustawia Admin w kliencie
}
