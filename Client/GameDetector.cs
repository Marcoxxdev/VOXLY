using System.Diagnostics;

namespace VoiceClient;

/// <summary>
/// Rozpoznaje uruchomioną grę z gotowej listy (po nazwie procesu, bez .exe). Tylko lista nazw procesów:
/// nie otwiera procesów gier, nie czyta ich pamięci i nie wstrzykuje niczego, więc nie ma kontaktu z anticheatami.
/// Programy spoza listy (przeglądarka, edytor itp.) nigdy nie są pokazywane.
/// </summary>
public static class GameDetector
{
    // nazwa procesu (bez .exe, wielkość liter bez znaczenia) -> nazwa wyświetlana. Kolejność = priorytet, gdy działa kilka gier.
    static readonly (string Process, string Name)[] Known =
    {
        ("cs2", "Counter-Strike 2"), ("csgo", "Counter-Strike: Global Offensive"),
        ("VALORANT-Win64-Shipping", "VALORANT"), ("League of Legends", "League of Legends"),
        ("dota2", "Dota 2"), ("r5apex", "Apex Legends"), ("r5apex_dx12", "Apex Legends"),
        ("FortniteClient-Win64-Shipping", "Fortnite"), ("RocketLeague", "Rocket League"),
        ("Overwatch", "Overwatch 2"), ("RainbowSix", "Rainbow Six Siege"), ("RainbowSix_Vulkan", "Rainbow Six Siege"),
        ("TslGame", "PUBG: BATTLEGROUNDS"), ("cod", "Call of Duty"), ("ModernWarfare", "Call of Duty: Modern Warfare"),
        ("BlackOpsColdWar", "Call of Duty: Black Ops Cold War"), ("Marvel-Win64-Shipping", "Marvel Rivals"),
        ("project8", "Deadlock"), ("left4dead2", "Left 4 Dead 2"), ("tf_win64", "Team Fortress 2"), ("gmod", "Garry's Mod"),
        ("GTA5", "Grand Theft Auto V"), ("GTA5_Enhanced", "Grand Theft Auto V"), ("RDR2", "Red Dead Redemption 2"),
        ("eldenring", "Elden Ring"), ("Cyberpunk2077", "Cyberpunk 2077"), ("witcher3", "The Witcher 3"),
        ("bg3", "Baldur's Gate 3"), ("bg3_dx11", "Baldur's Gate 3"), ("b1-Win64-Shipping", "Black Myth: Wukong"),
        ("MonsterHunterWilds", "Monster Hunter Wilds"), ("HogwartsLegacy", "Hogwarts Legacy"), ("Starfield", "Starfield"),
        ("SkyrimSE", "Skyrim"), ("Fallout4", "Fallout 4"), ("Wow", "World of Warcraft"), ("WowClassic", "World of Warcraft Classic"),
        ("ffxiv_dx11", "Final Fantasy XIV"), ("Diablo IV", "Diablo IV"), ("Hearthstone", "Hearthstone"),
        ("PathOfExile", "Path of Exile"), ("PathOfExile_x64", "Path of Exile"), ("destiny2", "Destiny 2"), ("Warframe.x64", "Warframe"),
        ("EscapeFromTarkov", "Escape from Tarkov"), ("RustClient", "Rust"), ("DayZ_x64", "DayZ"), ("SquadGame", "Squad"),
        ("HuntGame", "Hunt: Showdown"), ("ShooterGame", "ARK: Survival"), ("DeadByDaylight-Win64-Shipping", "Dead by Daylight"),
        ("Palworld-Win64-Shipping", "Palworld"), ("FactoryGame-Win64-Shipping", "Satisfactory"), ("valheim", "Valheim"),
        ("Helldivers2", "Helldivers 2"), ("LethalCompany", "Lethal Company"), ("Phasmophobia", "Phasmophobia"),
        ("SoTGame", "Sea of Thieves"), ("FallGuys_client_game", "Fall Guys"), ("Among Us", "Among Us"),
        ("Terraria", "Terraria"), ("Stardew Valley", "Stardew Valley"), ("Minecraft.Windows", "Minecraft"),
        ("RobloxPlayerBeta", "Roblox"), ("GenshinImpact", "Genshin Impact"), ("FC25", "EA SPORTS FC 25"), ("FC24", "EA SPORTS FC 24"),
        ("ForzaHorizon5", "Forza Horizon 5"), ("eurotrucks2", "Euro Truck Simulator 2"), ("acs", "Assetto Corsa"),
    };

    static readonly Dictionary<string, (string Name, int Prio)> Map = Build();
    static Dictionary<string, (string, int)> Build()
    {
        var d = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Known.Length; i++) d.TryAdd(Known[i].Process, (Known[i].Name, i));
        return d;
    }

    /// <summary>Nazwa uruchomionej gry z listy albo pusty tekst. Wywoływać poza wątkiem UI (wylicza procesy).</summary>
    public static string Detect()
    {
        string best = ""; int bestPrio = int.MaxValue;
        Process[] all;
        try { all = Process.GetProcesses(); } catch { return ""; }
        foreach (var p in all)
        {
            try
            {
                if (Map.TryGetValue(p.ProcessName, out var g) && g.Prio < bestPrio) { best = g.Name; bestPrio = g.Prio; }
            }
            catch { /* proces zakończył się w trakcie */ }
            finally { p.Dispose(); }
        }
        return best;
    }
}
