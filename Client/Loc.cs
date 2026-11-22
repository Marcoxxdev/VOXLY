using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace VoiceClient;

/// <summary>Tłumaczenia interfejsu (PL / EN). Zmiana języka odświeża napisy w XAML-u od razu (bez restartu).</summary>
public sealed class Loc : Notify
{
    public static readonly Loc I = new();

    public event Action? LanguageChanged;

    string _lang = "pl";
    public string Lang => _lang;

    /// <summary>Język systemu: polski albo angielski (dla pozostałych).</summary>
    public static string Detect() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pl" ? "pl" : "en";

    public void Set(string lang)
    {
        lang = lang == "en" ? "en" : "pl";
        if (lang == _lang) return;
        _lang = lang;
        Raise("Item[]");          // odświeża wszystkie bindowania {local:Tr ...}
        LanguageChanged?.Invoke();
    }

    /// <summary>Indeksator używany przez {local:Tr klucz} w XAML-u.</summary>
    public string this[string key] => Get(key);

    public string Get(string key)
    {
        var d = _lang == "en" ? En : Pl;
        if (d.TryGetValue(key, out var v)) return v;
        return Pl.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string T(string key) => I.Get(key);
    public static string T(string key, params object[] args) => string.Format(I.Get(key), args);

    /// <summary>Tłumaczy kod błędu z serwera (Msg.Error / Msg.LoginFailed) na aktualny język.</summary>
    public static string ServerError(string code, string arg = "")
    {
        var key = "e_" + code;
        if (!I.Pl.ContainsKey(key)) return arg.Length > 0 ? code + ": " + arg : code;
        return string.Format(I.Get(key), arg);
    }

    Dictionary<string, string> Pl { get; } = new()
    {
        // nawigacja i strony
        ["nav_channels"] = "Kanały",
        ["nav_chat"] = "Wiadomości",
        ["nav_users"] = "Użytkownicy",
        ["nav_settings"] = "Ustawienia",
        ["connected"] = "Połączono",
        ["add_channel"] = "+ Dodaj kanał",
        ["voice_channels"] = "Kanały Głosu",
        ["chat_hint"] = "Widzą je osoby z kanału, w którym jesteś.",
        ["send"] = "Wyślij",
        ["users_title"] = "Użytkownicy na serwerze",
        ["settings_title"] = "Ustawienia",
        ["language"] = "Język / Language",
        ["auto_reconnect"] = "Automatycznie łącz ponownie po utracie połączenia",
        ["avatar"] = "Awatar",
        ["avatar_hint"] = "Widzą go wszyscy na serwerze. Zmiana działa od razu.",
        ["avatar_none"] = "Bez awatara (litera z nicku)",
        ["mic"] = "Mikrofon",
        ["speakers"] = "Głośniki / słuchawki",
        ["tx_mode"] = "Tryb nadawania",
        ["mode_vad"] = "Wykrywanie głosu (mikrofon włącza się, gdy mówisz)",
        ["mode_ptt"] = "Push-to-talk (nadajesz, dopóki trzymasz klawisz lub przycisk myszy)",
        ["ptt_key"] = "Klawisz: {0}",
        ["ptt_hint"] = "Kliknij i naciśnij klawisz albo środkowy / boczny przycisk myszy.",
        ["ptt_press"] = "Naciśnij klawisz lub przycisk myszy... (Esc = anuluj)",
        ["key_mid"] = "środkowy przycisk myszy",
        ["key_x1"] = "mysz: przycisk boczny 1 (wstecz)",
        ["key_x2"] = "mysz: przycisk boczny 2 (dalej)",
        ["sensitivity"] = "Czułość wykrywania głosu (poziom mikrofonu widać po połączeniu)",
        ["volume"] = "Głośność wyjścia",
        ["admin_title"] = "Administrator",
        ["admin_desc"] = "Po zalogowaniu hasłem admina zarządzasz kanałami, rangami i serwerem.",
        ["login"] = "Zaloguj",
        ["admin_panel"] = "Panel administratora",
        ["chk_srvpwd"] = "Wymagaj hasła do wejścia na serwer",
        ["srvpwd_hint"] = "Hasło serwera (zostaw puste, aby nie zmieniać obecnego). Zapisuje się w config.json na serwerze.",
        ["save"] = "Zapisz",
        ["admin_tips"] = "Rangi: kliknij użytkownika prawym przyciskiem myszy. Kolejność kanałów: przeciągnij kanał myszą.",
        ["footer"] = "VOXLY 1.4 · stworzone przez Marcoxxdev",
        ["latency"] = "Opóźnienie:",
        ["encryption"] = "Szyfrowanie:",
        ["mute_mic"] = "Wycisz mikrofon",
        ["unmute_mic"] = "Włącz mikrofon",
        ["mute_sound"] = "Wycisz dźwięk",
        ["unmute_sound"] = "Włącz dźwięk",
        ["disconnect"] = "Rozłącz",
        ["default_device"] = "Domyślne urządzenie",
        // ekran łączenia
        ["tagline"] = "Prosty komunikator głosowy · by Marcoxxdev",
        ["host_label"] = "Adres serwera (np. 203.0.113.5 lub 203.0.113.5:9050)",
        ["nick_label"] = "Twój nick",
        ["pwd_label"] = "Hasło serwera (tylko jeśli serwer je wymaga)",
        ["remember"] = "Zapamiętaj dane",
        ["connect"] = "Połącz",
        ["connecting"] = "Łączenie...",
        ["fill_host_nick"] = "Uzupełnij adres serwera i nick.",
        ["error_prefix"] = "Błąd:",
        // ponowne łączenie
        ["reconnect_title"] = "Utracono połączenie",
        ["reconnecting"] = "Łączenie ponownie... (próba {0})",
        ["reconnect_in"] = "Ponowna próba za {0} s (próba {1})",
        ["cancel"] = "Anuluj",
        // powody rozłączenia
        ["r_connfailed"] = "Nie można połączyć się z serwerem. Sprawdź adres i port UDP.",
        ["r_rejected"] = "Serwer odrzucił połączenie: brak wolnych miejsc albo niezgodna wersja programu.",
        ["r_timeout"] = "Utracono połączenie z serwerem (przekroczony czas).",
        ["r_remoteclose"] = "Serwer zakończył połączenie.",
        ["r_other"] = "Rozłączono ({0}).",
        // administrator
        ["admin_bad"] = "Nieprawidłowe hasło administratora.",
        ["admin_saved"] = "Ustawienia hasła serwera zapisane.",
        ["admin_ok"] = "Zalogowano jako administrator.",
        ["hint_admin"] = "Przeciągnij kanał, aby zmienić kolejność. Prawy przycisk: edycja, usuwanie, rangi.",
        ["hint_ca"] = "Prawy przycisk na swoim kanale: edycja i hasło.",
        // menu i okna
        ["ch_pwd_title"] = "Kanał chroniony hasłem",
        ["ch_pwd_prompt"] = "Podaj hasło do kanału „{0}”:",
        ["m_edit"] = "Edytuj kanał (nazwa, kolor, hasło)",
        ["m_edit_title"] = "Edytuj kanał",
        ["m_delete"] = "Usuń kanał",
        ["m_delete_q"] = "Usunąć kanał „{0}”? Osoby z kanału trafią do Lobby.",
        ["m_new_title"] = "Nowy kanał",
        ["m_to_lobby"] = "Przenieś do Lobby",
        ["m_rank_admin"] = "Ranga: Admin",
        ["m_rank_ca"] = "Ranga: Channel Admin (kanał „{0}”)",
        ["m_rank_user"] = "Ranga: User",
        ["channel_fallback"] = "kanał",
        ["d_name"] = "Nazwa kanału",
        ["d_color"] = "Kolor (najedź na kolor, aby zobaczyć podgląd)",
        ["d_custom"] = "Własny kolor (HEX, np. #FF8800)",
        ["d_chk_pwd"] = "Chroń kanał hasłem",
        ["d_hint_keep"] = "Zostaw puste, aby zachować obecne hasło.",
        ["d_hint_new"] = "Wpisz hasło, które będą musieli podać wchodzący.",
        ["ok"] = "OK",
        ["app_error_title"] = "VOXLY - błąd",
        // kody błędów z serwera i silnika audio
        ["e_ServerPasswordRequired"] = "Serwer wymaga hasła.",
        ["e_ServerPasswordWrong"] = "Nieprawidłowe hasło serwera.",
        ["e_WrongChannelPassword"] = "Nieprawidłowe hasło kanału.",
        ["e_AdminNotConfigured"] = "Administrator nie ustawił hasła admina w config.json.",
        ["e_InvalidNameOrColor"] = "Nieprawidłowa nazwa lub kolor.",
        ["e_ChannelLimit"] = "Osiągnięto limit kanałów.",
        ["e_LobbyProtected"] = "Kanału głównego (Lobby) nie można usunąć.",
        ["e_InvalidChannelData"] = "Nieprawidłowe dane kanału.",
        ["e_NoPermission"] = "Brak uprawnień.",
        ["e_EnterServerPassword"] = "Podaj hasło serwera.",
        ["e_MovedToLobby"] = "{0} przeniósł Cię do Lobby.",
        ["e_MicFail"] = "Nie można uruchomić mikrofonu: {0}",
        ["e_SpkFail"] = "Nie można uruchomić głośników: {0}",
        // klienci serwera (online / offline)
        ["cl_nav"] = "Klienci",
        ["cl_title"] = "Klienci serwera",
        ["cl_stats"] = "Online: {0}  ·  Offline: {1}  ·  Zbanowani: {2}",
        ["cl_search"] = "Szukaj po nicku",
        ["cl_f_all"] = "Wszyscy",
        ["cl_f_online"] = "Online",
        ["cl_f_offline"] = "Offline",
        ["cl_f_banned"] = "Zbanowani",
        ["cl_online"] = "Online",
        ["cl_offline"] = "Offline",
        ["cl_banned"] = "Zbanowany",
        ["cl_you"] = "TY",
        ["cl_in_channel"] = "w kanale „{0}”",
        ["cl_last_seen"] = "ostatnio widziany: {0}",
        ["ago_now"] = "przed chwilą",
        ["ago_min"] = "{0} min temu",
        ["ago_hour"] = "{0} godz. temu",
        ["ago_day"] = "{0} dni temu",
        ["cd_title"] = "Zarządzanie klientem",
        ["cd_first"] = "Pierwsze połączenie: {0}",
        ["cd_last"] = "Ostatnio widziany: {0}",
        ["cd_kick"] = "Rozłącz z serwera",
        ["cd_ban"] = "Zbanuj",
        ["cd_unban"] = "Odbanuj",
        ["cd_forget"] = "Usuń z listy",
        ["cd_ban_q"] = "Zbanować „{0}”? Zostanie rozłączony i nie wejdzie na serwer, dopóki go nie odbanujesz.",
        ["cd_forget_q"] = "Usunąć „{0}” z listy? Straci też zapisaną rangę.",
        ["cd_ban_note"] = "Ranga zapisana dla klienta offline zacznie obowiązywać przy jego następnym wejściu. Ban dotyczy identyfikatora klienta; reinstalacja programu lub usunięcie settings.json nadaje nowy identyfikator.",
        ["e_Banned"] = "Zostałeś zbanowany na tym serwerze.",
        ["e_KickedFromServer"] = "Administrator {0} rozłączył Cię z serwerem.",
        ["e_CannotBanAdmin"] = "Nie można zbanować administratora. Najpierw zmień jego rangę.",
        ["e_ClientOnline"] = "Ten klient jest teraz online. Najpierw go rozłącz.",
        ["e_SelfAction"] = "Nie możesz wykonać tej akcji na sobie.",
        // głośność użytkowników
        ["vol_tip"] = "Głośność tego użytkownika (tylko u Ciebie)",
        ["vol_mute"] = "Wycisz tego użytkownika",
        ["vol_reset"] = "Przywróć 100%",
        ["vol_muted"] = "wyciszony",
        ["vol_hint"] = "Dotyczy tylko Ciebie i jest zapamiętywane dla danego nicku. Powyżej 100% dźwięk jest wzmacniany.",
        ["m_volume"] = "Głośność użytkownika...",
        ["m_manage"] = "Zarządzaj...",
        // aktualizacje
        ["upd_title"] = "Aktualizacje",
        ["upd_current"] = "Wersja programu: {0}",
        ["upd_auto"] = "Sprawdzaj aktualizacje przy uruchomieniu",
        ["upd_check"] = "Sprawdź aktualizacje",
        ["upd_checking"] = "Sprawdzanie...",
        ["upd_latest"] = "Masz najnowszą wersję ({0}).",
        ["upd_available"] = "Dostępna nowa wersja: {0}.",
        ["upd_banner"] = "Dostępna aktualizacja VOXLY {0} (masz {1}).",
        ["upd_now"] = "Aktualizuj teraz",
        ["upd_later"] = "Później",
        ["upd_downloading"] = "Pobieranie aktualizacji... {0}%",
        ["upd_installing"] = "Uruchamianie instalatora. Program zamknie się i uruchomi ponownie w nowej wersji...",
        ["upd_error"] = "Aktualizacja nie powiodła się: {0}",
        ["upd_norepo"] = "Nie ustawiono repozytorium GitHub (GitHubRepo w Client/VoiceClient.csproj).",
        ["upd_404"] = "Nie znaleziono wydań w repozytorium {0} (brak wydania albo repozytorium jest prywatne).",
        ["upd_noasset"] = "Wydanie nie zawiera pliku {0}.",
        ["upd_nohash"] = "Wydanie nie zawiera sumy kontrolnej SHA-256 instalatora (plik .sha256).",
        ["upd_badhash"] = "Suma kontrolna pobranego pliku się nie zgadza. Aktualizacja przerwana.",
        // test mikrofonu
        ["loop_start"] = "Test mikrofonu (słyszę siebie)",
        ["loop_stop"] = "Zakończ test mikrofonu",
        ["loop_hint"] = "Słyszysz siebie tak, jak usłyszą Cię inni (po kompresji). Działa według wybranego trybu: wykrywanie głosu albo push-to-talk. Użyj słuchawek, żeby uniknąć sprzężenia. Test wyłącza się po rozłączeniu.",
        // dostęp do kanału według rang
        ["d_access"] = "Kto może wejść do kanału",
        ["d_access_admin"] = "Admin (zawsze ma dostęp)",
        ["d_access_hint"] = "Channel Admin zawsze wchodzi do swojego kanału. Lobby jest otwarte dla wszystkich.",
        ["e_RankRequired"] = "Twoja ranga nie ma dostępu do tego kanału.",
        ["e_AccessRevoked"] = "Zmiana dostępu: Twoja ranga nie ma już wstępu do kanału „{0}”. Przeniesiono Cię do Lobby.",
        ["e_OwnRank"] = "Nie możesz zmienić własnej rangi.",
        // zarządzanie użytkownikiem
        ["user_manage"] = "Zarządzaj użytkownikiem (ranga, przeniesienie do Lobby)",
        ["user_manage_short"] = "Zarządzaj",
        ["ud_title"] = "Zarządzanie użytkownikiem",
        ["ud_rank"] = "Ranga",
        ["ud_current"] = "(obecna)",
        ["ud_user_desc"] = "Zwykły użytkownik: wchodzi do kanałów, rozmawia i pisze.",
        ["ud_ca_desc"] = "Zarządza wybranym kanałem: edycja, hasło, przenoszenie osób do Lobby.",
        ["ud_ca_channel"] = "Kanał:",
        ["ud_admin_desc"] = "Pełne uprawnienia: kanały, rangi i hasło serwera.",
        ["ud_save"] = "Zapisz rangę",
        ["ud_actions"] = "Akcje",
        ["ud_close"] = "Zamknij",
    };

    Dictionary<string, string> En { get; } = new()
    {
        ["nav_channels"] = "Channels",
        ["nav_chat"] = "Messages",
        ["nav_users"] = "Users",
        ["nav_settings"] = "Settings",
        ["connected"] = "Connected",
        ["add_channel"] = "+ Add channel",
        ["voice_channels"] = "Voice channels",
        ["chat_hint"] = "Visible to people in the channel you are in.",
        ["send"] = "Send",
        ["users_title"] = "Users on the server",
        ["settings_title"] = "Settings",
        ["language"] = "Język / Language",
        ["auto_reconnect"] = "Reconnect automatically when the connection is lost",
        ["avatar"] = "Avatar",
        ["avatar_hint"] = "Everyone on the server can see it. Changes apply immediately.",
        ["avatar_none"] = "No avatar (first letter of your nickname)",
        ["mic"] = "Microphone",
        ["speakers"] = "Speakers / headphones",
        ["tx_mode"] = "Transmit mode",
        ["mode_vad"] = "Voice activation (the microphone turns on when you speak)",
        ["mode_ptt"] = "Push-to-talk (you transmit while holding the key or mouse button)",
        ["ptt_key"] = "Key: {0}",
        ["ptt_hint"] = "Click, then press a key or the middle / side mouse button.",
        ["ptt_press"] = "Press a key or mouse button... (Esc = cancel)",
        ["key_mid"] = "middle mouse button",
        ["key_x1"] = "mouse: side button 1 (back)",
        ["key_x2"] = "mouse: side button 2 (forward)",
        ["sensitivity"] = "Voice detection sensitivity (the mic level is shown once connected)",
        ["volume"] = "Output volume",
        ["admin_title"] = "Administrator",
        ["admin_desc"] = "After logging in with the admin password you can manage channels, ranks and the server.",
        ["login"] = "Log in",
        ["admin_panel"] = "Admin panel",
        ["chk_srvpwd"] = "Require a password to join the server",
        ["srvpwd_hint"] = "Server password (leave empty to keep the current one). Stored in config.json on the server.",
        ["save"] = "Save",
        ["admin_tips"] = "Ranks: right-click a user. Channel order: drag a channel with the mouse.",
        ["footer"] = "VOXLY 1.4 · created by Marcoxxdev",
        ["latency"] = "Latency:",
        ["encryption"] = "Encryption:",
        ["mute_mic"] = "Mute microphone",
        ["unmute_mic"] = "Unmute microphone",
        ["mute_sound"] = "Mute sound",
        ["unmute_sound"] = "Unmute sound",
        ["disconnect"] = "Disconnect",
        ["default_device"] = "Default device",
        ["tagline"] = "Simple voice chat · by Marcoxxdev",
        ["host_label"] = "Server address (e.g. 203.0.113.5 or 203.0.113.5:9050)",
        ["nick_label"] = "Your nickname",
        ["pwd_label"] = "Server password (only if the server requires one)",
        ["remember"] = "Remember my details",
        ["connect"] = "Connect",
        ["connecting"] = "Connecting...",
        ["fill_host_nick"] = "Enter the server address and a nickname.",
        ["error_prefix"] = "Error:",
        ["reconnect_title"] = "Connection lost",
        ["reconnecting"] = "Reconnecting... (attempt {0})",
        ["reconnect_in"] = "Retrying in {0} s (attempt {1})",
        ["cancel"] = "Cancel",
        ["r_connfailed"] = "Cannot connect to the server. Check the address and the UDP port.",
        ["r_rejected"] = "The server rejected the connection: no free slots or a program version mismatch.",
        ["r_timeout"] = "Connection to the server lost (timeout).",
        ["r_remoteclose"] = "The server closed the connection.",
        ["r_other"] = "Disconnected ({0}).",
        ["admin_bad"] = "Wrong administrator password.",
        ["admin_saved"] = "Server password settings saved.",
        ["admin_ok"] = "Logged in as administrator.",
        ["hint_admin"] = "Drag a channel to reorder. Right-click: edit, delete, ranks.",
        ["hint_ca"] = "Right-click your channel: edit and password.",
        ["ch_pwd_title"] = "Password-protected channel",
        ["ch_pwd_prompt"] = "Enter the password for channel “{0}”:",
        ["m_edit"] = "Edit channel (name, colour, password)",
        ["m_edit_title"] = "Edit channel",
        ["m_delete"] = "Delete channel",
        ["m_delete_q"] = "Delete channel “{0}”? People in it will be moved to the Lobby.",
        ["m_new_title"] = "New channel",
        ["m_to_lobby"] = "Move to Lobby",
        ["m_rank_admin"] = "Rank: Admin",
        ["m_rank_ca"] = "Rank: Channel Admin (channel “{0}”)",
        ["m_rank_user"] = "Rank: User",
        ["channel_fallback"] = "channel",
        ["d_name"] = "Channel name",
        ["d_color"] = "Colour (hover over a colour to preview it)",
        ["d_custom"] = "Custom colour (HEX, e.g. #FF8800)",
        ["d_chk_pwd"] = "Protect the channel with a password",
        ["d_hint_keep"] = "Leave empty to keep the current password.",
        ["d_hint_new"] = "Enter the password people will have to give to join.",
        ["ok"] = "OK",
        ["app_error_title"] = "VOXLY - error",
        ["e_ServerPasswordRequired"] = "The server requires a password.",
        ["e_ServerPasswordWrong"] = "Wrong server password.",
        ["e_WrongChannelPassword"] = "Wrong channel password.",
        ["e_AdminNotConfigured"] = "The administrator has not set the admin password in config.json.",
        ["e_InvalidNameOrColor"] = "Invalid name or colour.",
        ["e_ChannelLimit"] = "Channel limit reached.",
        ["e_LobbyProtected"] = "The main channel (Lobby) cannot be deleted.",
        ["e_InvalidChannelData"] = "Invalid channel data.",
        ["e_NoPermission"] = "Permission denied.",
        ["e_EnterServerPassword"] = "Enter the server password.",
        ["e_MovedToLobby"] = "{0} moved you to the Lobby.",
        ["e_MicFail"] = "Cannot start the microphone: {0}",
        ["e_SpkFail"] = "Cannot start the speakers: {0}",
        // server clients (online / offline)
        ["cl_nav"] = "Clients",
        ["cl_title"] = "Server clients",
        ["cl_stats"] = "Online: {0}  ·  Offline: {1}  ·  Banned: {2}",
        ["cl_search"] = "Search by nickname",
        ["cl_f_all"] = "Everyone",
        ["cl_f_online"] = "Online",
        ["cl_f_offline"] = "Offline",
        ["cl_f_banned"] = "Banned",
        ["cl_online"] = "Online",
        ["cl_offline"] = "Offline",
        ["cl_banned"] = "Banned",
        ["cl_you"] = "YOU",
        ["cl_in_channel"] = "in channel “{0}”",
        ["cl_last_seen"] = "last seen: {0}",
        ["ago_now"] = "just now",
        ["ago_min"] = "{0} min ago",
        ["ago_hour"] = "{0} h ago",
        ["ago_day"] = "{0} days ago",
        ["cd_title"] = "Manage client",
        ["cd_first"] = "First connected: {0}",
        ["cd_last"] = "Last seen: {0}",
        ["cd_kick"] = "Disconnect from server",
        ["cd_ban"] = "Ban",
        ["cd_unban"] = "Unban",
        ["cd_forget"] = "Remove from list",
        ["cd_ban_q"] = "Ban “{0}”? They will be disconnected and cannot join the server until you unban them.",
        ["cd_forget_q"] = "Remove “{0}” from the list? Their saved rank is removed too.",
        ["cd_ban_note"] = "A rank saved for an offline client applies the next time they join. A ban applies to the client identifier; reinstalling the program or deleting settings.json creates a new identifier.",
        ["e_Banned"] = "You have been banned from this server.",
        ["e_KickedFromServer"] = "Administrator {0} disconnected you from the server.",
        ["e_CannotBanAdmin"] = "An administrator cannot be banned. Change their rank first.",
        ["e_ClientOnline"] = "This client is online right now. Disconnect them first.",
        ["e_SelfAction"] = "You cannot do this to yourself.",
        // per-user volume
        ["vol_tip"] = "Volume of this user (only for you)",
        ["vol_mute"] = "Mute this user",
        ["vol_reset"] = "Reset to 100%",
        ["vol_muted"] = "muted",
        ["vol_hint"] = "Only affects you and is remembered for this nickname. Above 100% the sound is amplified.",
        ["m_volume"] = "User volume...",
        ["m_manage"] = "Manage...",
        // updates
        ["upd_title"] = "Updates",
        ["upd_current"] = "Program version: {0}",
        ["upd_auto"] = "Check for updates on startup",
        ["upd_check"] = "Check for updates",
        ["upd_checking"] = "Checking...",
        ["upd_latest"] = "You have the latest version ({0}).",
        ["upd_available"] = "A new version is available: {0}.",
        ["upd_banner"] = "VOXLY {0} is available (you have {1}).",
        ["upd_now"] = "Update now",
        ["upd_later"] = "Later",
        ["upd_downloading"] = "Downloading update... {0}%",
        ["upd_installing"] = "Starting the installer. The program will close and restart in the new version...",
        ["upd_error"] = "Update failed: {0}",
        ["upd_norepo"] = "No GitHub repository is set (GitHubRepo in Client/VoiceClient.csproj).",
        ["upd_404"] = "No releases found in repository {0} (no release yet, or the repository is private).",
        ["upd_noasset"] = "The release does not contain the file {0}.",
        ["upd_nohash"] = "The release has no SHA-256 checksum for the installer (.sha256 file).",
        ["upd_badhash"] = "The checksum of the downloaded file does not match. Update aborted.",
        // microphone test
        ["loop_start"] = "Microphone test (hear yourself)",
        ["loop_stop"] = "Stop microphone test",
        ["loop_hint"] = "You hear yourself the way others will hear you (after compression). It follows the selected mode: voice activation or push-to-talk. Use headphones to avoid feedback. The test stops when you disconnect.",
        // channel access by rank
        ["d_access"] = "Who can enter this channel",
        ["d_access_admin"] = "Admin (always has access)",
        ["d_access_hint"] = "A Channel Admin can always enter their own channel. The Lobby is open to everyone.",
        ["e_RankRequired"] = "Your rank does not have access to this channel.",
        ["e_AccessRevoked"] = "Access changed: your rank can no longer enter the channel “{0}”. You were moved to the Lobby.",
        ["e_OwnRank"] = "You cannot change your own rank.",
        // user management
        ["user_manage"] = "Manage user (rank, move to the Lobby)",
        ["user_manage_short"] = "Manage",
        ["ud_title"] = "Manage user",
        ["ud_rank"] = "Rank",
        ["ud_current"] = "(current)",
        ["ud_user_desc"] = "Regular user: joins channels, talks and chats.",
        ["ud_ca_desc"] = "Manages the chosen channel: edit, password, move people to the Lobby.",
        ["ud_ca_channel"] = "Channel:",
        ["ud_admin_desc"] = "Full control: channels, ranks and the server password.",
        ["ud_save"] = "Save rank",
        ["ud_actions"] = "Actions",
        ["ud_close"] = "Close",
    };
}

/// <summary>Użycie w XAML: Text="{local:Tr nav_channels}" - napis zmienia się razem z językiem.</summary>
[MarkupExtensionReturnType(typeof(object))]
public class TrExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding("[" + Key + "]") { Source = Loc.I, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
