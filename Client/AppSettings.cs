using System.IO;
using System.Text.Json;

namespace VoiceClient;

public class AppSettings
{
    public string Host { get; set; } = "";
    public string Nick { get; set; } = "";
    public string ServerPassword { get; set; } = "";
    public bool Remember { get; set; } = true;
    public string MicDevice { get; set; } = "";
    public string SpeakerDevice { get; set; } = "";
    public string Mode { get; set; } = "VAD";      // "VAD" albo "PTT"
    public int PttKey { get; set; } = 0x56;         // kod klawisza Windows (domyślnie V)
    public double Threshold { get; set; } = 0.35;   // 0..1 (skala dB, patrz VoiceEngine)
    public double Volume { get; set; } = 1.0;
    public int Avatar { get; set; } = 0;            // 0 = litera z nicku, 1..19 = awatar z listy
    public string Language { get; set; } = "";      // "pl" / "en"; puste = język systemu
    public bool AutoReconnect { get; set; } = true; // automatyczne łączenie po utracie połączenia
    /// <summary>Zapisane serwery (ekran łączenia): własna nazwa, adres, nick i hasło.</summary>
    public List<ServerEntry> Servers { get; set; } = new();
    public string LastServerId { get; set; } = "";
    public double ChannelScale { get; set; } = 1.0;    // rozmiar listy kanałów (0.7-1.4)
    public bool AutoCheckUpdates { get; set; } = true; // sprawdzanie nowych wersji na GitHubie przy starcie
    /// <summary>Głośność poszczególnych rozmówców (tylko u mnie), klucz = nick małymi literami.</summary>
    public Dictionary<string, UserVolume> UserVolumes { get; set; } = new();
    /// <summary>Stały, losowy identyfikator tego komputera. Serwer zapisuje pod nim rangi. Nie udostępniaj settings.json.</summary>
    public string ClientId { get; set; } = "";

    public UserVolume GetUserVolume(string nick) =>
        UserVolumes.TryGetValue(nick.ToLowerInvariant(), out var v) ? v : new UserVolume();

    public void SetUserVolume(string nick, int volume, bool muted)
    {
        var key = nick.ToLowerInvariant();
        if (volume == 100 && !muted) UserVolumes.Remove(key);
        else UserVolumes[key] = new UserVolume { Volume = volume, Muted = muted };
    }

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voxly", "settings.json");

    public static AppSettings Load()
    {
        AppSettings s;
        try { s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { s = new(); }
        // migracja: dotychczasowy pojedynczy serwer trafia na listę
        if (s.Servers.Count == 0 && s.Host.Length > 0)
            s.Servers.Add(new ServerEntry { Name = s.Host, Host = s.Host, Nick = s.Nick, Password = s.ServerPassword });
        if (s.ChannelScale < 0.7 || s.ChannelScale > 1.4) s.ChannelScale = 1.0;
        if (s.ClientId.Length < 16)
        {
            s.ClientId = Guid.NewGuid().ToString("N");
            s.Save();
        }
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var copy = Remember ? this : new AppSettings
            {
                Remember = false, MicDevice = MicDevice, SpeakerDevice = SpeakerDevice,
                Mode = Mode, PttKey = PttKey, Threshold = Threshold, Volume = Volume,
                Avatar = Avatar, Language = Language, AutoReconnect = AutoReconnect, ClientId = ClientId,
                AutoCheckUpdates = AutoCheckUpdates, UserVolumes = UserVolumes,
                Servers = Servers, LastServerId = LastServerId, ChannelScale = ChannelScale
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

public class UserVolume
{
    public int Volume { get; set; } = 100;   // 0-200 %
    public bool Muted { get; set; }
}

public class ServerEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";       // własna nazwa serwera
    public string Host { get; set; } = "";       // adres IP (opcjonalnie z portem: 1.2.3.4:9050)
    public string Nick { get; set; } = "";
    public string Password { get; set; } = "";   // hasło serwera (jeśli wymagane)
}
