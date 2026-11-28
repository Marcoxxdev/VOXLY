using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VoiceShared;

namespace VoiceClient;

/// <summary>Wynik wyboru awatara: gotowy (Preset 0..19, Raw = null) albo własny obraz (Raw = piksele 64x64 premultiplied BGRA).</summary>
public record AvatarChoice(int Preset, byte[]? Raw);

/// <summary>Okno z gotowymi awatarami oraz przyciskiem własnego obrazu (z dopasowaniem w drugim oknie).</summary>
public static class AvatarDialog
{
    public static AvatarChoice? Show(Window owner, int currentPreset, bool hasCustom)
    {
        AvatarChoice? result = null;
        var win = new Window
        {
            Title = Loc.T("avatar_dialog_title"), Owner = owner, Width = 456, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = Theme.Panel, Foreground = Theme.Text, FontSize = 14
        };
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = Loc.T("avatar_hint"), Foreground = Theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });

        var grid = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        for (int id = 0; id <= Avatars.Count; id++)
        {
            bool none = id == 0;
            var b = new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(26), Margin = new Thickness(0, 0, 6, 6),
                Background = none ? Theme.B("#4A5266") : Avatars.Image(id), BorderThickness = new Thickness(2.5),
                BorderBrush = !hasCustom && id == currentPreset ? Brushes.White : Brushes.Transparent, Cursor = Cursors.Hand
            };
            if (none)
            {
                b.ToolTip = Loc.T("avatar_none");
                b.Child = new TextBlock { Text = "Aa", FontFamily = Avatars.TextFont, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
            int pick = id;
            b.MouseEnter += (_, _) => { if (b.BorderBrush == Brushes.Transparent) b.BorderBrush = Theme.Muted; };
            b.MouseLeave += (_, _) => { if (b.BorderBrush != Brushes.White) b.BorderBrush = !hasCustom && pick == currentPreset ? Brushes.White : Brushes.Transparent; };
            b.MouseLeftButtonUp += (_, _) => { result = new AvatarChoice(pick, null); win.Close(); };
            grid.Children.Add(b);
        }
        root.Children.Add(grid);

        var custom = new Button { Content = Loc.T(hasCustom ? "avatar_custom_active" : "avatar_custom"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 7, 14, 7) };
        custom.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = Loc.T("avatar_filter") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico" };
            if (dlg.ShowDialog(win) != true) return;
            var img = LoadImage(dlg.FileName);
            if (img == null) { MessageBox.Show(win, Loc.T("avatar_open_err"), Loc.T("avatar_dialog_title"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var raw = AvatarCropDialog.Show(win, img);
            if (raw != null) { result = new AvatarChoice(0, raw); win.Close(); }
        };
        var cancel = new Button { Content = Loc.T("cancel"), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(14, 7, 14, 7), IsCancel = true };
        cancel.Click += (_, _) => win.Close();
        var bottom = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(cancel, Dock.Right);
        bottom.Children.Add(cancel); bottom.Children.Add(custom);
        root.Children.Add(bottom);

        win.Content = root;
        win.ShowDialog();
        return result;
    }

    /// <summary>Wczytuje obraz z pliku (bardzo duże zmniejsza przy dekodowaniu). Null, gdy to nie obraz.</summary>
    static BitmapSource? LoadImage(string path)
    {
        try
        {
            if (new System.IO.FileInfo(path).Length > 40_000_000) return null;
            var uri = new Uri(path);
            var frame = BitmapDecoder.Create(uri, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            int w = frame.PixelWidth, h = frame.PixelHeight;
            if (w < 1 || h < 1) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            if (Math.Max(w, h) > 1200) { if (w >= h) bmp.DecodePixelWidth = 1200; else bmp.DecodePixelHeight = 1200; }
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}

/// <summary>
/// Dopasowanie własnego awatara: przeciąganie myszką, powiększenie (suwak / kółko myszy), "Wypełnij" (przycina do koła)
/// albo "Cała grafika" (cały obraz widoczny). Wynik: piksele 64x64 gotowe do wysłania.
/// </summary>
public static class AvatarCropDialog
{
    const double V = 280;   // bok pola podglądu

    public static byte[]? Show(Window owner, BitmapSource img)
    {
        double iw = img.PixelWidth, ih = img.PixelHeight;
        double cover = Math.Max(V / iw, V / ih), contain = Math.Min(V / iw, V / ih);
        double minZ = contain / cover;   // <= 1
        double z = 1, ox = 0, oy = 0;
        byte[]? result = null;

        var win = new Window
        {
            Title = Loc.T("avatar_crop_title"), Owner = owner, Width = 360, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = Theme.Panel, Foreground = Theme.Text, FontSize = 14
        };

        var image = new Image { Source = img, Stretch = Stretch.Fill, SnapsToDevicePixels = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var canvas = new Canvas { Width = V, Height = V, ClipToBounds = true, Background = Theme.B("#2B2F3A"), Cursor = Cursors.SizeAll };
        canvas.Children.Add(image);

        // ciemna nakładka z okrągłym "oknem": tak awatar będzie wyglądał na liście
        var mask = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), IsHitTestVisible = false,
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, V, V)), new EllipseGeometry(new Point(V / 2, V / 2), V / 2 - 2, V / 2 - 2))
        };
        var ring = new System.Windows.Shapes.Ellipse { Width = V - 4, Height = V - 4, Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false };
        Canvas.SetLeft(ring, 2); Canvas.SetTop(ring, 2);
        canvas.Children.Add(mask); canvas.Children.Add(ring);

        var slider = new Slider { Minimum = minZ, Maximum = 4, Value = 1, Margin = new Thickness(0, 10, 0, 0), IsEnabled = true };
        double S() => cover * z;

        void Clamp()
        {
            double sw = iw * S(), sh = ih * S();
            ox = sw >= V ? Math.Clamp(ox, V - sw, 0) : (V - sw) / 2;
            oy = sh >= V ? Math.Clamp(oy, V - sh, 0) : (V - sh) / 2;
        }
        void Layout()
        {
            Clamp();
            image.Width = iw * S(); image.Height = ih * S();
            Canvas.SetLeft(image, ox); Canvas.SetTop(image, oy);
        }
        bool syncing = false;
        void SetZoom(double nz, double ax, double ay)
        {
            nz = Math.Clamp(nz, minZ, 4);
            // punkt pod kursorem zostaje w miejscu
            double px = (ax - ox) / S(), py = (ay - oy) / S();
            z = nz;
            ox = ax - px * S(); oy = ay - py * S();
            Layout();
            syncing = true; slider.Value = z; syncing = false;
        }
        void Fill() { z = 1; ox = (V - iw * S()) / 2; oy = (V - ih * S()) / 2; Layout(); syncing = true; slider.Value = z; syncing = false; }
        void Fit() { z = minZ; ox = (V - iw * S()) / 2; oy = (V - ih * S()) / 2; Layout(); syncing = true; slider.Value = z; syncing = false; }

        slider.ValueChanged += (_, _) => { if (!syncing) SetZoom(slider.Value, V / 2, V / 2); };
        canvas.MouseWheel += (_, e) => SetZoom(z * (e.Delta > 0 ? 1.1 : 1 / 1.1), e.GetPosition(canvas).X, e.GetPosition(canvas).Y);

        Point? last = null;
        canvas.MouseLeftButtonDown += (_, e) => { last = e.GetPosition(canvas); canvas.CaptureMouse(); };
        canvas.MouseMove += (_, e) =>
        {
            if (last == null) return;
            var p = e.GetPosition(canvas);
            ox += p.X - last.Value.X; oy += p.Y - last.Value.Y; last = p;
            Layout();
        };
        canvas.MouseLeftButtonUp += (_, _) => { last = null; canvas.ReleaseMouseCapture(); };

        byte[] Render()
        {
            const int P = Wire.AvatarPx;
            double k = (double)P / V;
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Theme.B("#2B2F3A"), null, new Rect(0, 0, P, P));
                dc.DrawImage(img, new Rect(ox * k, oy * k, iw * S() * k, ih * S() * k));
            }
            var rtb = new RenderTargetBitmap(P, P, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var raw = new byte[P * P * 4];
            rtb.CopyPixels(raw, P * 4, 0);
            return raw;
        }

        var btnFill = new Button { Content = Loc.T("avatar_fill"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var btnFit = new Button { Content = Loc.T("avatar_fit"), Padding = new Thickness(12, 6, 12, 6) };
        btnFill.Click += (_, _) => Fill(); btnFit.Click += (_, _) => Fit();
        var ok = new Button { Content = Loc.T("save"), Padding = new Thickness(16, 6, 16, 6), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Loc.T("cancel"), Padding = new Thickness(16, 6, 16, 6), IsCancel = true };
        ok.Click += (_, _) => { result = Render(); win.Close(); };
        cancel.Click += (_, _) => win.Close();

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        tools.Children.Add(btnFill); tools.Children.Add(btnFit);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(ok); actions.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = Loc.T("avatar_drag_hint"), Foreground = Theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        root.Children.Add(new Border { Width = V, Height = V, HorizontalAlignment = HorizontalAlignment.Center, Child = canvas });
        root.Children.Add(new TextBlock { Text = Loc.T("avatar_zoom"), Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        root.Children.Add(slider);
        root.Children.Add(tools);
        root.Children.Add(actions);
        win.Content = root;

        Fill();
        win.ShowDialog();
        return result;
    }
}
