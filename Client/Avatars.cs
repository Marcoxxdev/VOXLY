using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VoiceShared;

namespace VoiceClient;

/// <summary>19 gotowych awatarów (obrazki Assets/Avatars/avNN.png, wkompilowane w program). Numer 0 = brak (litera z nicku).</summary>
public static class Avatars
{
    public const int Count = 19;

    public static readonly FontFamily TextFont = new("Segoe UI");

    static readonly Dictionary<int, ImageBrush> Cache = new();

    public static bool Has(int id) => id >= 1 && id <= Count;

    /// <summary>Pędzel z surowych pikseli 64x64 (premultiplied BGRA). Wątek interfejsu.</summary>
    public static Brush FromRaw(byte[] raw)
    {
        var bmp = BitmapSource.Create(Wire.AvatarPx, Wire.AvatarPx, 96, 96, PixelFormats.Pbgra32, null, raw, Wire.AvatarPx * 4);
        bmp.Freeze();
        var b = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        b.Freeze();
        return b;
    }

    /// <summary>Pędzel ze spakowanych danych z sieci albo null, gdy dane są nieprawidłowe (bez dekodowania żadnego formatu obrazu).</summary>
    public static Brush? FromPacked(byte[] packed)
    {
        var raw = Wire.UnpackAvatar(packed);
        return raw == null ? null : FromRaw(raw);
    }

    /// <summary>Pędzel z obrazkiem awatara (do tła okrągłego Border). Wywołuj tylko z wątku interfejsu.</summary>
    public static Brush Image(int id)
    {
        if (Cache.TryGetValue(id, out var b)) return b;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri($"pack://application:,,,/Assets/Avatars/av{id:00}.png", UriKind.Absolute);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        b = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        b.Freeze();
        Cache[id] = b;
        return b;
    }
}
