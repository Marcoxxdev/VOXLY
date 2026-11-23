using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoiceShared;

namespace VoiceClient;

/// <summary>MoveToLobby = true: przenieś użytkownika do Lobby; w przeciwnym razie ustaw rangę (Rank, ChannelId dla Channel Admina).</summary>
public record UserDialogResult(bool MoveToLobby, Rank Rank, int ChannelId);

/// <summary>Trzy karty wyboru rangi (User / Channel Admin / Admin) z opisami i podświetleniem. Używane w oknach zarządzania.</summary>
public sealed class RankSection
{
    static readonly Brush Muted = Theme.B("#8B93A5");
    readonly Rank _current;
    readonly int _currentChannel;
    readonly IList<ChannelVM> _channels;
    readonly RadioButton _rbUser, _rbCa, _rbAdmin;
    readonly ComboBox _combo;
    readonly List<(Border box, RadioButton rb)> _opts = new();

    public StackPanel Panel { get; } = new();
    public event Action? SelectionChanged;

    public Rank Selected => _rbAdmin.IsChecked == true ? Rank.Admin : _rbCa.IsChecked == true ? Rank.ChannelAdmin : Rank.User;
    public int SelectedChannelId => _channels[Math.Clamp(_combo.SelectedIndex, 0, _channels.Count - 1)].Id;
    public bool HasChanges => Selected != _current || (Selected == Rank.ChannelAdmin && SelectedChannelId != _currentChannel);

    public RankSection(Rank current, int currentAdminChannel, int defaultChannel, IList<ChannelVM> channels)
    {
        _current = current; _currentChannel = currentAdminChannel; _channels = channels;
        _combo = new ComboBox { ItemsSource = channels.Select(c => c.Name).ToList() };
        int idx = channels.ToList().FindIndex(c => c.Id == (current == Rank.ChannelAdmin ? currentAdminChannel : defaultChannel));
        _combo.SelectedIndex = Math.Max(0, idx);

        var caExtra = new StackPanel();
        caExtra.Children.Add(new TextBlock { Text = Loc.T("ud_ca_channel"), Foreground = Muted, FontSize = 12, Margin = new Thickness(26, 8, 0, 0) });
        _combo.Margin = new Thickness(26, 2, 0, 0);
        caExtra.Children.Add(_combo);

        _rbUser = Option(Rank.User, "User", Loc.T("ud_user_desc"), null);
        _rbCa = Option(Rank.ChannelAdmin, "Channel Admin", Loc.T("ud_ca_desc"), caExtra);
        _rbAdmin = Option(Rank.Admin, "Admin", Loc.T("ud_admin_desc"), null);

        foreach (var (_, rb) in _opts) rb.Checked += (_, _) => Refresh();
        _combo.SelectionChanged += (_, _) => Refresh();
        Refresh();
    }

    RadioButton Option(Rank rank, string title, string desc, UIElement? extra)
    {
        var rb = new RadioButton { GroupName = "rank", IsChecked = _current == rank, VerticalContentAlignment = VerticalAlignment.Top };
        var titleBlock = new TextBlock { FontWeight = FontWeights.SemiBold };
        titleBlock.Inlines.Add(new System.Windows.Documents.Run(title));
        if (_current == rank)
            titleBlock.Inlines.Add(new System.Windows.Documents.Run(" " + Loc.T("ud_current")) { Foreground = Muted, FontWeight = FontWeights.Normal });
        var content = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        content.Children.Add(titleBlock);
        content.Children.Add(new TextBlock { Text = desc, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), MaxWidth = 360 });
        rb.Content = content;
        var inner = new StackPanel();
        inner.Children.Add(rb);
        if (extra != null) inner.Children.Add(extra);
        var box = new Border
        {
            Child = inner, Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(1.5), Cursor = System.Windows.Input.Cursors.Hand
        };
        box.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not ComboBox) rb.IsChecked = true; };   // klik w całą kartę zaznacza opcję
        Panel.Children.Add(box);
        _opts.Add((box, rb));
        return rb;
    }

    void Refresh()
    {
        foreach (var (box, rb) in _opts)
        {
            bool on = rb.IsChecked == true;
            box.Background = Theme.B(on ? "#24385E" : "#262D3B");
            box.BorderBrush = on ? Theme.B("#2F7BEA") : Brushes.Transparent;
        }
        _combo.IsEnabled = _rbCa.IsChecked == true;
        SelectionChanged?.Invoke();
    }
}

/// <summary>Okno zarządzania zalogowanym użytkownikiem: ranga oraz przeniesienie do Lobby.</summary>
public static class UserDialog
{
    public static UserDialogResult? Show(Window owner, UserVM user, IList<ChannelVM> channels, bool canSetRank, bool canMove)
    {
        var win = new Window
        {
            Title = Loc.T("ud_title"), Owner = owner, Width = 470, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = Theme.B("#1B212C"), Foreground = Theme.Text, FontSize = 14
        };
        var muted = Theme.B("#8B93A5");
        UserDialogResult? result = null;
        var root = new StackPanel { Margin = new Thickness(20) };

        var avatar = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(26), Background = user.AvatarBrush,
            Child = new TextBlock
            {
                Text = user.AvatarText, FontFamily = user.AvatarFont, FontSize = user.AvatarFontSize + 4, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        names.Children.Add(new TextBlock { Text = user.Nick, FontSize = 20, FontWeight = FontWeights.SemiBold });
        names.Children.Add(new TextBlock { Text = RankName(user.Rank), Foreground = muted, FontSize = 12 });
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        head.Children.Add(avatar); head.Children.Add(names);
        root.Children.Add(head);

        if (canSetRank)
        {
            root.Children.Add(new TextBlock { Text = Loc.T("ud_rank"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            var rs = new RankSection(user.Rank, user.AdminChannelId, user.ChannelId, channels);
            root.Children.Add(rs.Panel);
            var save = new Button { Content = Loc.T("ud_save"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(18, 8, 18, 8) };
            save.IsEnabled = rs.HasChanges;
            rs.SelectionChanged += () => save.IsEnabled = rs.HasChanges;
            save.Click += (_, _) =>
            {
                result = new UserDialogResult(false, rs.Selected, rs.Selected == Rank.ChannelAdmin ? rs.SelectedChannelId : -1);
                win.DialogResult = true;
            };
            root.Children.Add(save);
        }

        if (canMove)
        {
            root.Children.Add(new TextBlock { Text = Loc.T("ud_actions"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) });
            var move = new Button { Content = Loc.T("m_to_lobby"), Background = Theme.B("#313A4D"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 8, 18, 8) };
            move.Click += (_, _) => { result = new UserDialogResult(true, user.Rank, -1); win.DialogResult = true; };
            root.Children.Add(move);
        }

        var close = new Button
        {
            Content = Loc.T("ud_close"), IsCancel = true, Background = Theme.B("#313A4D"), HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(18, 8, 18, 8)
        };
        root.Children.Add(close);

        win.Content = root;
        return win.ShowDialog() == true ? result : null;
    }

    static string RankName(Rank r) => r switch { Rank.Admin => "Admin", Rank.ChannelAdmin => "Channel Admin", _ => "User" };
}
