using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using LiteNetLib.Utils;

namespace VoiceShared;

public enum Rank : byte { User = 0, ChannelAdmin = 1, Admin = 2 }

// Typ pakietu UDP (pierwszy bajt).
public enum Pkt : byte
{
    Audio = 1,       // [Audio][seq u16][nonce12|tag16|Opus]  (C->S)   |  [Audio][ID nadawcy u16][seq u16][...] (S->C)
    Hello = 2,       // C->S, jawnie: klucz publiczny ECDH klienta
    HelloReply = 3,  // S->C, jawnie: klucz publiczny ECDH serwera + czy serwer wymaga hasła
    Secure = 4       // oba kierunki: [nonce12|tag16|zaszyfrowane (Msg + dane)], klucz sesji z ECDH
}

// Komunikaty sterujące (wewnątrz Pkt.Secure).
public enum Msg : byte
{
    Login = 1,              // C->S: nick, hasło serwera, stały identyfikator klienta (ClientId), awatar (byte)
    Welcome = 2,            // S->C: ID, klucz głosu (32 B)
    LoginFailed = 3,        // S->C: kod błędu (np. ServerPasswordWrong)
    State = 4,              // S->C: kanały + użytkownicy
    Join = 5,               // C->S: id kanału, hasło kanału
    AdminLogin = 6,         // C->S: hasło administratora
    AdminResult = 7,        // S->C: ok, czy serwer wymaga hasła
    CreateChannel = 8,      // C->S: nazwa, kolor, hasło, dostęp (maska rang)  (Admin)
    DeleteChannel = 9,      // C->S: id                             (Admin)
    EditChannel = 10,       // C->S: id, nazwa, kolor, tryb hasła, hasło, dostęp  (Admin / Channel Admin tego kanału; dostęp zmienia tylko Admin)
    MoveChannel = 11,       // C->S: id, nowy indeks                (Admin)
    SetRank = 12,           // C->S: id użytkownika, ranga, id kanału (Admin)
    Kick = 13,              // C->S: id użytkownika (do Lobby)      (Admin / Channel Admin)
    SetServerPassword = 14, // C->S: włączone, hasło                (Admin)
    Chat = 15,              // C->S: tekst | S->C: id nadawcy, tekst
    Error = 16,             // S->C: kod błędu, argument (klient tłumaczy kod na swój język)
    SetAvatar = 17,         // C->S: numer awatara (0 = brak, 1..Wire.AvatarCount)
    WatchClients = 18,      // C->S: włączone (Admin): serwer wysyła i odświeża listę klientów
    ClientAction = 19,      // C->S: id klienta, akcja (Wire.Act*), ranga, id kanału   (Admin)
    ClientList = 20,        // S->C: rejestr klientów (online i offline)
    SetTitle = 21,          // C->S: nazwa listy kanałów widoczna u wszystkich (Admin; pusta = domyślna)
    MoveUser = 22,          // C->S: id użytkownika, id kanału (Admin przenosi innych)
    Dm = 23,                // C->S: id odbiorcy, tekst | S->C: id nadawcy, nick, tekst (wiadomość prywatna)
    CallInvite = 24,        // C->S: id adresata | S->C: id dzwoniącego, nick (rozmowa głosowa prywatna 1:1)
    CallReply = 25,         // C->S: id dzwoniącego, przyjęte? | S->C: id adresata, przyjęte?
    CallEnd = 26,           // C->S: zakończ/anuluj rozmowę | S->C: id rozmówcy, który zakończył
    SetAfk = 27,            // C->S: bezczynny od 2 min? (serwer rozsyła w stanie użytkowników)
    SetMaxUsers = 28,       // C->S: maksymalna liczba użytkowników na serwerze (Admin, 1..Wire.MaxUsersLimit)
    WatchLog = 29,          // C->S: włączone (Admin): serwer wysyła dziennik zdarzeń i na bieżąco nowe wpisy
    LogList = 30,           // S->C: dziennik (ostatnie wpisy, od najstarszego)
    LogEntry = 31,          // S->C: jeden nowy wpis dziennika
    SetGame = 32,           // C->S: nazwa gry z listy klienta (pusta = nie gra). Starsze serwery ignorują nieznany komunikat
    GameList = 33,          // S->C: [liczba u16][id użytkownika, nazwa gry]... tylko osoby, które grają. Starsze klienty ignorują
    SetAvatarImage = 34,    // C->S: własny awatar: spakowane (Deflate) piksele 64x64 BGRA, bajty z długością (puste = usuń). Starsze serwery ignorują
    AvatarImage = 35        // S->C: id użytkownika, dane jak wyżej (puste = usunięty). Wysyłane przy zmianie i nowo zalogowanym. Starsze klienty ignorują
}

public record ChannelInfo(int Id, string Name, string Color, bool HasPassword, byte Access);
/// <summary>Wpis w rejestrze klientów serwera (online lub offline). Id to numer wpisu w rejestrze, nie ID połączenia.</summary>
public record ClientInfo(int Id, string Nick, bool Online, int ChannelId, Rank Rank, int AdminChannelId, bool Banned, long LastSeen, long FirstSeen, bool You);
/// <summary>Wpis dziennika zdarzeń serwera. Kind: 1 = wejście, 2 = wyjście, 3 = utrata połączenia, 4 = rozłączony przez admina.</summary>
public record LogInfo(long Time, string Nick, byte Kind);
public record UserInfo(int Id, string Nick, int ChannelId, Rank Rank, int AdminChannelId, byte Avatar, bool Afk);

public static class Wire
{
    public const string ProtocolKey = "VOXLY-9";
    public const int MaxNick = 24, MaxChannelName = 32, MaxChat = 300, MaxChannels = 30, MaxPassword = 64, MaxClientId = 64, MaxTitle = 40, MaxDm = 500, MaxUsersLimit = 200, AvatarCount = 19;

    /// <summary>Maska dostępu do kanału: bit 1 = User, bit 2 = Channel Admin, bit 4 = Admin (Admin zawsze ma dostęp).</summary>
    public const byte AccessAll = 7;
    public static bool Allows(byte mask, Rank rank) => (mask & (1 << (int)rank)) != 0;

    public static void WriteState(NetDataWriter w, IReadOnlyList<ChannelInfo> channels, IReadOnlyList<UserInfo> users, string title, int maxUsers, string serverVersion)
    {
        w.Put((byte)Msg.State);
        w.Put((ushort)channels.Count);
        foreach (var c in channels) { w.Put(c.Id); w.Put(c.Name); w.Put(c.Color); w.Put(c.HasPassword); w.Put(c.Access); }
        w.Put((ushort)users.Count);
        foreach (var u in users) { w.Put(u.Id); w.Put(u.Nick); w.Put(u.ChannelId); w.Put((byte)u.Rank); w.Put(u.AdminChannelId); w.Put(u.Avatar); w.Put(u.Afk); }
        w.Put(title); w.Put(maxUsers); w.Put(serverVersion);
    }

    // Wywołaj po odczytaniu bajtu Msg.State.
    public static (List<ChannelInfo>, List<UserInfo>, string, int, string) ReadState(NetDataReader r)
    {
        var channels = new List<ChannelInfo>();
        var users = new List<UserInfo>();
        int cc = r.GetUShort();
        for (int i = 0; i < cc; i++) channels.Add(new ChannelInfo(r.GetInt(), r.GetString(), r.GetString(), r.GetBool(), r.GetByte()));
        int uc = r.GetUShort();
        for (int i = 0; i < uc; i++) users.Add(new UserInfo(r.GetInt(), r.GetString(), r.GetInt(), (Rank)r.GetByte(), r.GetInt(), r.GetByte(), r.GetBool()));
        return (channels, users, r.GetString(), r.GetInt(), r.GetString());
    }

    // ---- własny awatar: surowe piksele 64x64 (premultiplied BGRA), więc odbiorca nie dekoduje żadnego formatu obrazu (nic do wykorzystania w pliku) ----
    public const int AvatarPx = 64, AvatarRaw = AvatarPx * AvatarPx * 4, MaxAvatarPacked = 24000;

    public static byte[] PackAvatar(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.SmallestSize, true)) ds.Write(raw, 0, raw.Length);
        return ms.ToArray();
    }

    /// <summary>Rozpakowuje awatar. Zwraca null, gdy dane są uszkodzone albo nie mają dokładnie AvatarRaw bajtów.</summary>
    public static byte[]? UnpackAvatar(byte[] packed)
    {
        if (packed.Length == 0 || packed.Length > MaxAvatarPacked) return null;
        try
        {
            using var ds = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress);
            var buf = new byte[AvatarRaw];
            int n = 0;
            while (n < AvatarRaw) { int r = ds.Read(buf, n, AvatarRaw - n); if (r == 0) break; n += r; }
            if (n != AvatarRaw || ds.ReadByte() != -1) return null;
            return buf;
        }
        catch { return null; }
    }

    public const int MaxGame = 40;

    /// <summary>Czyści nazwę gry z sieci: bez znaków sterujących, przycięta do MaxGame.</summary>
    public static string CleanGame(string s)
    {
        var t = new string((s ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return t.Length > MaxGame ? t[..MaxGame] : t;
    }

    public static void WriteGames(NetDataWriter w, IReadOnlyList<(int Id, string Game)> list)
    {
        w.Put((byte)Msg.GameList);
        w.Put((ushort)list.Count);
        foreach (var (id, game) in list) { w.Put(id); w.Put(game); }
    }

    // Wywołaj po odczytaniu bajtu Msg.GameList.
    public static Dictionary<int, string> ReadGames(NetDataReader r)
    {
        var d = new Dictionary<int, string>();
        int n = r.GetUShort();
        for (int i = 0; i < n; i++) { int id = r.GetInt(); d[id] = CleanGame(r.GetString(MaxGame * 4)); }
        return d;
    }

    // ---- stan serwera bez logowania (lista serwerów na ekranie łączenia) ----
    // Wiadomości bez połączenia (LiteNetLib): zapytanie [StatusQuery][nonce], odpowiedź [StatusReply][nonce][online][max][hasło?][nazwa][wersja].
    // Serwer ujawnia tylko to, co i tak widać po wejściu: liczbę osób, limit, czy jest hasło, nazwę i wersję.
    public const string StatusQuery = "VOXLY-STATUS?", StatusReply = "VOXLY-STATUS!";

    public static void WriteStatusQuery(NetDataWriter w, int nonce) { w.Put(StatusQuery); w.Put(nonce); }

    public static void WriteStatusReply(NetDataWriter w, int nonce, int online, int max, bool password, string title, string version)
    {
        w.Put(StatusReply); w.Put(nonce); w.Put((ushort)Math.Clamp(online, 0, ushort.MaxValue)); w.Put((ushort)Math.Clamp(max, 0, ushort.MaxValue));
        w.Put(password); w.Put(title ?? ""); w.Put(version ?? "");
    }

    public static bool TryReadStatusReply(NetDataReader r, out int nonce, out int online, out int max, out bool password, out string title, out string version)
    {
        nonce = online = max = 0; password = false; title = version = "";
        try
        {
            if (r.GetString(32) != StatusReply) return false;
            nonce = r.GetInt(); online = r.GetUShort(); max = r.GetUShort(); password = r.GetBool();
            title = r.GetString(MaxTitle * 4); version = r.GetString(32);
            return true;
        }
        catch { return false; }
    }

    // akcje Admina na kliencie (Msg.ClientAction)
    public const byte ActSetRank = 1, ActBan = 2, ActUnban = 3, ActForget = 4, ActKick = 5;

    public static void WriteClients(NetDataWriter w, IReadOnlyList<ClientInfo> list)
    {
        w.Put((byte)Msg.ClientList);
        int n = Math.Min(list.Count, 2000);
        w.Put((ushort)n);
        for (int i = 0; i < n; i++)
        {
            var c = list[i];
            w.Put(c.Id); w.Put(c.Nick); w.Put(c.Online); w.Put(c.ChannelId); w.Put((byte)c.Rank); w.Put(c.AdminChannelId);
            w.Put(c.Banned); w.Put(c.LastSeen); w.Put(c.FirstSeen); w.Put(c.You);
        }
    }

    public static void WriteLog(NetDataWriter w, IReadOnlyList<LogInfo> list, int max)
    {
        w.Put((byte)Msg.LogList);
        int n = Math.Min(list.Count, max);
        w.Put((ushort)n);
        for (int i = list.Count - n; i < list.Count; i++) { w.Put(list[i].Time); w.Put(list[i].Nick); w.Put(list[i].Kind); }
    }

    // Wywołaj po odczytaniu bajtu Msg.LogList.
    public static List<LogInfo> ReadLog(NetDataReader r)
    {
        var list = new List<LogInfo>();
        int n = r.GetUShort();
        for (int i = 0; i < n; i++) list.Add(new LogInfo(r.GetLong(), r.GetString(), r.GetByte()));
        return list;
    }

    // Wywołaj po odczytaniu bajtu Msg.ClientList.
    public static List<ClientInfo> ReadClients(NetDataReader r)
    {
        var list = new List<ClientInfo>();
        int n = r.GetUShort();
        for (int i = 0; i < n; i++)
            list.Add(new ClientInfo(r.GetInt(), r.GetString(), r.GetBool(), r.GetInt(), (Rank)r.GetByte(), r.GetInt(),
                r.GetBool(), r.GetLong(), r.GetLong(), r.GetBool()));
        return list;
    }
}

public static class Crypto
{
    public static ECDiffieHellman NewKey() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>Klucz sesji (32 B) = SHA-256 ze wspólnego sekretu ECDH.</summary>
    public static byte[] Derive(ECDiffieHellman mine, byte[] otherPublicKey)
    {
        using var other = ECDiffieHellman.Create();
        other.ImportSubjectPublicKeyInfo(otherPublicKey, out _);
        return mine.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256);
    }

    /// <summary>AES-256-GCM: [nonce 12][tag 16][szyfrogram].</summary>
    public static byte[] Seal(AesGcm aes, ReadOnlySpan<byte> plain)
    {
        var res = new byte[28 + plain.Length];
        RandomNumberGenerator.Fill(res.AsSpan(0, 12));
        aes.Encrypt(res.AsSpan(0, 12), plain, res.AsSpan(28), res.AsSpan(12, 16));
        return res;
    }

    public static byte[]? Open(AesGcm aes, ReadOnlySpan<byte> data)
    {
        if (data.Length < 28) return null;
        var plain = new byte[data.Length - 28];
        try { aes.Decrypt(data[..12], data[28..], data[12..28], plain); }
        catch { return null; }   // zły klucz lub uszkodzony pakiet
        return plain;
    }
}
