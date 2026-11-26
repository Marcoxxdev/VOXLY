using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VoiceShared;

namespace VoiceClient;

public partial class MainWindow : Window
{
    const int LobbyId = 1;
    const string DragFormat = "voxly.channel";
    const string UserFormat = "voxly.user";

    readonly AppSettings _s = AppSettings.Load();
    readonly VoiceEngine _e;
    readonly ObservableCollection<ChannelVM> _channels = new();
    readonly ObservableCollection<UserVM> _allUsers = new();
    readonly ObservableCollection<ChatLine> _chat = new();
    readonly Dictionary<int, UserVM> _uv = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly DispatcherTimer _retryTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _loading = true;
    bool _capturing;
    // automatyczne ponowne łączenie
    bool _wasConnected, _reconnecting, _trying, _syncLang;
    int _attempt, _countdown, _port = 9050;
    string _host = "", _srvName = "", _lastReason = "";
    int _myId, _tick, _myChannel = LobbyId, _myAdminCh = -1;
    Rank _myRank = Rank.User;
    ChannelVM? _downChannel;
    Point _dragStart;

    bool IsAdmin => _myRank == Rank.Admin;
    bool CanEnter(ChannelVM c) => IsAdmin || (_myRank == Rank.ChannelAdmin && _myAdminCh == c.Id) || Wire.Allows(c.Access, _myRank);

    /// <summary>Admin zarządza każdym (poza sobą), Channel Admin tylko osobami ze swojego kanału (przenoszenie do Lobby).</summary>
    bool CanManageUser(UserVM t) =>
        t.Id != _myId && (IsAdmin ||
            (_myRank == Rank.ChannelAdmin && t.ChannelId == _myAdminCh && t.ChannelId != LobbyId && t.Rank != Rank.Admin));

    bool CanManage(ChannelVM c) => IsAdmin || (_myRank == Rank.ChannelAdmin && _myAdminCh == c.Id);

    public MainWindow()
    {
        Loc.I.Set(string.IsNullOrEmpty(_s.Language) ? Loc.Detect() : _s.Language);   // przed wczytaniem XAML-a
        Theme.Apply(_s.ThemeName);   // kolor interfejsu wybrany przez użytkownika
        InitializeComponent();
        _e = new VoiceEngine(_s);

        ChannelsList.ItemsSource = _channels;
        ChatList.ItemsSource = _chat;
        AllUsersList.ItemsSource = _allUsers;

        FillDevices();
        BuildAvatarPicker();
        BuildThemePicker();
        UpdateLoopButton();
        SyncLangCombos();
        ChkAutoReconnect.IsChecked = _s.AutoReconnect;

        InitServers();
        ApplyTitle("");
        SldChScale.Value = _s.ChannelScale; ApplyChannelScale();
        RbPtt.IsChecked = _s.Mode == "PTT"; RbVad.IsChecked = _s.Mode != "PTT";
        BtnPttKey.Content = Loc.T("ptt_key", KeyName());
        SldThreshold.Value = _s.Threshold; SldVolume.Value = _s.Volume; UpdateVolLabel();

        _e.Connected += id => Dispatcher.InvokeAsync(() => OnConnected(id));
        _e.StateChanged += (c, u) => Dispatcher.InvokeAsync(() => ApplyState(c, u));
        _e.Disconnected += reason => Dispatcher.InvokeAsync(() => OnDisconnected(reason));
        _e.ChatReceived += (id, text) => Dispatcher.InvokeAsync(() => AddChat(id, text));
        _e.AdminResult += (ok, srvPwd) => Dispatcher.InvokeAsync(() =>
        {
            TxtAdminStatus.Foreground = ok ? Theme.Green : Brushes.IndianRed;
            TxtAdminStatus.Text = !ok ? Loc.T("admin_bad") : IsAdmin ? "" : Loc.T("admin_ok");
            if (ok && IsAdmin) { TxtAdminPageStatus.Foreground = Theme.Green; TxtAdminPageStatus.Text = Loc.T("admin_saved"); }
            ChkSrvPwd.IsChecked = srvPwd;
            PwdAdmin.Clear();
        });
        _e.Error += (code, arg) => Dispatcher.InvokeAsync(() =>
        {
            if (code is "Banned" or "KickedFromServer") _noReconnect = true;
            var msg = Loc.ServerError(code, arg);
            if (ConnectOverlay.Visibility == Visibility.Visible) TxtConnStatus.Text = msg;
            else MessageBox.Show(this, msg, "VOXLY");
        });

        ChkAutoUpdate.IsChecked = _s.AutoCheckUpdates;
        TxtVersion.Text = Loc.T("upd_current", Updater.CurrentText);
        _updStatusTimer.Tick += (_, _) => { _updStatusTimer.Stop(); TxtUpdateStatus.Text = ""; };
        _volSaveTimer.Tick += (_, _) => { _volSaveTimer.Stop(); _s.Save(); };
        Loaded += async (_, _) => { if (_s.AutoCheckUpdates) await CheckUpdateAsync(false); };
        InitClientsView();
        _e.TitleChanged += t => Dispatcher.InvokeAsync(() => ApplyTitle(t));
        _e.ServerInfoChanged += (max, ver) => Dispatcher.InvokeAsync(() => ApplyServerInfo(max, ver));
        _e.LogListReceived += list => Dispatcher.InvokeAsync(() => ApplyLog(list));
        _e.LogEntryReceived += entry => Dispatcher.InvokeAsync(() => AddLog(entry));
        _e.DmReceived += (id, nick, text) => Dispatcher.InvokeAsync(() => OnDm(nick, text));
        _e.CallIncoming += (id, nick) => Dispatcher.InvokeAsync(() => OnCallIncoming(id, nick));
        _e.CallReplied += (id, ok) => Dispatcher.InvokeAsync(() => OnCallReplied(id, ok));
        _e.CallEnded += id => Dispatcher.InvokeAsync(() => OnCallEnded(id));
        _ringTimer.Tick += (_, _) => { if (_callState == CallState.Incoming) Sound("ring"); else _ringTimer.Stop(); };
        _callTimeout.Tick += (_, _) =>
        {
            _callTimeout.Stop();
            if (_callState != CallState.Outgoing) return;
            NoteDm(_callNick, Loc.T("call_noanswer"));
            _e.CallEnd(); ResetCall(); RefreshCalls();
        };
        ChkSounds.IsChecked = _s.SoundsEnabled; ChkTray.IsChecked = _s.TrayNotify; ChkTrayInactive.IsChecked = _s.TrayOnlyInactive;
        LogList.ItemsSource = _log;
        _log.CollectionChanged += (_, _) => TxtLogEmpty.Visibility = _log.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        InitTray();
        RefreshVersionTexts();
        _e.ClientsChanged += list => Dispatcher.InvokeAsync(() => ApplyClients(list));
        _timer.Tick += Timer_Tick;
        _timer.Start();
        _retryTimer.Tick += RetryTimer_Tick;
        Loc.I.LanguageChanged += ApplyLanguage;
        _loading = false;
    }

    // ---------- język ----------
    void FillDevices()
    {
        bool was = _loading; _loading = true;     // ustawianie list nie ma zapisywać ustawień
        var mics = VoiceEngine.InputDevices(); CmbMic.ItemsSource = mics;
        CmbMic.SelectedIndex = Math.Max(0, mics.IndexOf(_s.MicDevice));
        var spk = VoiceEngine.OutputDevices(); CmbSpk.ItemsSource = spk;
        CmbSpk.SelectedIndex = Math.Max(0, spk.IndexOf(_s.SpeakerDevice));
        _loading = was;
    }

    void SyncLangCombos()
    {
        _syncLang = true;
        int idx = Loc.I.Lang == "en" ? 1 : 0;
        CmbLang.SelectedIndex = idx; CmbLangConn.SelectedIndex = idx;
        _syncLang = false;
    }

    void Lang_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncLang || _loading) return;
        if ((sender as ComboBox)?.SelectedItem is not ComboBoxItem { Tag: string code }) return;
        _s.Language = code; _s.Save();
        Loc.I.Set(code);       // odświeża XAML i wywołuje ApplyLanguage
        SyncLangCombos();
    }

    // napisy ustawiane z kodu trzeba odświeżyć ręcznie (te z XAML-a odświeżają się same)
    void ApplyLanguage()
    {
        BuildThemePicker();
        _tray?.SetTexts(Loc.T("tray_show"), Loc.T("tray_exit"));
        RefreshVersionTexts();
        foreach (var lv in _log) lv.RefreshText();
        _defNames.Clear(); FillDefChannels();
        ApplyTitle(_chTitle);
        UpdateVolLabel();
        FillClientFilter();
        foreach (var c in _clients) c.RefreshText();
        TxtVersion.Text = Loc.T("upd_current", Updater.CurrentText);
        if (_update != null && !_updating) ShowUpdateBanner();
        UpdateLoopButton();
        FillDevices();
        BtnPttKey.Content = _capturing ? Loc.T("ptt_press") : Loc.T("ptt_key", KeyName());
        BtnConnect.Content = Loc.T(BtnConnect.IsEnabled ? "connect" : "connecting");
        UpdateMuteButtons();
        RefreshRankUi();
        UpdateReconnectText();
    }

    // ---------- awatary ----------
    readonly List<(Border b, int id)> _avatarBtns = new();

    void BuildAvatarPicker()
    {
        for (int id = 0; id <= Avatars.Count; id++)
        {
            bool none = id == 0;
            var b = new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(26), Margin = new Thickness(0, 0, 6, 6),
                Background = none ? Theme.B("#4A5266") : Avatars.Image(id), BorderThickness = new Thickness(2.5),
                BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand
            };
            if (none)
            {
                b.ToolTip = Loc.T("avatar_none");
                b.Child = new TextBlock
                {
                    Text = "Aa", FontFamily = Avatars.TextFont, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
            }
            int pick = id;
            b.MouseLeftButtonUp += (_, _) => PickAvatar(pick);
            _avatarBtns.Add((b, id));
            AvatarPanel.Children.Add(b);
        }
        RefreshAvatarSelection();
    }

    void RefreshAvatarSelection()
    {
        foreach (var (b, id) in _avatarBtns) b.BorderBrush = id == _s.Avatar ? Brushes.White : Brushes.Transparent;
    }

    void PickAvatar(int id)
    {
        _s.Avatar = id; _s.Save();
        RefreshAvatarSelection();
        RefreshMeAvatar();
        if (_e.IsConnected) _e.SetAvatar(id);
    }

    void RefreshMeAvatar()
    {
        bool has = Avatars.Has(_s.Avatar);
        MeInitial.Text = has ? "" : (_s.Nick.Length > 0 ? _s.Nick[..1].ToUpperInvariant() : "?");
        MeInitial.FontFamily = Avatars.TextFont;
        MeInitial.FontSize = 20;
        MeAvatar.Background = has ? Avatars.Image(_s.Avatar) : Theme.Avatar(_s.Nick);
    }

    void AutoReconnect_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.AutoReconnect = ChkAutoReconnect.IsChecked == true; _s.Save();
    }

    // ---------- łączenie ----------
    // ---------- kolor interfejsu (motyw) ----------
    void BuildThemePicker()
    {
        ThemePanel.Children.Clear();
        foreach (var name in Theme.Names)
        {
            var pal = Theme.GetPalette(name);
            var inner = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            inner.Children.Add(new Border { Width = 34, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(pal.Accent), Margin = new Thickness(0, 0, 0, 6) });
            inner.Children.Add(new TextBlock { Text = Loc.T("th_" + name), FontSize = 11, Foreground = new SolidColorBrush(pal.Text), HorizontalAlignment = HorizontalAlignment.Center });
            var tile = new Border
            {
                Width = 86, Height = 56, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(pal.Card), BorderThickness = new Thickness(2),
                BorderBrush = name == Theme.Name ? Brushes.White : new SolidColorBrush(pal.Line), Child = inner
            };
            string pick = name;
            tile.MouseLeftButtonUp += (_, _) => SetTheme(pick);
            ThemePanel.Children.Add(tile);
        }
    }

    void SetTheme(string name)
    {
        _s.ThemeName = name; _s.Save();
        Theme.Apply(name);
        foreach (var sv in _servers) sv.RefreshTheme();
        foreach (var uv in _uv.Values) uv.RefreshTheme();
        UpdateMuteButtons(); UpdateLoopButton(); UpdateNet();
        BuildThemePicker();
    }

    // ---------- własny pasek tytułu ----------
    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Max_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Window_StateChanged(object? sender, EventArgs e)
    {
        bool max = WindowState == WindowState.Maximized;
        IcoMax.Text = max ? "\uE923" : "\uE922";
        // okno bez ramki systemowej po maksymalizacji wystaje poza ekran o grubość ramki zmiany rozmiaru
        RootGrid.Margin = max ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
    }

    // ---------- powiadomienia na pasku tytułu (kto wszedł na serwer) ----------
    readonly Queue<(string text, Brush brush)> _barQueue = new();
    bool _barBusy;

    void ShowBar(string text, Brush? brush = null)
    {
        if (_barQueue.Count < 8) _barQueue.Enqueue((text, brush ?? Theme.Green));
        if (!_barBusy) _ = PumpBar();
    }

    /// <summary>Wyświetla kolejno komunikaty jako tekst płynący przez pasek tytułu z lewej do prawej.</summary>
    async Task PumpBar()
    {
        _barBusy = true;
        while (_barQueue.Count > 0)
        {
            var (text, brush) = _barQueue.Dequeue();
            TxtBarNotice.Text = "●  " + text;
            TxtBarNotice.Foreground = brush;
            TxtBarNotice.Visibility = Visibility.Visible;
            TxtBarNotice.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = TxtBarNotice.DesiredSize.Width, h = TxtBarNotice.DesiredSize.Height;
            double width = Math.Max(BarCanvas.ActualWidth, 200);
            Canvas.SetTop(TxtBarNotice, Math.Max(0, (BarCanvas.ActualHeight - h) / 2));
            var done = new TaskCompletionSource();
            var anim = new DoubleAnimation(-w, width, TimeSpan.FromSeconds((width + w) / 110.0));   // ok. 110 px/s
            anim.Completed += (_, _) => done.TrySetResult();
            BarShift.BeginAnimation(TranslateTransform.XProperty, anim);
            await done.Task;
            TxtBarNotice.Visibility = Visibility.Collapsed;
        }
        _barBusy = false;
    }

    // ---------- dziennik zdarzeń serwera (panel admina) ----------
    readonly ObservableCollection<LogVM> _log = new();
    bool _watchingLog;

    void ApplyLog(List<LogInfo> list)
    {
        _log.Clear();
        for (int i = list.Count - 1; i >= 0; i--) _log.Add(new LogVM(list[i]));   // najnowsze na górze
    }

    void AddLog(LogInfo entry)
    {
        _log.Insert(0, new LogVM(entry));
        while (_log.Count > 2000) _log.RemoveAt(_log.Count - 1);
    }

    // ---------- wersje, limit użytkowników, link do pobrania ----------
    string _srvVersion = "";

    void RefreshVersionTexts()
    {
        string cur = Updater.CurrentText;
        TxtSideVersion.Text = Loc.T("ver_short", cur);
        TxtConnVersion.Text = Loc.T("ver_short", cur);
        TxtFooter.Text = Loc.T("footer", cur);
        TxtVersions.Text = Loc.T("ver_both", cur, string.IsNullOrEmpty(_srvVersion) ? "?" : _srvVersion);
    }

    void ApplyServerInfo(int maxUsers, string serverVersion)
    {
        _srvVersion = serverVersion;
        if (!TxtMaxUsers.IsKeyboardFocusWithin) TxtMaxUsers.Text = maxUsers.ToString();
        RefreshVersionTexts();
    }

    void SaveMaxUsers_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtMaxUsers.Text.Trim(), out int n) || n < 1 || n > 200)
        {
            MessageBox.Show(this, Loc.T("adm_maxusers_bad"), "VOXLY");
            return;
        }
        _e.SetMaxUsers(n);
        TxtAdminPageStatus.Foreground = Theme.Green; TxtAdminPageStatus.Text = Loc.T("adm_sent");
    }

    async void CopyLink_Click(object sender, MouseButtonEventArgs e)
    {
        try { Clipboard.SetText(Updater.DownloadUrl); }
        catch { return; }   // schowek bywa chwilowo zajęty przez inny program
        IcoCopy.Text = "\uE73E"; IcoCopy.Foreground = Theme.Green;   // ptaszek: skopiowano
        await Task.Delay(1500);
        IcoCopy.Text = "\uE8C8"; IcoCopy.Foreground = Theme.Muted;
    }

    void Download_Click(object sender, MouseButtonEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Updater.DownloadUrl) { UseShellExecute = true }); }
        catch { }
    }

    // ---------- dźwięki i powiadomienia w zasobniku ----------
    TrayService? _tray;
    Dictionary<int, int> _prevCh = new();
    Dictionary<int, string> _prevNicks = new();
    int _prevMyChannel = -1;
    Rank _prevMyRank = Rank.User;
    bool _haveBaseline;

    internal VoiceEngine Engine => _e;

    void Sound(string name)
    {
        if (!_s.SoundsEnabled || _e.Deafened) return;
        SoundFx.Play(name, _e.OutDeviceIndex, (float)_s.Volume);
    }

    void InitTray()
    {
        _tray = new TrayService();
        _tray.SetTexts(Loc.T("tray_show"), Loc.T("tray_exit"));
        _tray.OpenRequested += () => { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); };
        _tray.ExitRequested += () => Application.Current.Shutdown();
    }

    /// <summary>Dymek w zasobniku Windows, ale tylko gdy okno programu jest nieaktywne lub zminimalizowane.</summary>
    void Toast(string text)
    {
        if (!_s.TrayNotify) return;
        if (_s.TrayOnlyInactive && IsActive && WindowState != WindowState.Minimized) return;
        _tray?.Balloon("VOXLY", text);
    }

    void Sounds_Changed(object sender, RoutedEventArgs e) { if (_loading) return; _s.SoundsEnabled = ChkSounds.IsChecked == true; _s.Save(); }
    void TrayInactive_Changed(object sender, RoutedEventArgs e) { if (_loading) return; _s.TrayOnlyInactive = ChkTrayInactive.IsChecked == true; _s.Save(); }
    void TestTray_Click(object sender, RoutedEventArgs e) => _tray?.Balloon("VOXLY", Loc.T("tray_test_text"));

    void Tray_Changed(object sender, RoutedEventArgs e) { if (_loading) return; _s.TrayNotify = ChkTray.IsChecked == true; _s.Save(); }

    /// <summary>Porównuje nowy stan z poprzednim: dźwięki wejścia i wyjścia z kanału, nowa ranga, powiadomienie o nowej osobie.</summary>
    void NotifyChanges(List<UserInfo> us)
    {
        var me = us.FirstOrDefault(u => u.Id == _myId);
        if (_haveBaseline && me != null)
        {
            int myCh = me.ChannelId;
            bool movedMe = _prevMyChannel != myCh;
            foreach (var u in us)
            {
                if (u.Id == _myId) continue;
                if (!_prevCh.TryGetValue(u.Id, out int pc))
                {
                    ShowBar(Loc.T("tray_joined", u.Nick));   // pasek na górze okna
                    Toast(Loc.T("tray_joined", u.Nick));   // nowa osoba na serwerze
                    if (u.ChannelId == myCh) Sound("join");
                }
                else if (!movedMe && pc != u.ChannelId)
                {
                    if (u.ChannelId == myCh) Sound("join");
                    else if (pc == myCh) Sound("leave");
                }
            }
            foreach (var gone in _prevCh.Keys.ToList())   // ktoś wyszedł z serwera lub stracił połączenie
                if (gone != _myId && !us.Any(x => x.Id == gone) && _prevNicks.TryGetValue(gone, out var goneNick))
                {
                    ShowBar(Loc.T("tray_left", goneNick), Theme.B("#F5A623"));
                    Toast(Loc.T("tray_left", goneNick));
                }
            if (!movedMe)
                foreach (var kv in _prevCh)   // ktoś rozłączył się z Twojego kanału
                    if (kv.Key != _myId && kv.Value == myCh && !us.Any(x => x.Id == kv.Key)) Sound("leave");
            if (me.Rank > _prevMyRank) Sound("rank");
        }
        _prevCh = us.ToDictionary(x => x.Id, x => x.ChannelId);
        _prevNicks = us.ToDictionary(x => x.Id, x => x.Nick);
        _prevMyChannel = me?.ChannelId ?? -1;
        _prevMyRank = me?.Rank ?? Rank.User;
        _haveBaseline = me != null;
    }

    // ---------- wiadomości prywatne ----------
    readonly Dictionary<string, DmWindow> _dms = new(StringComparer.OrdinalIgnoreCase);
    string HistKey => _host + ":" + _port;

    UserVM? FindUser(string nick) => _uv.Values.FirstOrDefault(v => v.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase));

    void UserDm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: UserVM t }) OpenDm(t.Nick, true);
    }

    DmWindow OpenDm(string nick, bool activate)
    {
        if (!_dms.TryGetValue(nick, out var w))
        {
            w = new DmWindow(this, HistKey, nick, FindUser(nick)?.Id ?? -1);
            w.Closed += (_, _) => _dms.Remove(nick);
            _dms[nick] = w;
            w.RefreshCall(_callState, _callNick, _callSince);
            w.Show();
        }
        if (activate)
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        }
        DmOpened(nick);
        return w;
    }

    internal void DmOpened(string nick)
    {
        var u = FindUser(nick);
        if (u != null && u.Unread != 0) u.Unread = 0;
    }

    void OnDm(string fromNick, string text)
    {
        var m = new DmMessage { Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Mine = false, Text = text };
        DmHistory.Append(HistKey, fromNick, m);
        if (_dms.TryGetValue(fromNick, out var w))
        {
            w.AddMessage(m);
            if (w.IsActive) return;   // rozmowa jest na wierzchu: bez dźwięku i powiadomienia
        }
        else
        {
            var u = FindUser(fromNick);
            if (u != null) u.Unread++;   // licznik nieprzeczytanych przy ikonie wiadomości
        }
        Sound("dm");
        Toast(Loc.T("tray_dm", fromNick));
    }

    // ---------- rozmowy głosowe prywatne (1:1) ----------
    CallState _callState;
    int _callPeer = -1;
    string _callNick = "";
    DateTime _callSince = DateTime.Now;
    readonly DispatcherTimer _ringTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    readonly DispatcherTimer _callTimeout = new() { Interval = TimeSpan.FromSeconds(30) };

    internal CallState CurrentCall => _callState;

    void RefreshCalls() { foreach (var dw in _dms.Values) dw.RefreshCall(_callState, _callNick, _callSince); }

    void ResetCall()
    {
        _callState = CallState.None; _callPeer = -1; _callNick = "";
        _ringTimer.Stop(); _callTimeout.Stop();
    }

    void NoteDm(string nick, string text)
    {
        if (_dms.TryGetValue(nick, out var dw)) dw.SystemNote(text);
    }

    internal void StartCall(string nick, int peerId)
    {
        if (_callState != CallState.None || peerId < 0) return;
        _callState = CallState.Outgoing; _callPeer = peerId; _callNick = nick; _callSince = DateTime.Now;
        _e.CallInvite(peerId);
        _callTimeout.Stop(); _callTimeout.Start();   // po 30 s bez odpowiedzi anulujemy
        RefreshCalls();
    }

    internal void AnswerCall(bool accept)
    {
        if (_callState != CallState.Incoming) return;
        _ringTimer.Stop();
        _e.CallAnswer(_callPeer, accept);
        if (accept) { _callState = CallState.Active; _callSince = DateTime.Now; }
        else ResetCall();
        RefreshCalls();
    }

    internal void HangUp()
    {
        if (_callState == CallState.None) return;
        _e.CallEnd();
        ResetCall();
        RefreshCalls();
    }

    void OnCallIncoming(int id, string nick)
    {
        if (_callState != CallState.None) { _e.CallAnswer(id, false); return; }   // zajęty: odrzucamy
        _callState = CallState.Incoming; _callPeer = id; _callNick = nick; _callSince = DateTime.Now;
        OpenDm(nick, true);
        RefreshCalls();
        Sound("ring"); _ringTimer.Stop(); _ringTimer.Start();
        Toast(Loc.T("tray_call", nick));
    }

    void OnCallReplied(int id, bool accepted)
    {
        if (_callState != CallState.Outgoing || _callPeer != id) return;
        _callTimeout.Stop();
        if (accepted) { _callState = CallState.Active; _callSince = DateTime.Now; }
        else { NoteDm(_callNick, Loc.T("call_declined")); ResetCall(); }
        RefreshCalls();
    }

    void OnCallEnded(int id)
    {
        if (_callState == CallState.None || _callPeer != id) return;
        NoteDm(_callNick, Loc.T("call_ended"));
        ResetCall(); RefreshCalls();
        Sound("leave");
    }

    // ---------- AFK (bezczynność 2 minuty) ----------
    bool _afk;

    [StructLayout(LayoutKind.Sequential)]
    struct LastInputInfo { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")]
    static extern bool GetLastInputInfo(ref LastInputInfo plii);

    static TimeSpan IdleTime()
    {
        var li = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref li)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - li.dwTime));
    }

    void CheckAfk()
    {
        // bezczynność = brak klawiatury i myszy w całym systemie; mówienie przez mikrofon liczy się jako aktywność
        bool idle = IdleTime() >= TimeSpan.FromMinutes(2) && !_e.IsSpeaking(_myId);
        if (idle == _afk) return;
        _afk = idle;
        _e.SetAfk(idle);
    }

    // ---------- domyślny kanał po połączeniu (osobno dla każdego serwera) ----------
    string SrvKey => (_host + ":" + _port).ToLowerInvariant();
    readonly List<int> _defIds = new();
    readonly List<string> _defNames = new();
    bool _fillingDef, _pendingAutoJoin;

    void FillDefChannels()
    {
        var names = new List<string> { Loc.T("def_channel_none") };
        names.AddRange(_channels.Select(c => c.Name));
        if (names.SequenceEqual(_defNames)) return;   // bez zmian: nie przebudowujemy listy (nie zamykamy rozwiniętej)
        _defNames.Clear(); _defNames.AddRange(names);
        _defIds.Clear(); _defIds.Add(0); _defIds.AddRange(_channels.Select(c => c.Id));
        _fillingDef = true;
        CmbDefChannel.ItemsSource = names;
        int cur = _s.DefaultChannels.TryGetValue(SrvKey, out int id) ? _defIds.IndexOf(id) : 0;
        CmbDefChannel.SelectedIndex = Math.Max(0, cur);
        _fillingDef = false;
    }

    void DefChannel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _fillingDef || CmbDefChannel.SelectedIndex < 0 || CmbDefChannel.SelectedIndex >= _defIds.Count) return;
        int id = _defIds[CmbDefChannel.SelectedIndex];
        if (id == 0) _s.DefaultChannels.Remove(SrvKey); else _s.DefaultChannels[SrvKey] = id;
        _s.Save();
    }

    // ---------- zapisane serwery ----------
    readonly ObservableCollection<ServerVM> _servers = new();
    ServerVM? _selServer;
    bool _autoName;            // nazwa serwera ma zostać pobrana z nazwy listy kanałów przy pierwszym wejściu
    ServerEntry? _connEntry;   // wpis na liście, z którego aktualnie się łączymy

    void InitServers()
    {
        foreach (var entry in _s.Servers) _servers.Add(new ServerVM(entry));
        ServerList.ItemsSource = _servers;
        SelectServer(_servers.FirstOrDefault(x => x.Entry.Id == _s.LastServerId) ?? _servers.FirstOrDefault());
    }

    void SelectServer(ServerVM? sv)
    {
        if (_selServer != null) _selServer.IsSelected = false;
        _selServer = sv;
        if (sv != null) sv.IsSelected = true;
        var entry = sv?.Entry;
        TxtSrvName.Text = entry?.Name ?? "";
        TxtHost.Text = entry?.Host ?? "";
        TxtNick.Text = entry?.Nick ?? _s.Nick;     // nowy serwer: podpowiadamy ostatnio użyty nick
        PwdKey.Password = entry?.Password ?? "";
        ChkRemember.IsChecked = true;
        BtnDeleteServer.Visibility = sv != null ? Visibility.Visible : Visibility.Collapsed;
        TxtConnStatus.Text = "";
    }

    void Server_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ServerVM sv }) SelectServer(sv);
    }

    void AddServer_Click(object sender, RoutedEventArgs e)
    {
        SelectServer(null);
        TxtSrvName.Focus();
    }

    void DeleteServer_Click(object sender, RoutedEventArgs e)
    {
        if (_selServer == null) return;
        if (MessageBox.Show(this, Loc.T("srv_delete_q", _selServer.Name), "VOXLY", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _s.Servers.Remove(_selServer.Entry);
        _servers.Remove(_selServer);
        _selServer = null;
        SelectServer(_servers.FirstOrDefault());
        _s.Save();
    }

    /// <summary>Zapisuje bieżący formularz jako serwer na liście (nowy wpis albo edycja zaznaczonego).</summary>
    void UpsertServer(string name, string host, string nick, string password, bool autoName)
    {
        if (_selServer == null)
        {
            var entry = new ServerEntry();
            _s.Servers.Add(entry);
            var vm = new ServerVM(entry);
            _servers.Add(vm);
            _selServer = vm; vm.IsSelected = true;
            BtnDeleteServer.Visibility = Visibility.Visible;
        }
        var en = _selServer.Entry;
        en.Name = name; en.Host = host; en.Nick = nick; en.Password = password; en.AutoName = autoName;
        _selServer.Refresh();
        _s.LastServerId = en.Id;
    }

    // ---------- własna nazwa listy kanałów (ustawia Admin) ----------
    string _chTitle = "";

    void ApplyTitle(string title)
    {
        _chTitle = title;
        if (_autoName && !string.IsNullOrWhiteSpace(title))   // pierwsze wejście: nazwa serwera = nazwa listy kanałów ustawiona przez admina
        {
            _autoName = false;
            _srvName = title.Trim();
            TxtServer.Text = _srvName;
            if (_connEntry != null && _connEntry.AutoName)
            {
                _connEntry.Name = _srvName; _connEntry.AutoName = false;
                _servers.FirstOrDefault(x => x.Entry == _connEntry)?.Refresh();
                _s.Save();
            }
        }
        TxtChannelsTitle.Text = string.IsNullOrWhiteSpace(title) ? Loc.T("voice_channels") : title;
        if (!TxtChTitle.IsKeyboardFocusWithin) TxtChTitle.Text = title;
    }

    void SaveTitle_Click(object sender, RoutedEventArgs e)
    {
        _e.SetTitle(TxtChTitle.Text.Trim());
        TxtAdminPageStatus.Foreground = Theme.Green; TxtAdminPageStatus.Text = Loc.T("adm_sent");
    }

    // ---------- rozmiar listy kanałów ----------
    void ApplyChannelScale()
    {
        double sc = Math.Clamp(_s.ChannelScale, 0.7, 1.4);
        ChannelsList.LayoutTransform = new ScaleTransform(sc, sc);
        LblChScale.Text = (int)Math.Round(sc * 100) + "%";
    }

    void ChScale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.ChannelScale = Math.Round(SldChScale.Value, 2);
        ApplyChannelScale();
        _volSaveTimer.Stop(); _volSaveTimer.Start();   // zapis z opóźnieniem
    }

    void Channels_Wheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;   // bez Ctrl zwykłe przewijanie
        SldChScale.Value = Math.Clamp(_s.ChannelScale + (e.Delta > 0 ? 0.05 : -0.05), 0.7, 1.4);
        e.Handled = true;
    }

    void Connect_Click(object sender, RoutedEventArgs e)
    {
        var hostText = TxtHost.Text.Trim();
        var nick = TxtNick.Text.Trim();
        var srvName = TxtSrvName.Text.Trim();
        bool autoName = srvName.Length == 0 || (_selServer?.Entry.AutoName == true && srvName == _selServer.Entry.Name);
        if (srvName.Length == 0) srvName = hostText;
        if (hostText.Length == 0 || nick.Length == 0)
        {
            TxtConnStatus.Text = Loc.T("fill_host_nick");
            return;
        }
        string host = hostText; int port = 9050;
        int idx = hostText.LastIndexOf(':');
        if (idx > 0 && hostText.Count(c => c == ':') == 1 && int.TryParse(hostText[(idx + 1)..], out var p))
        { host = hostText[..idx]; port = p; }

        _host = host; _port = port; _srvName = srvName; _autoName = autoName;
        _s.Host = hostText; _s.Nick = nick; _s.ServerPassword = PwdKey.Password; _s.Remember = ChkRemember.IsChecked == true;
        if (_s.Remember) { UpsertServer(srvName, hostText, nick, PwdKey.Password, autoName); _connEntry = _selServer?.Entry; }
        else _connEntry = null;
        _s.Save();

        TxtConnStatus.Text = "";
        BtnConnect.IsEnabled = false; BtnConnect.Content = Loc.T("connecting");
        _e.MicMuted = false; _e.Deafened = false; UpdateMuteButtons();
        try { _e.Connect(host, port, _s.ServerPassword, nick); }
        catch (Exception ex) { ShowConnect(Loc.T("error_prefix") + " " + ex.Message); }
    }

    void OnConnected(int id)
    {
        _myId = id;
        _pendingAutoJoin = true; _afk = false;
        bool wasReconnect = _reconnecting;
        _wasConnected = true;
        StopReconnect();
        ConnectOverlay.Visibility = Visibility.Collapsed;
        TxtServer.Text = _srvName.Length > 0 ? _srvName : _s.Host;
        TxtMe.Text = _s.Nick;
        RefreshMeAvatar();
        if (!wasReconnect) ShowPage("ch");
    }

    void ShowConnect(string? message)
    {
        ApplyTitle("");
        _watching = false; _clients.Clear(); _noReconnect = false;
        _haveBaseline = false; _prevCh.Clear(); _prevNicks.Clear(); _defNames.Clear(); _srvVersion = "";
        _watchingLog = false; _log.Clear();
        ResetCall(); RefreshCalls();
        foreach (var dw in _dms.Values) dw.SetPeer(-1);
        _e.Loopback = false; UpdateLoopButton();
        _wasConnected = false;
        StopReconnect();
        _channels.Clear(); _uv.Clear(); _allUsers.Clear(); _chat.Clear();
        _myRank = Rank.User; _myAdminCh = -1;
        RefreshRankUi();
        TxtAdminStatus.Text = ""; TxtAdminPageStatus.Text = "";
        if (_connEntry != null && _selServer?.Entry == _connEntry) TxtSrvName.Text = _connEntry.Name;   // pokaż pobraną nazwę
        ConnectOverlay.Visibility = Visibility.Visible;
        BtnConnect.IsEnabled = true; BtnConnect.Content = Loc.T("connect");
        TxtConnStatus.Text = message ?? "";
    }

    static string ReasonText(string r)
    {
        if (r.StartsWith("LoginFailed|")) return Loc.ServerError(r["LoginFailed|".Length..]);
        return r switch
        {
            "ConnectionFailed" => Loc.T("r_connfailed"),
            "ConnectionRejected" => Loc.T("r_rejected"),
            "Timeout" => Loc.T("r_timeout"),
            "RemoteConnectionClose" => Loc.T("r_remoteclose"),
            _ => Loc.T("r_other", r)
        };
    }

    void Leave_Click(object sender, RoutedEventArgs e)
    {
        _e.Disconnect();
        ShowConnect(null);
    }

    // ---------- automatyczne ponowne łączenie ----------
    void OnDisconnected(string reason)
    {
        if (_wasConnected && !_reconnecting) Sound("lost");   // utrata połączenia (raz, nie przy każdej próbie)
        _haveBaseline = false; _watchingLog = false; ResetCall(); RefreshCalls();
        _watching = false;
        _e.Loopback = false; UpdateLoopButton();
        _e.Disconnect();
        // zły login/hasło nie ma sensu ponawiać; ponawiamy tylko, gdy wcześniej byliśmy połączeni
        bool fatal = reason.StartsWith("LoginFailed|") || _noReconnect;   // ban lub rozłączenie przez admina: bez automatycznego łączenia
        if (!fatal && _s.AutoReconnect && (_wasConnected || _reconnecting))
        {
            _trying = false;
            _lastReason = ReasonText(reason);
            if (!_reconnecting) { _reconnecting = true; _attempt = 0; _countdown = 2; }
            else _countdown = _attempt < 5 ? 3 : 10;   // po kilku nieudanych próbach łączymy rzadziej
            ReconnectOverlay.Visibility = Visibility.Visible;
            _retryTimer.Start();
            UpdateReconnectText();
            return;
        }
        ShowConnect(ReasonText(reason));
    }

    void RetryTimer_Tick(object? sender, EventArgs e)
    {
        if (!_reconnecting || _trying) return;
        if (--_countdown > 0) { UpdateReconnectText(); return; }
        _trying = true; _attempt++;
        UpdateReconnectText();
        try { _e.Connect(_host, _port, _s.ServerPassword, _s.Nick); }
        catch (Exception ex)
        {
            _trying = false; _lastReason = ex.Message; _countdown = 5;
            UpdateReconnectText();
        }
    }

    void UpdateReconnectText()
    {
        if (!_reconnecting) return;
        TxtReconnectReason.Text = _lastReason;
        TxtReconnectState.Text = _trying ? Loc.T("reconnecting", _attempt) : Loc.T("reconnect_in", _countdown, _attempt + 1);
    }

    void StopReconnect()
    {
        _reconnecting = false; _trying = false;
        _retryTimer.Stop();
        ReconnectOverlay.Visibility = Visibility.Collapsed;
    }

    void CancelReconnect_Click(object sender, RoutedEventArgs e)
    {
        _e.Disconnect();
        ShowConnect(null);
    }

    // ---------- stan z serwera ----------
    void ApplyState(List<ChannelInfo> cs, List<UserInfo> us)
    {
        NotifyChanges(us);
        var me = us.FirstOrDefault(u => u.Id == _myId);
        if (me != null && me.Avatar != _s.Avatar) { _s.Avatar = me.Avatar; RefreshAvatarSelection(); }
        _myRank = me?.Rank ?? Rank.User;
        _myAdminCh = me?.AdminChannelId ?? -1;
        _myChannel = me?.ChannelId ?? LobbyId;
        RefreshRankUi();

        for (int i = _channels.Count - 1; i >= 0; i--)
            if (!cs.Any(c => c.Id == _channels[i].Id)) _channels.RemoveAt(i);
        for (int i = 0; i < cs.Count; i++)
        {
            var c = cs[i];
            var vm = _channels.FirstOrDefault(x => x.Id == c.Id);
            if (vm == null) { vm = new ChannelVM { Id = c.Id }; _channels.Insert(Math.Min(i, _channels.Count), vm); }
            vm.Name = c.Name; vm.Color = c.Color; vm.HasPassword = c.HasPassword; vm.Access = c.Access;
            int cur = _channels.IndexOf(vm);
            if (cur != i) _channels.Move(cur, i);
        }

        var alive = us.Select(u => u.Id).ToHashSet();
        foreach (var id in _uv.Keys.Where(k => !alive.Contains(k)).ToList()) _uv.Remove(id);
        foreach (var u in us)
        {
            if (!_uv.TryGetValue(u.Id, out var v)) _uv[u.Id] = v = new UserVM { Id = u.Id };
            v.Nick = u.Nick; v.AvatarId = u.Avatar; v.ChannelId = u.ChannelId; v.Rank = u.Rank; v.AdminChannelId = u.AdminChannelId; v.IsMe = u.Id == _myId;
            v.ChannelName = cs.FirstOrDefault(c => c.Id == u.ChannelId)?.Name ?? "";
            v.Afk = u.Afk;
            if (!v.IsMe)   // zapamiętana głośność tego rozmówcy (po nicku)
            {
                var sv = _s.GetUserVolume(u.Nick);
                v.Volume = sv.Volume; v.UserMuted = sv.Muted;
                _e.SetUserGain(u.Id, v.Gain);
            }
        }
        foreach (var ch in _channels)
        {
            ch.Users.Clear();
            foreach (var u in us.Where(u => u.ChannelId == ch.Id)) ch.Users.Add(_uv[u.Id]);
        }
        foreach (var ch in _channels) ch.Denied = !CanEnter(ch);
        foreach (var v in _uv.Values) v.CanManageUser = CanManageUser(v);
        foreach (var dw in _dms.Values) dw.SetPeer(FindUser(dw.Nick)?.Id ?? -1);   // okna rozmów prywatnych: kto jest online
        FillDefChannels();
        if (_pendingAutoJoin && me != null)   // domyślny kanał wybrany przez użytkownika dla tego serwera
        {
            _pendingAutoJoin = false;
            if (_s.DefaultChannels.TryGetValue(SrvKey, out int defId) && defId != _myChannel)
            {
                var dc = _channels.FirstOrDefault(c => c.Id == defId);
                if (dc != null && !dc.Denied) Dispatcher.InvokeAsync(() => JoinChannel(dc));
            }
        }
        _allUsers.Clear();
        foreach (var u in us) _allUsers.Add(_uv[u.Id]);
    }

    void RefreshRankUi()
    {
        BtnNavClients.Visibility = IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        if (!IsAdmin && PgClients.Visibility == Visibility.Visible) ShowPage("ch");
        BtnAddChannel.Visibility = IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        BtnNavAdmin.Visibility = IsAdmin ? Visibility.Visible : Visibility.Collapsed;   // panel admina: osobna strona, tylko dla Admina
        if (!IsAdmin && PgAdmin.Visibility == Visibility.Visible) ShowPage("ch");
        PnlAdminLogin.Visibility = IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        TxtMeRank.Text = _myRank switch { Rank.Admin => "Admin", Rank.ChannelAdmin => "Channel Admin", _ => "User" };
        TxtChannelsHint.Text = _myRank switch
        {
            Rank.Admin => Loc.T("hint_admin"),
            Rank.ChannelAdmin => Loc.T("hint_ca"),
            _ => ""
        };
    }

    void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_e.IsConnected) return;
        foreach (var v in _uv.Values) v.IsSpeaking = _e.IsSpeaking(v.Id);
        MicBar.Value = _e.MicLevel;
        if (++_tick % 5 == 0) UpdateNet();
        if (_tick % 50 == 0) CheckAfk();   // co ok. 5 s
    }

    void UpdateNet()
    {
        int ping = _e.Ping; float loss = _e.Loss;
        TxtPing.Text = ping + " ms";
        int bars = ping < 50 ? 4 : ping < 100 ? 3 : ping < 200 ? 2 : 1;
        if (loss > 5) bars = Math.Max(1, bars - 1);
        var on = Theme.Accent; var off = Theme.B("#4A5266");
        Bar1.Fill = bars >= 1 ? on : off; Bar2.Fill = bars >= 2 ? on : off;
        Bar3.Fill = bars >= 3 ? on : off; Bar4.Fill = bars >= 4 ? on : off;
    }

    // ---------- kanały: wejście, menu ----------
    void Channel_Down(object sender, MouseButtonEventArgs e)
    {
        _downChannel = (sender as FrameworkElement)?.Tag as ChannelVM;
        _dragStart = e.GetPosition(null);
    }

    void Channel_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChannelVM c } && _downChannel == c) JoinChannel(c);
        _downChannel = null;
    }

    void JoinChannel(ChannelVM c)
    {
        if (c.Id == _myChannel) return;
        if (c.Denied) { MessageBox.Show(this, Loc.ServerError("RankRequired"), "VOXLY"); return; }
        if (c.HasPassword && !CanManage(c))
        {
            var pwd = Dialogs.Prompt(this, Loc.T("ch_pwd_title"), Loc.T("ch_pwd_prompt", c.Name), true);
            if (pwd == null) return;
            _e.Join(c.Id, pwd);
        }
        else _e.Join(c.Id);
    }

    static ContextMenu NewMenu() => new() { Style = (Style)Application.Current.FindResource("DarkContextMenu") };   // ciemne menu z App.xaml

    static MenuItem Item(string header, Action act, bool check = false)
    {
        var m = new MenuItem { Header = header, IsChecked = check, Style = (Style)Application.Current.FindResource("DarkMenuItem") };
        m.Click += (_, _) => act();
        return m;
    }

    void Channel_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ChannelVM c } || !CanManage(c)) return;
        var menu = NewMenu();
        menu.Items.Add(Item(Loc.T("m_edit"), () =>
        {
            var r = ChannelDialog.Show(this, Loc.T("m_edit_title"), c.Name, c.Color, c.HasPassword, true, c.Access, IsAdmin && c.Id != LobbyId);
            if (r != null) _e.EditChannel(c.Id, r.Name, r.Color, r.PwdMode, r.Password, r.Access);
        }));
        if (IsAdmin && c.Id != LobbyId)
            menu.Items.Add(Item(Loc.T("m_delete"), () =>
            {
                if (MessageBox.Show(this, Loc.T("m_delete_q", c.Name), Loc.T("m_delete"),
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) _e.DeleteChannel(c.Id);
            }));
        menu.IsOpen = true;
    }

    void AddChannel_Click(object sender, RoutedEventArgs e)
    {
        var r = ChannelDialog.Show(this, Loc.T("m_new_title"), "", "#3B82F6", false, true, Wire.AccessAll, true);
        if (r != null) _e.CreateChannel(r.Name, r.Color, r.PwdMode == 2 ? r.Password : "", r.Access);
    }

    // ---------- zarządzanie użytkownikiem: rangi i przenoszenie ----------
    void UserManage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: UserVM t }) OpenUserDialog(t);
    }

    void User_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UserVM t } row) return;
        var menu = NewMenu();
        if (!t.IsMe) menu.Items.Add(Item(Loc.T("m_dm"), () => OpenDm(t.Nick, true)));
        if (!t.IsMe) menu.Items.Add(Item(Loc.T("m_volume"), () => ShowVolumePopup(t, row)));
        if (CanManageUser(t)) menu.Items.Add(Item(Loc.T("m_manage"), () => OpenUserDialog(t)));
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }

    // ---------- głośność poszczególnych użytkowników ----------
    void UserVolume_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: UserVM t } btn) ShowVolumePopup(t, btn);
    }

    void ShowVolumePopup(UserVM t, FrameworkElement target)
    {
        if (t.IsMe) return;
        var muted = Theme.Muted;
        var valueText = new TextBlock { Foreground = muted, HorizontalAlignment = HorizontalAlignment.Right };
        var slider = new Slider
        {
            Minimum = 0, Maximum = 200, Value = t.Volume, Width = 230, SmallChange = 5, LargeChange = 10,
            TickFrequency = 50, TickPlacement = TickPlacement.BottomRight, Margin = new Thickness(0, 6, 0, 0)
        };
        var mute = new CheckBox { Content = Loc.T("vol_mute"), IsChecked = t.UserMuted, Margin = new Thickness(0, 10, 0, 0) };
        var reset = new Button
        {
            Content = Loc.T("vol_reset"), Background = Theme.CardHead, Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left
        };
        void Apply()
        {
            t.Volume = (int)Math.Round(slider.Value);
            t.UserMuted = mute.IsChecked == true;
            valueText.Text = t.Volume + "%";
            _s.SetUserVolume(t.Nick, t.Volume, t.UserMuted);   // zapis do pliku dopiero po zamknięciu okienka
            _e.SetUserGain(t.Id, t.Gain);
        }
        valueText.Text = t.Volume + "%";
        slider.ValueChanged += (_, _) => Apply();
        mute.Checked += (_, _) => Apply();
        mute.Unchecked += (_, _) => Apply();
        reset.Click += (_, _) => { slider.Value = 100; mute.IsChecked = false; Apply(); };

        var header = new DockPanel();
        DockPanel.SetDock(valueText, Dock.Right);
        header.Children.Add(valueText);
        header.Children.Add(new TextBlock { Text = t.Nick, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 16, 0) });
        var panel = new StackPanel();
        panel.Children.Add(header); panel.Children.Add(slider); panel.Children.Add(mute); panel.Children.Add(reset);
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("vol_hint"), Foreground = muted, FontSize = 11, TextWrapping = TextWrapping.Wrap,
            MaxWidth = 230, Margin = new Thickness(0, 10, 0, 0)
        });
        var border = new Border
        {
            Child = panel, Background = Theme.Card, BorderBrush = Theme.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14)
        };
        System.Windows.Documents.TextElement.SetForeground(border, Theme.Text);   // Popup nie dziedziczy koloru tekstu z okna
        var popup = new Popup
        {
            PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false,
            AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Child = border
        };
        popup.Closed += (_, _) => _s.Save();
        // otwieramy po zamknięciu ewentualnego menu kontekstowego, inaczej popup zamknie się od razu
        Dispatcher.InvokeAsync(() => popup.IsOpen = true, DispatcherPriority.Background);
    }

    void OpenUserDialog(UserVM t)
    {
        if (!CanManageUser(t)) return;
        var r = UserDialog.Show(this, t, _channels.ToList(), IsAdmin, t.ChannelId != LobbyId);
        if (r == null) return;
        if (r.MoveToLobby) _e.Kick(t.Id);
        else _e.SetRank(t.Id, r.Rank, r.ChannelId);
    }

    // ---------- klienci serwera (online i offline), tylko Admin ----------
    readonly ObservableCollection<ClientVM> _clients = new();
    ICollectionView? _clientsView;
    bool _watching, _noReconnect;

    void InitClientsView()
    {
        _clientsView = CollectionViewSource.GetDefaultView(_clients);
        _clientsView.Filter = ClientFilter;
        _clientsView.SortDescriptions.Add(new SortDescription(nameof(ClientVM.SortKey), ListSortDirection.Ascending));
        if (_clientsView is ICollectionViewLiveShaping live)   // sortowanie i filtr reagują na zmianę stanu bez przeładowania listy
        {
            if (live.CanChangeLiveSorting) { live.LiveSortingProperties.Add(nameof(ClientVM.SortKey)); live.IsLiveSorting = true; }
            if (live.CanChangeLiveFiltering)
            {
                live.LiveFilteringProperties.Add(nameof(ClientVM.Online));
                live.LiveFilteringProperties.Add(nameof(ClientVM.Banned));
                live.IsLiveFiltering = true;
            }
        }
        ClientsList.ItemsSource = _clientsView;
        FillClientFilter();
    }

    void FillClientFilter()
    {
        bool was = _loading; _loading = true;
        int i = Math.Max(0, CmbClientFilter.SelectedIndex);
        CmbClientFilter.ItemsSource = new[] { Loc.T("cl_f_all"), Loc.T("cl_f_online"), Loc.T("cl_f_offline"), Loc.T("cl_f_banned") };
        CmbClientFilter.SelectedIndex = i;
        _loading = was;
    }

    bool ClientFilter(object o)
    {
        var c = (ClientVM)o;
        var q = TxtClientSearch.Text.Trim();
        if (q.Length > 0 && !c.Nick.Contains(q, StringComparison.OrdinalIgnoreCase)) return false;
        return CmbClientFilter.SelectedIndex switch { 1 => c.Online, 2 => !c.Online, 3 => c.Banned, _ => true };
    }

    void ClientFilter_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) _clientsView?.Refresh(); }
    void ClientSearch_Changed(object sender, TextChangedEventArgs e) { if (!_loading) _clientsView?.Refresh(); }

    void ApplyClients(List<ClientInfo> list)
    {
        var ids = list.Select(i => i.Id).ToHashSet();
        for (int i = _clients.Count - 1; i >= 0; i--)
            if (!ids.Contains(_clients[i].Id)) _clients.RemoveAt(i);
        foreach (var info in list)
        {
            var vm = _clients.FirstOrDefault(x => x.Id == info.Id);
            if (vm == null) { vm = new ClientVM { Id = info.Id }; _clients.Add(vm); }
            vm.Update(info, _channels.FirstOrDefault(c => c.Id == info.ChannelId)?.Name ?? "");
        }
        TxtClientStats.Text = Loc.T("cl_stats", list.Count(i => i.Online), list.Count(i => !i.Online), list.Count(i => i.Banned));
    }

    void ClientManage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ClientVM c }) OpenClientDialog(c);
    }

    void OpenClientDialog(ClientVM c)
    {
        if (!IsAdmin || c.You) return;
        var r = ClientDialog.Show(this, c, _channels.ToList());
        if (r == null) return;
        _e.ClientAction(c.Id, (byte)r.Act, r.Rank, r.ChannelId);
    }

    // ---------- aktualizacje (GitHub Releases) ----------
    UpdateInfo? _update;
    bool _updating;

    void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.AutoCheckUpdates = ChkAutoUpdate.IsChecked == true; _s.Save();
    }

    async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckUpdateAsync(true);

    readonly DispatcherTimer _updStatusTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    void SetUpdateStatus(string text, Brush brush, bool autoClear)
    {
        _updStatusTimer.Stop();
        TxtUpdateStatus.Foreground = brush;
        TxtUpdateStatus.Text = text;
        if (autoClear) _updStatusTimer.Start();
    }

    async Task CheckUpdateAsync(bool manual)
    {
        if (_updating) return;
        if (manual)
        {
            SetUpdateStatus(Loc.T("upd_checking"), Theme.Muted, false);
            BtnCheckUpdate.IsEnabled = false;
        }
        try
        {
            var info = await Updater.CheckAsync();
            if (info != null)
            {
                _update = info;
                ShowUpdateBanner();
                SetUpdateStatus(Loc.T("upd_available", info.VersionText), Theme.Green, true);
            }
            else if (manual)
            {
                SetUpdateStatus(Loc.T("upd_latest", Updater.CurrentText), Theme.Green, true);
            }
        }
        catch (Exception ex)
        {
            if (manual) SetUpdateStatus(Loc.T("upd_error", ex.Message), Brushes.IndianRed, false);
        }
        finally { BtnCheckUpdate.IsEnabled = true; }
    }

    void ShowUpdateBanner()
    {
        if (_update == null) return;
        TxtUpdateBanner.Text = Loc.T("upd_banner", _update.VersionText, Updater.CurrentText);
        PbUpdate.Visibility = Visibility.Collapsed;
        BtnUpdateNow.IsEnabled = true; BtnUpdateLater.IsEnabled = true;
        UpdateBanner.Visibility = Visibility.Visible;
    }

    void UpdateLater_Click(object sender, RoutedEventArgs e) => UpdateBanner.Visibility = Visibility.Collapsed;

    async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_update == null || _updating) return;
        _updating = true;
        BtnUpdateNow.IsEnabled = false; BtnUpdateLater.IsEnabled = false;
        PbUpdate.Value = 0; PbUpdate.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<double>(p =>
            {
                PbUpdate.Value = p;
                TxtUpdateBanner.Text = Loc.T("upd_downloading", (int)(p * 100));
            });
            var path = await Updater.DownloadAsync(_update, progress);
            TxtUpdateBanner.Text = Loc.T("upd_installing");
            Updater.LaunchInstaller(path);
            Application.Current.Shutdown();   // instalator zamknie resztę, wgra nową wersję i uruchomi ją ponownie
        }
        catch (Exception ex)
        {
            TxtUpdateBanner.Text = Loc.T("upd_error", ex.Message);
            PbUpdate.Visibility = Visibility.Collapsed;
            BtnUpdateNow.IsEnabled = true; BtnUpdateLater.IsEnabled = true;
            _updating = false;
        }
    }

    // ---------- test mikrofonu ----------
    void Loop_Click(object sender, RoutedEventArgs e)
    {
        _e.Loopback = !_e.Loopback;
        UpdateLoopButton();
    }

    void UpdateLoopButton()
    {
        BtnLoop.Content = Loc.T(_e.Loopback ? "loop_stop" : "loop_start");
        BtnLoop.Background = _e.Loopback ? Theme.B("#D9363E") : Theme.Accent;
    }

    // ---------- kanały: przeciąganie (drag and drop) ----------
    void Channel_Move(object sender, MouseEventArgs e)
    {
        if (!IsAdmin || e.LeftButton != MouseButtonState.Pressed || _downChannel == null) return;
        var d = e.GetPosition(null) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var src = _downChannel;
        _downChannel = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, src.Id), DragDropEffects.Move);
    }

    bool CanDrop(DragEventArgs e) =>
        (IsAdmin && e.Data.GetDataPresent(DragFormat)) ||
        (e.Data.GetDataPresent(UserFormat) && (IsAdmin || (int)e.Data.GetData(UserFormat)! == _myId));

    UserVM? _downUser;
    Point _userDragStart;

    static bool InsideButton(DependencyObject? d, DependencyObject stop)
    {
        while (d != null && d != stop)
        {
            if (d is Button) return true;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    void UserRow_Down(object sender, MouseButtonEventArgs e)
    {
        _downUser = (sender as FrameworkElement)?.Tag as UserVM;
        _userDragStart = e.GetPosition(null);
        if (e.OriginalSource is DependencyObject src && InsideButton(src, (DependencyObject)sender)) _downUser = null;   // klik w przycisk to nie przeciąganie
    }

    void UserRow_Move(object sender, MouseEventArgs e)
    {
        if (_downUser == null || e.LeftButton != MouseButtonState.Pressed) return;
        if (!(_downUser.Id == _myId || IsAdmin)) return;   // swoją osobę może przeciągać każdy, cudzą tylko Admin
        var d = e.GetPosition(null) - _userDragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var u = _downUser;
        _downUser = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(UserFormat, u.Id), DragDropEffects.Move);
    }

    void Card_DragEnter(object sender, DragEventArgs e)
    {
        if (CanDrop(e) && sender is Border b) b.BorderBrush = Theme.Accent;
    }

    void Card_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanDrop(e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    void Card_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b) b.BorderBrush = Brushes.Transparent;
    }

    void Card_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border { Tag: ChannelVM target } b) return;
        b.BorderBrush = Brushes.Transparent;
        if (e.Data.GetDataPresent(UserFormat))   // użytkownik upuszczony na kanał
        {
            int uid = (int)e.Data.GetData(UserFormat)!;
            if (uid == _myId) JoinChannel(target);                       // siebie przenosisz jak zwykłym wejściem (hasło, ranga)
            else if (IsAdmin) _e.MoveUser(uid, target.Id);               // Admin przenosi innych
            e.Handled = true;
            return;
        }
        if (!IsAdmin || !e.Data.GetDataPresent(DragFormat)) return;
        int srcId = (int)e.Data.GetData(DragFormat)!;
        if (srcId != target.Id) _e.MoveChannel(srcId, _channels.IndexOf(target));
        e.Handled = true;
    }

    // ---------- czat ----------
    void AddChat(int id, string text)
    {
        _chat.Add(new ChatLine { Time = DateTime.Now.ToString("HH:mm"), Nick = (_uv.TryGetValue(id, out var u) ? u.Nick : "?") + ":", Text = text });
        Dispatcher.InvokeAsync(() => ChatScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    void SendChat_Click(object sender, RoutedEventArgs e)
    {
        var t = TxtChat.Text.Trim();
        if (t.Length == 0) return;
        _e.SendChat(t); TxtChat.Clear();
    }

    void TxtChat_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SendChat_Click(sender, e); e.Handled = true; }
    }

    // ---------- nawigacja ----------
    void Nav_Click(object sender, RoutedEventArgs e) => ShowPage((string)((Button)sender).Tag);

    void ShowPage(string p)
    {
        PgClients.Visibility = p == "clients" && IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        bool want = p == "clients" && IsAdmin;   // lista klientów jest odświeżana na żywo tylko na otwartej stronie
        if (want != _watching) { _watching = want; if (_e.IsConnected) _e.WatchClients(want); }
        PgChannels.Visibility = p == "ch" ? Visibility.Visible : Visibility.Collapsed;
        PgChat.Visibility = p == "chat" ? Visibility.Visible : Visibility.Collapsed;
        PgUsers.Visibility = p == "users" ? Visibility.Visible : Visibility.Collapsed;
        PgSettings.Visibility = p == "set" ? Visibility.Visible : Visibility.Collapsed;
        PgAdmin.Visibility = p == "admin" && IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        bool wantLog = p == "admin" && IsAdmin;   // dziennik jest odświeżany na żywo tylko na otwartej stronie panelu
        if (wantLog != _watchingLog) { _watchingLog = wantLog; if (_e.IsConnected) _e.WatchLog(wantLog); }
    }

    // ---------- przyciski dolnego paska ----------
    void Mic_Click(object sender, RoutedEventArgs e) { _e.MicMuted = !_e.MicMuted; UpdateMuteButtons(); }

    void Deaf_Click(object sender, RoutedEventArgs e)
    {
        _e.Deafened = !_e.Deafened;
        _e.ApplyVolume();
        UpdateMuteButtons();
    }

    void UpdateMuteButtons()
    {
        bool mic = _e.MicMuted || _e.Deafened;   // zagłuszenie wycisza też mikrofon
        BtnMic.Background = mic ? Theme.B("#D9363E") : Theme.Accent;
        MicSlash.Visibility = mic ? Visibility.Visible : Visibility.Collapsed;
        LblMic.Text = Loc.T(mic ? "unmute_mic" : "mute_mic");
        BtnDeaf.Background = _e.Deafened ? Theme.B("#D9363E") : Theme.Accent;
        DeafSlash.Visibility = _e.Deafened ? Visibility.Visible : Visibility.Collapsed;
        LblDeaf.Text = Loc.T(_e.Deafened ? "unmute_sound" : "mute_sound");
    }

    // ---------- ustawienia ----------
    string KeyName() => _s.PttKey switch
    {
        0x04 => Loc.T("key_mid"),
        0x05 => Loc.T("key_x1"),
        0x06 => Loc.T("key_x2"),
        var vk => KeyInterop.KeyFromVirtualKey(vk).ToString()
    };

    void Mic_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CmbMic.SelectedIndex < 0) return;
        _s.MicDevice = CmbMic.SelectedIndex == 0 ? "" : (string)CmbMic.SelectedItem;
        _s.Save(); _e.RestartAudio();
    }

    void Spk_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CmbSpk.SelectedIndex < 0) return;
        _s.SpeakerDevice = CmbSpk.SelectedIndex == 0 ? "" : (string)CmbSpk.SelectedItem;
        _s.Save(); _e.RestartAudio();
    }

    void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Mode = RbPtt.IsChecked == true ? "PTT" : "VAD";
        _s.Save();
    }

    void PttKey_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        BtnPttKey.Content = Loc.T("ptt_press");
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        _capturing = false;
        if (key != Key.Escape) { _s.PttKey = KeyInterop.VirtualKeyFromKey(key); _s.Save(); }
        BtnPttKey.Content = Loc.T("ptt_key", KeyName());
    }

    // push-to-talk pod przyciskiem myszy (lewy i prawy pomijamy, żeby nie blokować klikania)
    void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_capturing) return;
        int vk = e.ChangedButton switch
        {
            MouseButton.Middle => 0x04,
            MouseButton.XButton1 => 0x05,
            MouseButton.XButton2 => 0x06,
            _ => 0
        };
        if (vk == 0) return;
        e.Handled = true;
        _capturing = false;
        _s.PttKey = vk; _s.Save();
        BtnPttKey.Content = Loc.T("ptt_key", KeyName());
    }

    void Threshold_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.Threshold = SldThreshold.Value; _s.Save();
    }

    readonly DispatcherTimer _volSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _s.Volume = SldVolume.Value;
        _e.ApplyVolume();
        UpdateVolLabel();
        _volSaveTimer.Stop(); _volSaveTimer.Start();   // zapis do pliku dopiero chwilę po zakończeniu ruchu suwaka
    }

    void Volume_Wheel(object sender, MouseWheelEventArgs e)
    {
        SldVolume.Value = Math.Clamp(SldVolume.Value + (e.Delta > 0 ? 0.05 : -0.05), 0, 1);
        e.Handled = true;
    }

    void UpdateVolLabel()
    {
        int pct = (int)Math.Round(SldVolume.Value * 100);
        LblVol.Text = Loc.T("vol_master", pct);
        IcoVol.Text = pct == 0 ? "\uE74F" : "\uE767";
    }

    void AdminLogin_Click(object sender, RoutedEventArgs e)
    {
        if (PwdAdmin.Password.Length > 0) _e.AdminLogin(PwdAdmin.Password);
    }

    void SaveSrvPwd_Click(object sender, RoutedEventArgs e)
    {
        _e.SetServerPassword(ChkSrvPwd.IsChecked == true, TxtSrvPwd.Text);
        TxtSrvPwd.Clear();
    }

    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _timer.Stop();
        _retryTimer.Stop();
        _volSaveTimer.Stop();
        _tray?.Dispose();
        _s.Save();   // zapis ustawień (np. głośności), gdyby timer jeszcze nie zdążył
        _e.Dispose();
    }
}
