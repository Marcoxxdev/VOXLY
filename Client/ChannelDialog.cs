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

        // ----- wybór koloru: jeden pasek wszystkich barw (tęcza), pasek jasności i pole HEX; wszystko jest zsynchronizowane -----
        var custom = new TextBox { Text = selected, Width = 110, MaxLength = 7, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 12) };
        var hueBar = new GradientBar { Margin = new Thickness(0, 6, 0, 8), Fill = ColorMath.RainbowBrush() };
        var lightBar = new GradientBar { Margin = new Thickness(0, 6, 0, 8) };
        double H = 0, S = 1, L = 0.5;
        bool syncing = false;

        void ApplyHsl(bool writeHex)
        {
            selected = ColorMath.ToHex(H, S, L);
            Paint(selected);
            lightBar.Fill = ColorMath.LightnessBrush(H, S);
            hueBar.ThumbFill = Theme.B(ColorMath.ToHex(H, 1, 0.5));
            lightBar.ThumbFill = Theme.B(selected);
            if (writeHex) { syncing = true; custom.Text = selected; syncing = false; }
        }
        void SetFromHex(string hex)
        {
            ColorMath.FromHex(hex, out H, out S, out L);
            syncing = true; hueBar.Value = H / 360.0; lightBar.Value = L; syncing = false;
            ApplyHsl(false);
            selected = hex.ToUpperInvariant(); Paint(selected);   // dokładnie to, co wpisał użytkownik
        }
        hueBar.ValueChanged += v => { if (syncing) return; H = v * 360.0; if (S < 0.05) S = 1; ApplyHsl(true); };
        lightBar.ValueChanged += v => { if (syncing) return; L = v; ApplyHsl(true); };
        custom.TextChanged += (_, _) =>
        {
            if (syncing) return;
            var t = custom.Text.Trim();
            if (Hex.IsMatch(t)) SetFromHex(t);
        };
        SetFromHex(selected);

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
        root.Children.Add(hueBar);
        root.Children.Add(new TextBlock { Text = Loc.T("d_light") });
        root.Children.Add(lightBar);
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

/// <summary>Poziomy pasek z gradientem i uchwytem: miejsce, w którym zostawisz uchwyt, wyznacza wartość 0..1.</summary>
sealed class GradientBar : Grid
{
    const double Th = 22;   // średnica uchwytu
    readonly Border _track = new() { Height = 14, CornerRadius = new CornerRadius(7), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Th / 2, 0, Th / 2, 0) };
    readonly Canvas _canvas = new() { Height = Th, VerticalAlignment = VerticalAlignment.Center };
    readonly System.Windows.Shapes.Ellipse _thumb = new() { Width = Th, Height = Th, Stroke = Brushes.White, StrokeThickness = 3 };
    double _value;
    bool _drag;

    public event Action<double>? ValueChanged;

    public Brush Fill { set => _track.Background = value; }
    public Brush ThumbFill { set => _thumb.Fill = value; }
    public double Value { get => _value; set { _value = Math.Clamp(value, 0, 1); Place(); } }

    public GradientBar()
    {
        Height = Th + 4;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;   // żeby cały pasek reagował na mysz
        Children.Add(_track);
        Children.Add(_canvas);
        _canvas.Children.Add(_thumb);
        SizeChanged += (_, _) => Place();
        MouseLeftButtonDown += (_, e) => { _drag = true; CaptureMouse(); Pick(e.GetPosition(this).X); };
        MouseMove += (_, e) => { if (_drag) Pick(e.GetPosition(this).X); };
        MouseLeftButtonUp += (_, _) => { _drag = false; ReleaseMouseCapture(); };
    }

    void Pick(double x)
    {
        double w = Math.Max(1, ActualWidth - Th);
        Value = (x - Th / 2) / w;
        ValueChanged?.Invoke(_value);
    }

    void Place()
    {
        Canvas.SetLeft(_thumb, _value * Math.Max(0, ActualWidth - Th));
        Canvas.SetTop(_thumb, 0);
    }
}

/// <summary>Przeliczenia HSL i kod HEX oraz gradienty dla pasków wyboru koloru.</summary>
static class ColorMath
{
    public static (byte r, byte g, byte b) Rgb(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double hp = (h % 360.0) / 60.0;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        double r1, g1, b1;
        if (hp < 1) (r1, g1, b1) = (c, x, 0);
        else if (hp < 2) (r1, g1, b1) = (x, c, 0);
        else if (hp < 3) (r1, g1, b1) = (0, c, x);
        else if (hp < 4) (r1, g1, b1) = (0, x, c);
        else if (hp < 5) (r1, g1, b1) = (x, 0, c);
        else (r1, g1, b1) = (c, 0, x);
        double m = l - c / 2;
        return ((byte)Math.Round(Math.Clamp((r1 + m) * 255, 0, 255)), (byte)Math.Round(Math.Clamp((g1 + m) * 255, 0, 255)), (byte)Math.Round(Math.Clamp((b1 + m) * 255, 0, 255)));
    }

    public static string ToHex(double h, double s, double l)
    {
        var (r, g, b) = Rgb(h, s, l);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    public static void FromHex(string hex, out double h, out double s, out double l)
    {
        double r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
        double g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
        double b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        l = (max + min) / 2;
        s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        if (d == 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d + 6) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
    }

    static Color Col(double h, double s, double l) { var (r, g, b) = Rgb(h, s, l); return Color.FromRgb(r, g, b); }

    public static Brush RainbowBrush()
    {
        var br = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int i = 0; i <= 6; i++) br.GradientStops.Add(new GradientStop(Col(i * 60.0, 1, 0.5), i / 6.0));
        br.Freeze();
        return br;
    }

    public static Brush LightnessBrush(double h, double s)
    {
        var br = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        br.GradientStops.Add(new GradientStop(Colors.Black, 0));
        br.GradientStops.Add(new GradientStop(Col(h, s, 0.5), 0.5));
        br.GradientStops.Add(new GradientStop(Colors.White, 1));
        br.Freeze();
        return br;
    }
}
