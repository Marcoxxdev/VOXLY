using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using VoiceShared;

namespace VoiceClient;

public enum PingState { Unknown, Ok, NoResponse }

/// <summary>
/// Odpytuje zapisane serwery o liczbę osób online (wiadomość bez połączenia i bez logowania).
/// Wszystko dzieje się na wątku UI: <see cref="Poll"/> wołamy z timera, odpowiedzi trafiają od razu do ServerVM.
/// Starsze serwery (sprzed tej funkcji) nie odpowiadają, więc pokazują "brak odpowiedzi".
/// </summary>
public sealed class ServerPinger : IDisposable
{
    sealed class Pending { public ServerVM Vm = null!; public long Sent; }

    readonly EventBasedNetListener _l = new();
    readonly NetManager _net;
    readonly Dictionary<int, Pending> _pending = new();
    readonly object _gate = new();
    bool _started;

    public ServerPinger()
    {
        _net = new NetManager(_l) { UnconnectedMessagesEnabled = true };
        _l.NetworkReceiveUnconnectedEvent += (ep, r, type) =>
        {
            try
            {
                if (type != UnconnectedMessageType.BasicMessage) return;
                if (!Wire.TryReadStatusReply(r, out var nonce, out var online, out var max, out var pwd, out var title, out var ver)) return;
                Pending? p;
                lock (_gate) { if (!_pending.Remove(nonce, out p)) return; }
                p.Vm.SetStatus(PingState.Ok, online, max, pwd, title);
            }
            catch { }
            finally { r.Recycle(); }
        };
    }

    /// <summary>"host" albo "host:port" (domyślnie 9050), tak samo jak przy łączeniu.</summary>
    public static (string Host, int Port) ParseHost(string text)
    {
        string host = text.Trim(); int port = 9050;
        int idx = host.LastIndexOf(':');
        if (idx > 0 && host.Count(c => c == ':') == 1 && int.TryParse(host[(idx + 1)..], out var p)) { host = host[..idx]; port = p; }
        return (host, port);
    }

    /// <summary>Wysyła zapytanie o stan serwera (nie blokuje UI, rozwiązywanie nazwy hosta idzie w tle).</summary>
    public void Query(ServerVM vm)
    {
        if (!_started) { try { _started = _net.Start(); } catch { _started = false; } }
        if (!_started) { vm.SetStatus(PingState.NoResponse, 0, 0, false, ""); return; }
        var (host, port) = ParseHost(vm.Host);
        if (host.Length == 0 || port < 1 || port > 65535) { vm.SetStatus(PingState.NoResponse, 0, 0, false, ""); return; }
        int nonce = Random.Shared.Next();
        lock (_gate)
        {
            foreach (var k in _pending.Where(x => x.Value.Vm == vm).Select(x => x.Key).ToList()) _pending.Remove(k);   // jedno zapytanie na serwer
            _pending[nonce] = new Pending { Vm = vm, Sent = Environment.TickCount64 };
        }
        Task.Run(() =>
        {
            try
            {
                var w = new NetDataWriter();
                Wire.WriteStatusQuery(w, nonce);
                _net.SendUnconnectedMessage(w, NetUtils.MakeEndPoint(host, port));
            }
            catch { /* nie rozwiązano nazwy itp.: zapytanie wygaśnie jako "brak odpowiedzi" */ }
        });
    }

    /// <summary>Wołać z timera na wątku UI: odbiera odpowiedzi i oznacza serwery, które nie odpowiedziały w 2,5 s.</summary>
    public void Poll()
    {
        if (!_started) return;
        _net.PollEvents();
        long now = Environment.TickCount64;
        List<Pending> expired;
        lock (_gate)
        {
            var keys = _pending.Where(x => now - x.Value.Sent > 2500).Select(x => x.Key).ToList();
            expired = keys.Select(k => _pending[k]).ToList();
            foreach (var k in keys) _pending.Remove(k);
        }
        foreach (var p in expired) p.Vm.SetStatus(PingState.NoResponse, 0, 0, false, "");
    }

    public void Dispose() { try { _net.Stop(); } catch { } }
}
