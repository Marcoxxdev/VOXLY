using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VoiceClient;

/// <summary>PwdMode: 0 = bez zmian, 1 = usuń hasło, 2 = ustaw nowe hasło.</summary>
public record ChannelResult(string Name, string Color, byte PwdMode, string Password, byte Access);

public static class ChannelDialog
{
    static readonly string[] Palette =
    {
        "#EF4444", "#F97316", "#F59E0B", "#EAB308", "#84CC16", "#22C55E", "#3DDC57", "#10B981",
        "#14B8A6", "#06B6D4", "#0EA5E9", "#3B82F6", "#6366F1", "#8B5CF6", "#A855F7", "#D946EF",
        "#EC4899", "#F43F5E", "#B45309", "#065F46", "#1E3A8A", "#78716C", "#9CA3AF", "#E5E7EB"
    };
    static readonly Regex Hex = new("^#[0-9A-Fa-f]{6}$");

    public static ChannelResult? Show(Window owner, string title, string name, string color, bool hasPassword, bool allowPassword = true, byte access = 7, bool allowAccess = false)
    {
        string selected = Hex.IsMatch(color) ? color : "#3B82F6";
        var win = new Window
        {
            Title = title, Owner = owner, Width = 410, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = Theme.B("#1B212C"), Foreground = Theme.Text, FontSize = 14
        };
        var box = new TextBox { Text = name, MaxLength = 32, Margin = new Thickness(0, 4, 0, 12) };

        // podgląd nagłówka kanału
        var dot = new System.Windows.Shapes.Ellipse { Width = 11, Height = 11, Margin = new Thickness(0, 0, 10, 0), Fill = Theme.B(selected) };
        var previewName = new TextBlock { Text = name.Length > 0 ? name : Loc.T("d_name"), FontSize = 16, FontWeight = FontWeights.SemiBold };
        var hexLabel = new TextBlock { Foreground = Theme.B("#8B93A5"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Text = selected };
        var previewRow = new StackPanel { Orientation = Orientation.Horizontal };
        previewRow.Children.Add(dot); previewRow.Children.Add(previewName);
        var dock = new DockPanel();
        DockPanel.SetDock(hexLabel, Dock.Right);
        dock.Children.Add(hexLabel); dock.Children.Add(previewRow);
        var preview = new Border { Background = Theme.B("#313A4D"), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 12), Child = dock };
        box.TextChanged += (_, _) => previewName.Text = box.Text.Length > 0 ? box.Text : Loc.T("d_name");

        void Paint(string c) { dot.Fill = Theme.B(c); hexLabel.Text = c; }

        var swatches = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        var borders = new List<(Border b, string c)>();
        void Refresh() { foreach (var (b, c) in borders) b.BorderBrush = c.Equals(selected, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent; }
        var custom = new TextBox { Text = selected, Width = 110, MaxLength = 7, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 12) };

        foreach (var c in Palette)
        {
            var scale = new ScaleTransform(1, 1);
            var b = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 8, 8),
                Background = Theme.B(c), BorderThickness = new Thickness(2.5), Cursor = Cursors.Hand,
                RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5), ToolTip = c
            };
            var col = c;
            b.MouseEnter += (_, _) => { scale.ScaleX = scale.ScaleY = 1.25; Paint(col); };      // podgląd po najechaniu
            b.MouseLeave += (_, _) => { scale.ScaleX = scale.ScaleY = 1; Paint(selected); };
            b.MouseLeftButtonDown += (_, _) => { selected = col; custom.Text = col; Paint(col); Refresh(); };
            borders.Add((b, c)); swatches.Children.Add(b);
        }
        Refresh();
        custom.TextChanged += (_, _) =>
        {
            var t = custom.Text.Trim();
            if (Hex.IsMatch(t)) { selected = t; Paint(t); Refresh(); }
        };

        var chk = new CheckBox { Content = Loc.T("d_chk_pwd"), IsChecked = hasPassword, Margin = new Thickness(0, 0, 0, 6) };
        var pwd = new TextBox { MaxLength = 64, IsEnabled = hasPassword };
        var hint = new TextBlock
        {
            Text = hasPassword ? Loc.T("d_hint_keep") : Loc.T("d_hint_new"),
            Foreground = Theme.B("#8B93A5"), FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        chk.Checked += (_, _) => pwd.IsEnabled = true;
        chk.Unchecked += (_, _) => pwd.IsEnabled = false;

        // dostęp według rang (tylko Admin; Admin zawsze ma dostęp)
        var chkUser = new CheckBox { Content = "User", IsChecked = (access & 1) != 0, Margin = new Thickness(0, 2, 0, 0) };
        var chkCa = new CheckBox { Content = "Channel Admin", IsChecked = (access & 2) != 0, Margin = new Thickness(0, 2, 0, 0) };
        var chkAdmin = new CheckBox { Content = Loc.T("d_access_admin"), IsChecked = true, IsEnabled = false, Margin = new Thickness(0, 2, 0, 0) };
        var accessHint = new TextBlock
        {
            Text = Loc.T("d_access_hint"), Foreground = Theme.B("#8B93A5"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12)
        };

        ChannelResult? result = null;
        var ok = new Button { Content = Loc.T("save"), Width = 100, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = Loc.T("cancel"), Width = 100, Background = Theme.B("#313A4D"), IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text)) return;
            byte mode = 0;
            if (allowPassword)
                mode = chk.IsChecked != true ? (byte)1 : pwd.Text.Length > 0 ? (byte)2 : (byte)0;
            byte acc = allowAccess
                ? (byte)((chkUser.IsChecked == true ? 1 : 0) | (chkCa.IsChecked == true ? 2 : 0) | 4)
                : access;
            result = new ChannelResult(box.Text.Trim(), selected, mode, pwd.Text, acc);
            win.DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = Loc.T("d_name") });
        root.Children.Add(box);
        root.Children.Add(preview);
        root.Children.Add(new TextBlock { Text = Loc.T("d_color") });
        root.Children.Add(swatches);
        root.Children.Add(new TextBlock { Text = Loc.T("d_custom") });
        root.Children.Add(custom);
        if (allowAccess)
        {
            root.Children.Add(new TextBlock { Text = Loc.T("d_access"), FontWeight = FontWeights.SemiBold });
            root.Children.Add(chkUser); root.Children.Add(chkCa); root.Children.Add(chkAdmin); root.Children.Add(accessHint);
        }
        if (allowPassword) { root.Children.Add(chk); root.Children.Add(pwd); root.Children.Add(hint); }
        root.Children.Add(buttons);
        win.Content = root;
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return win.ShowDialog() == true ? result : null;
    }
}

public static class Dialogs
{
    /// <summary>Proste okno z jednym polem (np. hasło kanału). Zwraca null po anulowaniu.</summary>
    public static string? Prompt(Window owner, string title, string label, bool secret)
    {
        var win = new Window
        {
            Title = title, Owner = owner, Width = 360, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = Theme.B("#1B212C"), Foreground = Theme.Text, FontSize = 14
        };
        Control input = secret ? new PasswordBox { MaxLength = 64 } : new TextBox { MaxLength = 64 };
        string? result = null;
        var ok = new Button { Content = Loc.T("ok"), Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = Loc.T("cancel"), Width = 90, Background = Theme.B("#313A4D"), IsCancel = true };
        ok.Click += (_, _) =>
        {
            result = input is PasswordBox pb ? pb.Password : ((TextBox)input).Text;
            win.DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(input);
        root.Children.Add(buttons);
        win.Content = root;
        input.Loaded += (_, _) => input.Focus();
        return win.ShowDialog() == true ? result : null;
    }
}
