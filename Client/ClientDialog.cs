using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoiceShared;

namespace VoiceClient;

public enum ClientAct : byte { None = 0, SetRank = 1, Ban = 2, Unban = 3, Forget = 4, Kick = 5 }   // wartości = Wire.Act*

public record ClientDialogResult(ClientAct Act, Rank Rank, int ChannelId);

/// <summary>Okno zarządzania klientem z rejestru serwera (online lub offline): ranga, rozłączenie, ban, usunięcie z listy.</summary>
public static class ClientDialog
{
    public static ClientDialogResult? Show(Window owner, ClientVM c, IList<ChannelVM> channels)
    {
        var win = new Window
        {
            Title = Loc.T("cd_title"), Owner = owner, Width = 470, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = Theme.Panel, Foreground = Theme.Text, FontSize = 14
        };
        var muted = Theme.Muted;
        ClientDialogResult? result = null;
        var root = new StackPanel { Margin = new Thickness(20) };

        // ----- nagłówek -----
        var avatar = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(26), Background = c.AvatarBrush,
            Child = new TextBlock
            {
                Text = c.Initial, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        status.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = c.StatusBrush, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        status.Children.Add(new TextBlock { Text = c.StatusText, Foreground = muted, FontSize = 12 });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        names.Children.Add(new TextBlock { Text = c.Nick, FontSize = 20, FontWeight = FontWeights.SemiBold });
        names.Children.Add(status);
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(avatar); head.Children.Add(names);
        root.Children.Add(head);

        root.Children.Add(new TextBlock { Text = Loc.T("cd_first", ClientVM.Full(c.FirstSeen)), Foreground = muted, FontSize = 12 });
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("cd_last", c.Online ? Loc.T("cl_online") : ClientVM.Full(c.LastSeen)),
            Foreground = muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 16)
        });

        // ----- ranga (działa także dla klienta offline: obowiązuje od następnego wejścia) -----
        root.Children.Add(new TextBlock { Text = Loc.T("ud_rank"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        int defaultChannel = c.ChannelId >= 0 ? c.ChannelId : channels[0].Id;
        var rs = new RankSection(c.Rank, c.AdminChannelId, defaultChannel, channels);
        root.Children.Add(rs.Panel);
        var save = new Button { Content = Loc.T("ud_save"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(18, 8, 18, 8) };
        save.IsEnabled = rs.HasChanges;
        rs.SelectionChanged += () => save.IsEnabled = rs.HasChanges;
        save.Click += (_, _) =>
        {
            result = new ClientDialogResult(ClientAct.SetRank, rs.Selected, rs.Selected == Rank.ChannelAdmin ? rs.SelectedChannelId : -1);
            win.DialogResult = true;
        };
        root.Children.Add(save);

        // ----- akcje -----
        root.Children.Add(new TextBlock { Text = Loc.T("ud_actions"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) });
        var actions = new WrapPanel();

        void AddAction(string label, ClientAct act, string? confirm, Brush bg)
        {
            var b = new Button { Content = label, Background = bg, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 8) };
            b.Click += (_, _) =>
            {
                if (confirm != null &&
                    MessageBox.Show(win, confirm, "VOXLY", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                result = new ClientDialogResult(act, c.Rank, -1);
                win.DialogResult = true;
            };
            actions.Children.Add(b);
        }

        if (c.Online) AddAction(Loc.T("cd_kick"), ClientAct.Kick, null, Theme.CardHead);
        if (c.Banned) AddAction(Loc.T("cd_unban"), ClientAct.Unban, null, Theme.Accent);
        else AddAction(Loc.T("cd_ban"), ClientAct.Ban, Loc.T("cd_ban_q", c.Nick), Theme.B("#B02A31"));
        if (!c.Online) AddAction(Loc.T("cd_forget"), ClientAct.Forget, Loc.T("cd_forget_q", c.Nick), Theme.CardHead);
        root.Children.Add(actions);
        root.Children.Add(new TextBlock { Text = Loc.T("cd_ban_note"), Foreground = muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });

        var close = new Button
        {
            Content = Loc.T("ud_close"), IsCancel = true, Background = Theme.CardHead, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(18, 8, 18, 8)
        };
        root.Children.Add(close);

        win.Content = root;
        return win.ShowDialog() == true ? result : null;
    }
}
