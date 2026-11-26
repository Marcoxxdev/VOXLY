using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VoiceClient;

public enum CallState { None, Outgoing, Incoming, Active }

public sealed class DmMessage
{
    public long Time { get; set; }       // czas uniksowy (s)
    public bool Mine { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>Historia rozmów prywatnych zapisana lokalnie: %AppData%\Voxly\history\{serwer}\{nick}.json (ostatnie 300 wiadomości).</summary>
public static class DmHistory
{
    const int Max = 300;

    static string Safe(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.ToLowerInvariant();
    }

    static string FileFor(string server, string nick) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voxly", "history", Safe(server), Safe(nick) + ".json");

    public static List<DmMessage> Load(string server, string nick)
    {
        try { return JsonSerializer.Deserialize<List<DmMessage>>(File.ReadAllText(FileFor(server, nick))) ?? new(); }
        catch { return new(); }
    }

    public static void Append(string server, string nick, DmMessage m)
    {
        try
        {
            var list = Load(server, nick);
            list.Add(m);
            if (list.Count > Max) list.RemoveRange(0, list.Count - Max);
            var path = FileFor(server, nick);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(list));
        }
        catch { }
    }
}

/// <summary>Okno rozmowy prywatnej z jednym użytkownikiem: czat z historią oraz rozmowa głosowa 1:1.</summary>
public sealed class DmWindow : Window
{
    readonly MainWindow _main;
    readonly string _serverKey;
    readonly StackPanel _list = new();
    readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBox _input = new() { MaxLength = 500 };
    readonly Button _send = new() { Content = Loc.T("send"), Margin = new Thickness(8, 0, 0, 0) };
    readonly Button _call = new();
    readonly TextBlock _status = new() { FontSize = 12, Foreground = Theme.Muted };
    readonly TextBlock _offline = new() { Foreground = Theme.B("#FF7B7B"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    readonly Border _callBar = new() { Visibility = Visibility.Collapsed, Background = Theme.AccentDeep, Padding = new Thickness(12, 8, 12, 8) };
    readonly TextBlock _callText = new() { FontWeight = FontWeights.SemiBold };
    readonly Button _accept = new() { Content = Loc.T("call_accept"), Background = Theme.B("#2E9E4F"), Margin = new Thickness(8, 0, 0, 0) };
    readonly Button _decline = new() { Content = Loc.T("call_decline"), Background = Theme.B("#D9363E"), Margin = new Thickness(8, 0, 0, 0) };
    readonly Button _hang = new() { Background = Theme.B("#D9363E"), Margin = new Thickness(8, 0, 0, 0) };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    CallState _state;
    DateTime _since = DateTime.Now;

    public string Nick { get; }
    public int PeerId { get; private set; }

    public DmWindow(MainWindow main, string serverKey, string nick, int peerId)
    {
        _main = main; _serverKey = serverKey; Nick = nick; PeerId = peerId;
        Owner = main;
        Title = Loc.T("dm_title", nick);
        Width = 440; Height = 580; MinWidth = 360; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Panel; Foreground = Theme.Text; FontSize = 14;
        var muted = Theme.Muted;

        // ----- nagłówek: nick, status i przycisk "Zadzwoń" -----
        _call.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), Text = "\uE717", FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
                new TextBlock { Text = Loc.T("call_btn"), VerticalAlignment = VerticalAlignment.Center }
            }
        };
        _call.Click += (_, _) => _main.StartCall(Nick, PeerId);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = nick, FontSize = 18, FontWeight = FontWeights.SemiBold });
        names.Children.Add(_status);
        var head = new DockPanel { Margin = new Thickness(14, 12, 14, 12) };
        DockPanel.SetDock(_call, Dock.Right);
        head.Children.Add(_call);
        head.Children.Add(names);

        // ----- pasek rozmowy głosowej -----
        _accept.Click += (_, _) => _main.AnswerCall(true);
        _decline.Click += (_, _) => _main.AnswerCall(false);
        _hang.Click += (_, _) => _main.HangUp();
        var callRow = new DockPanel();
        var callButtons = new StackPanel { Orientation = Orientation.Horizontal };
        callButtons.Children.Add(_accept); callButtons.Children.Add(_decline); callButtons.Children.Add(_hang);
        DockPanel.SetDock(callButtons, Dock.Right);
        callRow.Children.Add(callButtons);
        callRow.Children.Add(_callText);
        var callPanel = new StackPanel();
        callPanel.Children.Add(callRow);
        callPanel.Children.Add(new TextBlock { Text = Loc.T("call_privacy"), FontSize = 11, Foreground = muted, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        _callBar.Child = callPanel;

        var top = new StackPanel();
        top.Children.Add(new Border { Background = Theme.Panel, Child = head });
        top.Children.Add(_callBar);

        // ----- historia -----
        _scroll.Content = _list;
        var history = new Border { Background = Theme.Card, CornerRadius = new CornerRadius(8), Margin = new Thickness(12, 0, 12, 0), Padding = new Thickness(10), Child = _scroll };

        // ----- pole wpisywania -----
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SendMessage(); e.Handled = true; } };
        _send.Click += (_, _) => SendMessage();
        var inputRow = new DockPanel();
        DockPanel.SetDock(_send, Dock.Right);
        inputRow.Children.Add(_send);
        inputRow.Children.Add(_input);
        var bottom = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
        bottom.Children.Add(inputRow);
        bottom.Children.Add(_offline);
        bottom.Children.Add(new TextBlock { Text = Loc.T("dm_hint_local"), FontSize = 11, Foreground = muted, Margin = new Thickness(0, 6, 0, 0) });

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(top, 0); Grid.SetRow(history, 1); Grid.SetRow(bottom, 2);
        root.Children.Add(top); root.Children.Add(history); root.Children.Add(bottom);
        Content = root;

        foreach (var m in DmHistory.Load(_serverKey, Nick)) AddBubble(m);

        _tick.Tick += (_, _) => UpdateCallText();
        Activated += (_, _) => _main.DmOpened(Nick);
        Closing += (_, _) =>
        {
            if (_state == CallState.Incoming) _main.AnswerCall(false);   // zamknięcie okna = odrzucenie połączenia
            _tick.Stop();
        };
        Loaded += (_, _) => _input.Focus();
        SetPeer(peerId);
    }

    /// <summary>Aktualny numer rozmówcy (-1 = offline).</summary>
    public void SetPeer(int id)
    {
        PeerId = id;
        bool on = id >= 0;
        _input.IsEnabled = on; _send.IsEnabled = on;
        _call.IsEnabled = on && _state == CallState.None && _main.CurrentCall == CallState.None;
        _status.Text = Loc.T(on ? "cl_online" : "cl_offline");
        _offline.Text = on ? "" : Loc.T("dm_offline");
    }

    public void AddMessage(DmMessage m) => AddBubble(m);

    public void SystemNote(string text)
    {
        _list.Children.Add(new TextBlock
        {
            Text = text, FontSize = 11, Foreground = Theme.Muted, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap
        });
        ScrollDown();
    }

    void AddBubble(DmMessage m)
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(m.Time).ToLocalTime();
        var stamp = new TextBlock
        {
            Text = time.ToString(time.LocalDateTime.Date == DateTime.Today ? "HH:mm" : "yyyy-MM-dd HH:mm"),
            FontSize = 10, Foreground = Theme.B("#C7D0E0"), Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = m.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock { Text = m.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 });
        inner.Children.Add(stamp);
        _list.Children.Add(new Border
        {
            Child = inner, Padding = new Thickness(10, 7, 10, 5), CornerRadius = new CornerRadius(10),
            Background = m.Mine ? Theme.Accent : Theme.CardHead, Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = m.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left
        });
        ScrollDown();
    }

    void ScrollDown() => Dispatcher.InvokeAsync(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);

    void SendMessage()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0 || PeerId < 0) return;
        _main.Engine.SendDm(PeerId, text);
        var m = new DmMessage { Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Mine = true, Text = text };
        DmHistory.Append(_serverKey, Nick, m);
        AddBubble(m);
        _input.Clear();
    }

    /// <summary>Odświeża pasek rozmowy zależnie od stanu rozmowy (dotyczy tylko okna rozmówcy).</summary>
    public void RefreshCall(CallState state, string peerNick, DateTime since)
    {
        bool mine = state != CallState.None && peerNick.Equals(Nick, StringComparison.OrdinalIgnoreCase);
        _state = mine ? state : CallState.None;
        _since = since;
        _callBar.Visibility = mine ? Visibility.Visible : Visibility.Collapsed;
        _accept.Visibility = _decline.Visibility = _state == CallState.Incoming ? Visibility.Visible : Visibility.Collapsed;
        _hang.Visibility = _state == CallState.Outgoing || _state == CallState.Active ? Visibility.Visible : Visibility.Collapsed;
        _hang.Content = Loc.T(_state == CallState.Outgoing ? "call_cancel" : "call_hangup");
        _call.IsEnabled = PeerId >= 0 && state == CallState.None;
        if (mine) { _tick.Start(); UpdateCallText(); } else _tick.Stop();
    }

    void UpdateCallText()
    {
        var span = DateTime.Now - _since;
        _callText.Text = _state switch
        {
            CallState.Outgoing => Loc.T("call_calling", Nick),
            CallState.Incoming => Loc.T("call_incoming", Nick),
            CallState.Active => Loc.T("call_active", Nick) + "  " + $"{(int)span.TotalMinutes:00}:{span.Seconds:00}",
            _ => ""
        };
    }
}
