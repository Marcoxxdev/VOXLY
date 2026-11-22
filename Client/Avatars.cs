using System.Windows.Media;

namespace VoiceClient;

/// <summary>70 gotowych awatarów (ikony emoji na kolorowym tle). Numer 0 = brak (litera z nicku).</summary>
public static class Avatars
{
    public const int Count = 70;

    public static readonly FontFamily EmojiFont = new("Segoe UI Emoji");
    public static readonly FontFamily TextFont = new("Segoe UI");

    // kolejność = numer awatara - 1 (zmiana kolejności zmienia wybrane awatary u użytkowników!)
    static readonly string[] Glyphs =
    {
        "\U0001F52B", "\U0001F4A3", "\U0001F3AF", "\U0001F4A5", "\U0001F525", "\U0001F480", "\U0001F46E", "\U0001F482", "\U0001F681", "\U0001F693",
        "\U0001F697", "\U0001F680", "\U0001F6A4", "\U0001F4B0", "\U0001F48E", "\U0001F60E", "\U0001F920", "\U0001F3A9", "\U0001F4B5", "\U0001F3B0",
        "\U0001F378", "\U0001F334", "\U0001F306", "\U0001F303", "\U0001F3AE", "\U0001F52A", "\U0001F43A", "\U0001F985", "\U0001F98A", "\U0001F43B",
        "\U0001F42F", "\U0001F981", "\U0001F40D", "\U0001F988", "\U0001F409", "\U0001F982", "\U0001F987", "\U0001F417", "\U0001F47D", "\U0001F916",
        "\U0001F47E", "\U0001F47B", "\U0001F383", "\U0001F608", "\U0001F479", "\u26A1",     "\U0001F300", "\U0001F52E", "\U0001F3C6", "\U0001F451",
        // 51-70: archetypy znane z gier (rycerz, mag, elf, wampir, smok...), zwykłe emoji Unicode
        "\u2694\uFE0F", "\U0001F6E1\uFE0F", "\U0001F3F9", "\U0001F9D9", "\U0001F9DD", "\U0001F9DB", "\U0001F9DF", "\U0001F9DE", "\U0001F9DA", "\U0001F9B8",
        "\U0001F9B9", "\U0001F93A", "\U0001F5E1\uFE0F", "\U0001F3F0", "\U0001F432", "\U0001F984", "\U0001F996", "\U0001F9EA", "\U0001F3B2", "\U0001F579\uFE0F"
    };

    static readonly string[] Palette =
    {
        "#2F7BEA", "#8E5BD9", "#E0653D", "#2FA7A0", "#C94B8B", "#7A8B2E",
        "#D1A32A", "#5B6EE1", "#D9363E", "#1F9D55", "#B45309", "#0E7490"
    };

    static readonly Brush[] Backgrounds = Palette.Select(Theme.B).ToArray();

    public static bool Has(int id) => id >= 1 && id <= Count;
    public static string Glyph(int id) => Glyphs[id - 1];
    public static Brush Bg(int id) => Backgrounds[(id - 1) % Backgrounds.Length];
}
