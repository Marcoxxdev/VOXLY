namespace VoiceClient;

/// <summary>Ikona w zasobniku systemowym Windows z powiadomieniami (dymki/toasty) i małym menu.</summary>
public sealed class TrayService : IDisposable
{
    readonly System.Windows.Forms.NotifyIcon _icon;
    readonly System.Windows.Forms.ToolStripMenuItem _show = new(), _exit = new();

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        _icon = new System.Windows.Forms.NotifyIcon { Text = "VOXLY", Visible = true };
        var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/voxly.ico"));
        if (res != null) _icon.Icon = new System.Drawing.Icon(res.Stream);

        var menu = new System.Windows.Forms.ContextMenuStrip();
        _show.Click += (_, _) => OpenRequested?.Invoke();
        _exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(_show);
        menu.Items.Add(_exit);
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
    }

    public void SetTexts(string show, string exit) { _show.Text = show; _exit.Text = exit; }

    public void Balloon(string title, string text)
    {
        try { _icon.ShowBalloonTip(5000, title, text, System.Windows.Forms.ToolTipIcon.Info); }
        catch { }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
