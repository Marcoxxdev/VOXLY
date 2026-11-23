using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Concentus.Enums;
using Concentus.Structs;
using LiteNetLib;
using LiteNetLib.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceShared;

namespace VoiceClient;

/// <summary>Sieć + audio: mikrofon -> Opus -> AES-GCM -> UDP i odwrotnie (osobny dekoder i bufor na każdego rozmówcę).</summary>
public sealed class VoiceEngine : IDisposable
{
    public const int Rate = 48000, Frame = 960;   // ramka 20 ms

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    public event Action<List<ChannelInfo>, List<UserInfo>>? StateChanged;
    public event Action<int>? Connected;
    public event Action<string>? Disconnected;
    public event Action<int, string>? ChatReceived;
    public event Action<bool, bool>? AdminResult;   // (ok, serwer wymaga hasła)
    public event Action<string, string>? Error;   // (kod błędu, argument) - tłumaczy Loc.ServerError
    public event Action<int, string, string>? DmReceived;       // (id nadawcy, nick, tekst)
    public event Action<int, string>? CallIncoming;            // (id dzwoniącego, nick)
    public event Action<int, bool>? CallReplied;               // (id adresata, przyjął?)
    public event Action<int>? CallEnded;                       // rozmówca zakończył rozmowę / anulował dzwonienie
    public event Action<string>? TitleChanged;                 // własna nazwa listy kanałów (pusta = domyślna)
    public event Action<List<ClientInfo>>? ClientsChanged;   // rejestr klientów (tylko dla Admina, po WatchClients)

    public AppSettings Settings { get; }
    public bool MicMuted { get; set; }
    public bool Deafened { get; set; }
    public float MicLevel { get; private set; }
    public int MyId { get; private set; }
    public bool IsConnected { get; private set; }
    public int Ping => _net?.FirstPeer?.Ping ?? 0;
    public float Loss => _net?.FirstPeer?.Statistics.PacketLossPercent ?? 0;

    NetManager? _net;
    EventBasedNetListener? _listener;
    volatile bool _closing;
    string _nick = "", _password = "";
    ECDiffieHellman? _ecdh;
    AesGcm? _ctl;                       // klucz sesji sterowania (ECDH)
    readonly object _ctlLock = new();
    AesGcm? _txAes, _rxAes;             // klucz głosu wydany przez serwer

    // audio
    WaveInEvent? _in;
    WaveOutEvent? _out;
    MixingSampleProvider? _mixer;
    VolumeSampleProvider? _volume;
    OpusEncoder? _enc;
    readonly short[] _frame = new short[Frame];
    int _fill;
    readonly byte[] _opusBuf = new byte[1000];
    readonly NetDataWriter _wtx = new();
    ushort _seq;
    int _hang;
    long _lastTx;
    readonly ConcurrentDictionary<int, Voice> _voices = new();

    // Test mikrofonu: własny głos wraca do słuchawek po przejściu przez kodek Opus (tak, jak słyszą Cię inni).
    // Głośność poszczególnych rozmówców (tylko lokalnie): ID użytkownika -> mnożnik (0 = wyciszony, 1 = 100 %).
    readonly ConcurrentDictionary<int, float> _gains = new();
    public void SetUserGain(int userId, float gain)
    {
        _gains[userId] = gain;
        if (_voices.TryGetValue(userId, out var v)) v.Gate.Gain = gain;
    }

    volatile bool _loopback;
    Voice? _loop;
    public bool Loopback { get => _loopback; set => _loopback = value; }

    public VoiceEngine(AppSettings s) => Settings = s;

    // ---------- urządzenia ----------
    public static List<string> InputDevices()
    {
        var l = new List<string> { Loc.T("default_device") };
        for (int i = 0; i < WaveInEvent.DeviceCount; i++) l.Add(WaveInEvent.GetCapabilities(i).ProductName);
        return l;
    }
    public static List<string> OutputDevices()
    {
        var l = new List<string> { Loc.T("default_device") };
        for (int i = 0; i < WaveOut.DeviceCount; i++) l.Add(WaveOut.GetCapabilities(i).ProductName);
        return l;
    }
    static int IndexIn(string name)
    {
        for (int i = 0; i < WaveInEvent.DeviceCount; i++) if (WaveInEvent.GetCapabilities(i).ProductName == name) return i;
        return -1;
    }
    static int IndexOut(string name)
    {
        for (int i = 0; i < WaveOut.DeviceCount; i++) if (WaveOut.GetCapabilities(i).ProductName == name) return i;
        return -1;
    }

    // ---------- połączenie ----------
    public void Connect(string host, int port, string password, string nick)
    {
        Disconnect();
        _closing = false;
        _nick = nick;
        _password = password;
        _ecdh = Crypto.NewKey();

        _listener = new EventBasedNetListener();
        _net = new NetManager(_listener) { UnsyncedEvents = true, DisconnectTimeout = 8000 };

        _listener.PeerConnectedEvent += peer =>
        {
            if (_closing) return;
            // 1. wymiana kluczy (ECDH), dopiero potem logowanie
            var w = new NetDataWriter();
            w.Put((byte)Pkt.Hello);
            w.PutBytesWithLength(_ecdh!.ExportSubjectPublicKeyInfo());
            peer.Send(w, DeliveryMethod.ReliableOrdered);
        };
        _listener.PeerDisconnectedEvent += (peer, info) =>
        {
            if (_closing) return;
            IsConnected = false;
            StopAudio();
            Disconnected?.Invoke(info.Reason.ToString());
        };
        _listener.NetworkReceiveEvent += (peer, r, ch, dm) =>
        {
            try { if (!_closing) OnPacket(r); }
            catch { /* uszkodzony pakiet - ignorujemy */ }
            finally { r.Recycle(); }
        };

        _net.Start();
        _net.Connect(host, port, Wire.ProtocolKey);
    }

    public void Disconnect()
    {
        _gains.Clear();
        _closing = true;
        IsConnected = false;
        try { _net?.Stop(true); } catch { }
        _net = null;
        StopAudio();
        lock (_ctlLock) { _ctl?.Dispose(); _ctl = null; }
        _txAes?.Dispose(); _rxAes?.Dispose(); _txAes = _rxAes = null;
        _ecdh?.Dispose(); _ecdh = null;
    }

    void OnPacket(NetPacketReader r)
    {
        var type = (Pkt)r.GetByte();
        switch (type)
        {
            case Pkt.Audio: OnAudio(r); break;
            case Pkt.HelloReply:
            {
                var serverPub = r.GetBytesWithLength();
                r.GetBool();   // czy serwer wymaga hasła (informacyjnie; decyzja po stronie serwera przy logowaniu)
                if (_ecdh == null) return;
                var key = Crypto.Derive(_ecdh, serverPub);
                lock (_ctlLock) _ctl = new AesGcm(key, 16);
                SendCtl(w => { w.Put((byte)Msg.Login); w.Put(_nick); w.Put(_password); w.Put(Settings.ClientId); w.Put((byte)Settings.Avatar); });
                break;
            }
            case Pkt.Secure:
            {
                byte[]? plain;
                lock (_ctlLock) plain = _ctl == null ? null : Crypto.Open(_ctl, r.GetRemainingBytes());
                if (plain != null) OnMsg(new NetDataReader(plain));
                break;
            }
        }
    }

    void OnMsg(NetDataReader r)
    {
        var type = (Msg)r.GetByte();
        switch (type)
        {
            case Msg.Welcome:
            {
                MyId = r.GetInt();
                var voiceKey = r.GetBytesWithLength();
                _txAes = new AesGcm(voiceKey, 16);
                _rxAes = new AesGcm(voiceKey, 16);
                IsConnected = true;
                StartAudio();
                Connected?.Invoke(MyId);
                break;
            }
            case Msg.LoginFailed: Disconnected?.Invoke("LoginFailed|" + r.GetString()); break;
            case Msg.State:
            {
                var (c, u, title) = Wire.ReadState(r);
                var alive = u.Select(x => x.Id).ToHashSet();
                foreach (var id in _voices.Keys.Where(k => !alive.Contains(k)).ToList())
                    if (_voices.TryRemove(id, out var v)) _mixer?.RemoveMixerInput(v.Gate);
                foreach (var gid in _gains.Keys.Where(k => !alive.Contains(k)).ToList()) _gains.TryRemove(gid, out _);
                StateChanged?.Invoke(c, u);
                TitleChanged?.Invoke(title);
                break;
            }
            case Msg.AdminResult: { bool ok = r.GetBool(); AdminResult?.Invoke(ok, r.GetBool()); break; }
            case Msg.Chat: { int id = r.GetInt(); ChatReceived?.Invoke(id, r.GetString()); break; }
            case Msg.Dm: { int id = r.GetInt(); string nick = r.GetString(); DmReceived?.Invoke(id, nick, r.GetString()); break; }
            case Msg.CallInvite: { int id = r.GetInt(); CallIncoming?.Invoke(id, r.GetString()); break; }
            case Msg.CallReply: { int id = r.GetInt(); CallReplied?.Invoke(id, r.GetBool()); break; }
            case Msg.CallEnd: CallEnded?.Invoke(r.GetInt()); break;
            case Msg.ClientList: ClientsChanged?.Invoke(Wire.ReadClients(r)); break;
            case Msg.Error: { var code = r.GetString(); Error?.Invoke(code, r.GetString()); break; }
        }
    }

    // ---------- polecenia do serwera (szyfrowane kluczem sesji) ----------
    void SendCtl(Action<NetDataWriter> build)
    {
        var inner = new NetDataWriter();
        build(inner);
        byte[] sealedData;
        lock (_ctlLock)
        {
            if (_ctl == null) return;
            sealedData = Crypto.Seal(_ctl, inner.CopyData());
        }
        var w = new NetDataWriter();
        w.Put((byte)Pkt.Secure);
        w.Put(sealedData);
        _net?.FirstPeer?.Send(w, DeliveryMethod.ReliableOrdered);
    }

    public void Join(int channelId, string password = "") => SendCtl(w => { w.Put((byte)Msg.Join); w.Put(channelId); w.Put(password); });
    public void AdminLogin(string pwd) => SendCtl(w => { w.Put((byte)Msg.AdminLogin); w.Put(pwd); });
    public void CreateChannel(string name, string color, string pwd, byte access) => SendCtl(w => { w.Put((byte)Msg.CreateChannel); w.Put(name); w.Put(color); w.Put(pwd); w.Put(access); });
    public void DeleteChannel(int id) => SendCtl(w => { w.Put((byte)Msg.DeleteChannel); w.Put(id); });
    public void EditChannel(int id, string name, string color, byte pwdMode, string pwd, byte access) =>
        SendCtl(w => { w.Put((byte)Msg.EditChannel); w.Put(id); w.Put(name); w.Put(color); w.Put(pwdMode); w.Put(pwd); w.Put(access); });
    public void MoveChannel(int id, int newIndex) => SendCtl(w => { w.Put((byte)Msg.MoveChannel); w.Put(id); w.Put(newIndex); });
    public void SetRank(int userId, Rank rank, int channelId) => SendCtl(w => { w.Put((byte)Msg.SetRank); w.Put(userId); w.Put((byte)rank); w.Put(channelId); });
    public void Kick(int userId) => SendCtl(w => { w.Put((byte)Msg.Kick); w.Put(userId); });
    public void SetServerPassword(bool enabled, string pwd) => SendCtl(w => { w.Put((byte)Msg.SetServerPassword); w.Put(enabled); w.Put(pwd); });
    public void MoveUser(int userId, int channelId) => SendCtl(w => { w.Put((byte)Msg.MoveUser); w.Put(userId); w.Put(channelId); });
    public void SendDm(int toId, string text) => SendCtl(w => { w.Put((byte)Msg.Dm); w.Put(toId); w.Put(text); });
    public void CallInvite(int toId) => SendCtl(w => { w.Put((byte)Msg.CallInvite); w.Put(toId); });
    public void CallAnswer(int fromId, bool accept) => SendCtl(w => { w.Put((byte)Msg.CallReply); w.Put(fromId); w.Put(accept); });
    public void CallEnd() => SendCtl(w => w.Put((byte)Msg.CallEnd));
    public void SetAfk(bool afk) => SendCtl(w => { w.Put((byte)Msg.SetAfk); w.Put(afk); });
    /// <summary>Numer wybranego urządzenia wyjściowego (do dźwięków powiadomień).</summary>
    public int OutDeviceIndex => IndexOut(Settings.SpeakerDevice);
    public void SetTitle(string title) => SendCtl(w => { w.Put((byte)Msg.SetTitle); w.Put(title); });
    public void WatchClients(bool watch) => SendCtl(w => { w.Put((byte)Msg.WatchClients); w.Put(watch); });
    public void ClientAction(int clientId, byte action, Rank rank, int channelId) =>
        SendCtl(w => { w.Put((byte)Msg.ClientAction); w.Put(clientId); w.Put(action); w.Put((byte)rank); w.Put(channelId); });
    public void SetAvatar(int id) => SendCtl(w => { w.Put((byte)Msg.SetAvatar); w.Put((byte)id); });
    public void SendChat(string text) => SendCtl(w => { w.Put((byte)Msg.Chat); w.Put(text); });

    // ---------- audio: start/stop ----------
    public void RestartAudio() { if (IsConnected) { StopAudio(); StartAudio(); } }

    public void ApplyVolume() { if (_volume != null) _volume.Volume = Deafened ? 0f : (float)Settings.Volume; }

    void StartAudio()
    {
        StopAudio();
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1)) { ReadFully = true };
        _volume = new VolumeSampleProvider(_mixer);
        _loop = new Voice();
        _mixer.AddMixerInput(_loop.Gate);
        ApplyVolume();
        try
        {
            _out = new WaveOutEvent { DeviceNumber = IndexOut(Settings.SpeakerDevice), DesiredLatency = 80 };
            _out.Init(new SampleToWaveProvider(_volume));
            _out.Play();
        }
        catch (Exception ex) { Error?.Invoke("SpkFail", ex.Message); }
        try
        {
            _enc = new OpusEncoder(Rate, 1, OpusApplication.OPUS_APPLICATION_VOIP) { Bitrate = 32000 };
            _fill = 0;
            _in = new WaveInEvent { DeviceNumber = IndexIn(Settings.MicDevice), WaveFormat = new WaveFormat(Rate, 16, 1), BufferMilliseconds = 20 };
            _in.DataAvailable += OnMic;
            _in.StartRecording();
        }
        catch (Exception ex) { Error?.Invoke("MicFail", ex.Message); }
    }

    void StopAudio()
    {
        try { if (_in != null) { _in.DataAvailable -= OnMic; _in.StopRecording(); _in.Dispose(); } } catch { }
        try { _out?.Stop(); _out?.Dispose(); } catch { }
        _in = null; _out = null; _mixer = null; _volume = null; _enc = null; _loop = null;
        _voices.Clear();
    }

    // ---------- nadawanie ----------
    void OnMic(object? sender, WaveInEventArgs e)
    {
        // WaveIn nie gwarantuje dokładnie 960 próbek - składamy ramki ręcznie
        for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            _frame[_fill++] = BitConverter.ToInt16(e.Buffer, i);
            if (_fill == Frame) { _fill = 0; ProcessFrame(); }
        }
    }

    void ProcessFrame()
    {
        double sum = 0;
        for (int i = 0; i < Frame; i++) sum += _frame[i] * (double)_frame[i];
        double rms = Math.Sqrt(sum / Frame) / 32768.0;
        double db = 20 * Math.Log10(Math.Max(rms, 1e-6));
        float level = (float)Math.Clamp((db + 60) / 60, 0, 1);   // -60 dB..0 dB -> 0..1
        MicLevel = level;

        bool talk = false;
        if (!MicMuted && !Deafened)
        {
            if (Settings.Mode == "PTT") talk = (GetAsyncKeyState(Settings.PttKey) & 0x8000) != 0;
            else
            {
                if (level > Settings.Threshold) _hang = 12;     // ~240 ms "ogona" po zakończeniu mowy
                talk = _hang > 0;
                if (_hang > 0) _hang--;
            }
        }
        if (!talk || _enc == null) return;

        _lastTx = Environment.TickCount64;
        int n = _enc.Encode(_frame, 0, Frame, _opusBuf, 0, _opusBuf.Length);
        if (_loopback && _loop != null)
        {
            var copy = new byte[n];
            Array.Copy(_opusBuf, copy, n);
            Feed(_loop, copy);
        }
        if (_txAes == null) return;
        var pkt = Crypto.Seal(_txAes, _opusBuf.AsSpan(0, n));
        _wtx.Reset();
        _wtx.Put((byte)Pkt.Audio);
        _wtx.Put(_seq++);
        _wtx.Put(pkt);
        _net?.FirstPeer?.Send(_wtx, DeliveryMethod.Unreliable);
    }

    // ---------- odbiór ----------
    sealed class Voice
    {
        public OpusDecoder Dec = new OpusDecoder(Rate, 1);
        public BufferedWaveProvider Buf = new(new WaveFormat(Rate, 16, 1))
        { BufferDuration = TimeSpan.FromMilliseconds(500), DiscardOnBufferOverflow = true };
        public JitterGate Gate;
        public ushort Seq;
        public bool Has;
        public long Last;
        public short[] Pcm = new short[Frame];
        public byte[] Bytes = new byte[Frame * 2];
        public Voice() => Gate = new JitterGate(Buf);
    }

    void OnAudio(NetPacketReader r)
    {
        if (Deafened || _mixer == null || r.AvailableBytes < 4 + 28) return;
        int sender = r.GetUShort();
        ushort seq = r.GetUShort();
        var opus = _rxAes == null ? null : Crypto.Open(_rxAes, r.GetRemainingBytes());
        if (opus == null) return;

        if (!_voices.TryGetValue(sender, out var v))
        {
            v = new Voice();
            v.Gate.Gain = _gains.TryGetValue(sender, out var g) ? g : 1f;
            _voices[sender] = v;
            _mixer.AddMixerInput(v.Gate);
        }
        long now = Environment.TickCount64;
        if (now - v.Last > 2000) v.Has = false;     // nowa seria (albo nowy użytkownik z tym samym ID)
        v.Last = now;

        if (v.Has)
        {
            short diff = (short)(seq - v.Seq);
            if (diff <= 0) return;                  // spóźniony lub zduplikowany pakiet
            for (int i = 1; i < diff && i <= 3; i++) Feed(v, null);   // ukrywanie utraconych pakietów (PLC)
        }
        v.Seq = seq; v.Has = true;
        if (v.Buf.BufferedBytes > Frame * 2 * 10) v.Buf.ClearBuffer();   // trzymamy małe opóźnienie
        Feed(v, opus);
    }

    static void Feed(Voice v, byte[]? opus)
    {
        int n = v.Dec.Decode(opus, 0, opus?.Length ?? 0, v.Pcm, 0, Frame, false);
        if (n <= 0) return;
        Buffer.BlockCopy(v.Pcm, 0, v.Bytes, 0, n * 2);
        v.Buf.AddSamples(v.Bytes, 0, n * 2);
    }

    public bool IsSpeaking(int id)
    {
        if (id == MyId) return Environment.TickCount64 - _lastTx < 250;
        return _voices.TryGetValue(id, out var v) && Environment.TickCount64 - v.Last < 250;
    }

    public void Dispose() => Disconnect();
}

/// <summary>Minimalny bufor jitter: zaczyna grać dopiero po zebraniu ~60 ms, po opróżnieniu znów czeka.</summary>
sealed class JitterGate : ISampleProvider
{
    readonly BufferedWaveProvider _buf;
    readonly ISampleProvider _src;
    bool _playing;
    /// <summary>Głośność tego rozmówcy (mnożnik; 0 = wyciszony). Ustawiana z interfejsu.</summary>
    public volatile float Gain = 1f;
    const int Prebuffer = VoiceEngine.Frame * 2 * 3;

    public JitterGate(BufferedWaveProvider buf) { _buf = buf; _src = buf.ToSampleProvider(); }
    public WaveFormat WaveFormat => _src.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        if (!_playing)
        {
            if (_buf.BufferedBytes < Prebuffer) { Array.Clear(buffer, offset, count); return count; }
            _playing = true;
        }
        _src.Read(buffer, offset, count);
        float g = Gain;
        if (g != 1f)
            for (int i = 0; i < count; i++)
            {
                float x = buffer[offset + i] * g;
                buffer[offset + i] = x > 1f ? 1f : x < -1f ? -1f : x;   // przy wzmocnieniu >100 % obcinamy zamiast przesterować
            }
        if (_buf.BufferedBytes == 0) _playing = false;
        return count;
    }
}
