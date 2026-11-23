using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using VoiceShared;

namespace VoiceClient;

public static class Theme
{
    public static Brush B(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }
    public static readonly Brush Green = B("#3DDC57");
    public static readonly Brush Text = B("#E6E9EF");
    static readonly Brush[] Av = new[] { "#2F7BEA", "#8E5BD9", "#E0653D", "#2FA7A0", "#C94B8B", "#7A8B2E", "#D1A32A", "#5B6EE1" }
        .Select(B).ToArray();
    public static Brush Avatar(string nick)
    {
        int h = 0;
        foreach (var ch in nick) h = h * 31 + ch;
        return Av[Math.Abs(h % Av.Length)];
    }
}

public class Notify : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}

public class UserVM : Notify
{
    public int Id { get; set; }
    public int ChannelId { get; set; }
    public int AdminChannelId { get; set; } = -1;
    public bool IsMe { get; set; }
    public string ChannelName { get; set; } = "";

    string _nick = "";
    public string Nick { get => _nick; set { _nick = value; Raise(nameof(Nick), nameof(Initial), nameof(AvatarText), nameof(AvatarBrush)); } }
    Rank _rank;
    public Rank Rank
    {
        get => _rank;
        set { _rank = value; Raise(nameof(Rank), nameof(BadgeText), nameof(BadgeVis), nameof(BadgeBg), nameof(BadgeFg)); }
    }
    bool _speaking;
    public bool IsSpeaking
    {
        get => _speaking;
        set { if (_speaking == value) return; _speaking = value; Raise(nameof(IsSpeaking), nameof(RingBrush), nameof(NickBrush)); }
    }

    int _avatarId;
    public int AvatarId
    {
        get => _avatarId;
        set { _avatarId = value; Raise(nameof(AvatarId), nameof(AvatarText), nameof(AvatarFont), nameof(AvatarFontSize), nameof(AvatarBrush)); }
    }

    bool _canManage;
    /// <summary>Czy zalogowany użytkownik może zarządzać tą osobą (ranga, przeniesienie do Lobby).</summary>
    public bool CanManageUser { get => _canManage; set { if (_canManage == value) return; _canManage = value; Raise(nameof(CanManageUser), nameof(ManageVis)); } }
    public Visibility ManageVis => CanManageUser ? Visibility.Visible : Visibility.Collapsed;

    // ----- AFK, wiadomości prywatne -----
    bool _afk;
    public bool Afk { get => _afk; set { if (_afk == value) return; _afk = value; Raise(nameof(Afk), nameof(AfkVis), nameof(AvatarOpacity)); } }
    public Visibility AfkVis => Afk ? Visibility.Visible : Visibility.Collapsed;
    public double AvatarOpacity => Afk ? 0.5 : 1.0;
    int _unread;
    public int Unread { get => _unread; set { _unread = value; Raise(nameof(Unread), nameof(UnreadText)); } }
    public string UnreadText => Unread > 0 ? Unread.ToString() : "";
    public Visibility DmVis => IsMe ? Visibility.Collapsed : Visibility.Visible;

    // ----- głośność tego rozmówcy (lokalnie) -----
    int _volume = 100;
    bool _userMuted;
    public int Volume { get => _volume; set { _volume = Math.Clamp(value, 0, 200); RaiseVol(); } }
    public bool UserMuted { get => _userMuted; set { _userMuted = value; RaiseVol(); } }
    void RaiseVol() => Raise(nameof(Volume), nameof(UserMuted), nameof(VolumeText), nameof(VolumeGlyph), nameof(VolumeBrush));
    public float Gain => UserMuted ? 0f : Volume / 100f;
    public Visibility VolumeVis => IsMe ? Visibility.Collapsed : Visibility.Visible;
    public string VolumeGlyph => UserMuted || Volume == 0 ? "\uE74F" : "\uE767";
    public string VolumeText => UserMuted ? Loc.T("vol_muted") : Volume == 100 ? "" : Volume + "%";
    public Brush VolumeBrush => UserMuted || Volume == 0 ? Theme.B("#FF7B7B") : Volume != 100 ? Theme.B("#6AB7F0") : Theme.B("#8B93A5");

    public string Initial => Nick.Length > 0 ? Nick[..1].ToUpperInvariant() : "?";
    public string AvatarText => Avatars.Has(AvatarId) ? "" : Initial;   // przy awatarze-obrazku bez litery
    public FontFamily AvatarFont => Avatars.TextFont;
    public double AvatarFontSize => 15;
    public Brush AvatarBrush => Avatars.Has(AvatarId) ? Avatars.Image(AvatarId) : Theme.Avatar(Nick);
    public Brush RingBrush => IsSpeaking ? Theme.Green : Brushes.Transparent;
    public Brush NickBrush => IsSpeaking ? Theme.Green : Theme.Text;

    public string BadgeText => Rank switch { Rank.Admin => "ADMIN", Rank.ChannelAdmin => "CHANNEL ADMIN", _ => "" };
    public Visibility BadgeVis => Rank == Rank.User ? Visibility.Collapsed : Visibility.Visible;
    public Brush BadgeBg => Rank == Rank.Admin ? Theme.B("#33F5C518") : Theme.B("#334AA3E0");
    public Brush BadgeFg => Rank == Rank.Admin ? Theme.B("#F5C518") : Theme.B("#6AB7F0");
}

public class ChannelVM : Notify
{
    public int Id { get; set; }
    string _name = "";
    public string Name { get => _name; set { _name = value; Raise(nameof(Name)); } }
    string _color = "#3B82F6";
    public string Color { get => _color; set { _color = value; Raise(nameof(Color), nameof(ColorBrush)); } }
    bool _pw;
    public bool HasPassword { get => _pw; set { _pw = value; Raise(nameof(HasPassword), nameof(LockVis)); } }
    byte _access = 7;
    public byte Access { get => _access; set { _access = value; Raise(nameof(Access), nameof(AccessVis), nameof(AccessText)); } }
    public Visibility AccessVis => Access == 7 ? Visibility.Collapsed : Visibility.Visible;
    public string AccessText => string.Join(", ", new[] { (1, "User"), (2, "Channel Admin"), (4, "Admin") }
        .Where(x => (Access & x.Item1) != 0).Select(x => x.Item2));
    bool _denied;
    /// <summary>Ranga zalogowanego użytkownika nie ma wstępu do tego kanału.</summary>
    public bool Denied { get => _denied; set { _denied = value; Raise(nameof(Denied), nameof(HeadOpacity)); } }
    public double HeadOpacity => Denied ? 0.5 : 1.0;
    public Brush ColorBrush => Theme.B(Color);
    public Visibility LockVis => HasPassword ? Visibility.Visible : Visibility.Collapsed;
    public ObservableCollection<UserVM> Users { get; } = new();
}

public class ChatLine
{
    public string Time { get; set; } = "";
    public string Nick { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>Wiersz listy klientów serwera (online i offline) widoczny dla Admina.</summary>
public class ClientVM : Notify
{
    public int Id { get; set; }
    public string Nick { get; private set; } = "";
    public bool Online { get; private set; }
    public int ChannelId { get; private set; } = -1;
    public string ChannelName { get; private set; } = "";
    public Rank Rank { get; private set; }
    public int AdminChannelId { get; private set; } = -1;
    public bool Banned { get; private set; }
    public long LastSeen { get; private set; }
    public long FirstSeen { get; private set; }
    public bool You { get; private set; }

    public void Update(ClientInfo i, string channelName)
    {
        Nick = i.Nick; Online = i.Online; ChannelId = i.ChannelId; ChannelName = channelName; Rank = i.Rank;
        AdminChannelId = i.AdminChannelId; Banned = i.Banned; LastSeen = i.LastSeen; FirstSeen = i.FirstSeen; You = i.You;
        RefreshText();
        Raise(nameof(Online), nameof(Banned), nameof(Rank), nameof(SortKey), nameof(Nick), nameof(Initial), nameof(AvatarBrush),
              nameof(BadgeVis), nameof(BadgeBg), nameof(BadgeFg), nameof(BannedVis), nameof(YouVis), nameof(ManageVis), nameof(RowOpacity), nameof(StatusBrush));
    }

    /// <summary>Teksty zależne od języka i od czasu ("5 min temu").</summary>
    public void RefreshText() => Raise(nameof(StatusText), nameof(SubText), nameof(BadgeText));

    public string StatusText => Banned ? Loc.T("cl_banned") : Online ? Loc.T("cl_online") : Loc.T("cl_offline");
    public Brush StatusBrush => Banned ? Theme.B("#D9363E") : Online ? Theme.Green : Theme.B("#5A6275");
    public string SubText => Online
        ? (ChannelName.Length > 0 ? Loc.T("cl_in_channel", ChannelName) : Loc.T("cl_online"))
        : Loc.T("cl_last_seen", Ago(LastSeen));
    public string SortKey => (Online ? "0" : "1") + Nick.ToLowerInvariant();
    public string Initial => Nick.Length > 0 ? Nick[..1].ToUpperInvariant() : "?";
    public Brush AvatarBrush => Theme.Avatar(Nick);
    public double RowOpacity => Online ? 1.0 : 0.72;
    public Visibility BannedVis => Banned ? Visibility.Visible : Visibility.Collapsed;
    public Visibility YouVis => You ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ManageVis => You ? Visibility.Collapsed : Visibility.Visible;
    public string BadgeText => Rank switch { Rank.Admin => "ADMIN", Rank.ChannelAdmin => "CHANNEL ADMIN", _ => "" };
    public Visibility BadgeVis => Rank == Rank.User ? Visibility.Collapsed : Visibility.Visible;
    public Brush BadgeBg => Rank == Rank.Admin ? Theme.B("#33F5C518") : Theme.B("#334AA3E0");
    public Brush BadgeFg => Rank == Rank.Admin ? Theme.B("#F5C518") : Theme.B("#6AB7F0");

    public static string Ago(long unix)
    {
        var then = DateTimeOffset.FromUnixTimeSeconds(unix);
        var d = DateTimeOffset.UtcNow - then;
        if (d.TotalMinutes < 1) return Loc.T("ago_now");
        if (d.TotalHours < 1) return Loc.T("ago_min", (int)d.TotalMinutes);
        if (d.TotalDays < 1) return Loc.T("ago_hour", (int)d.TotalHours);
        if (d.TotalDays < 30) return Loc.T("ago_day", (int)d.TotalDays);
        return then.ToLocalTime().ToString("yyyy-MM-dd");
    }

    public static string Full(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

/// <summary>Wiersz listy zapisanych serwerów na ekranie łączenia.</summary>
public class ServerVM : Notify
{
    public ServerEntry Entry { get; }
    public ServerVM(ServerEntry e) { Entry = e; }
    bool _sel;
    public bool IsSelected { get => _sel; set { _sel = value; Raise(nameof(IsSelected), nameof(RowBg), nameof(RowBorder)); } }
    public string Name => Entry.Name;
    public string Host => Entry.Host;
    public Brush RowBg => IsSelected ? Theme.B("#24385E") : Theme.B("#262D3B");
    public Brush RowBorder => IsSelected ? Theme.B("#2F7BEA") : Brushes.Transparent;
    public void Refresh() => Raise(nameof(Name), nameof(Host));
}
