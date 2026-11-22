# VOXLY

Prosty komunikator głosowy w stylu TeamSpeaka: **klient na Windows**, **serwer na Linuksie**, do 30 osób.
Autor: **Marcoxxdev**.

```
 Klient (Windows, WPF)  <--- UDP 9050, całość szyfrowana AES-256-GCM --->  Serwer (Linux, .NET)
```

## Funkcje

- **Kanały głosowe** z kolorowymi oznaczeniami: paleta 24 kolorów z podglądem po najechaniu oraz dowolny kolor HEX.
- **Rangi:** `Admin`, `Channel Admin`, `User` (opis niżej), z przejrzystym oknem **Zarządzaj** przy każdym użytkowniku.
- **Dostęp do kanałów według rang:** Admin wybiera, które rangi mogą wejść do kanału (np. kanał tylko dla Channel Adminów i Adminów).
- **Głośność poszczególnych użytkowników** (0-200%, wyciszanie), jak w TeamSpeaku.
- **Aktualizacje z poziomu programu** przez GitHub Releases (opis niżej).
- **Wiele serwerów** na ekranie logowania: lista zapisanych serwerów z własnymi nazwami.
- **Unikalne nicki:** serwer odrzuca nick zajęty przez kogoś innego (online lub offline).
- **Kolor kanału** wybierany jednym paskiem barw z suwakiem, paskiem jasności i polem HEX.
- **Rozmiar listy kanałów** (Ustawienia lub Ctrl + kółko myszy) i własna nazwa listy kanałów ustawiana przez Admina.
- **Klienci online i offline:** Admin widzi wszystkich, którzy kiedykolwiek weszli na serwer, i zarządza nimi (strona *Klienci*).
- **Test mikrofonu:** słyszysz siebie tak, jak usłyszą Cię inni (*Ustawienia → Test mikrofonu*).
- **Hasła kanałów** (kłódka przy kanale) i **opcjonalne hasło serwera**.
- **Przeciąganie kanałów myszą** (kolejność zapisuje się na serwerze).
- Zielona obwódka przy osobie, która mówi, czat tekstowy kanału, lista użytkowników.
- **Push-to-talk** pod klawiszem **lub przyciskiem myszy** (środkowy, boczne), działa też, gdy okno jest w tle. Alternatywnie wykrywanie głosu.
- Wybór mikrofonu i głośników, głośność, wyciszenie mikrofonu / dźwięku, pomiar opóźnienia.
- **Własna ikona** programu, instalatora i okna klienta (`Client/Assets/voxly.ico`, `voxly.png`; żeby ją zmienić, podmień oba pliki i zbuduj ponownie).
- **50 awatarów do wyboru** (*Ustawienia → Awatar*), widocznych dla wszystkich na serwerze; zmiana działa od razu.
- **Interfejs po polsku i po angielsku** (wybór na ekranie logowania i w *Ustawieniach*, zmiana działa od razu; domyślnie język systemu).
- **Automatyczne łączenie po utracie połączenia** (np. po restarcie serwera): klient ponawia próby, aż się uda, albo do kliknięcia „Anuluj". Można to wyłączyć w *Ustawieniach*.

## Rangi i uprawnienia

| | User | Channel Admin | Admin |
|---|:-:|:-:|:-:|
| Wejście do kanałów (z hasłem, jeśli ustawione), głos, czat | ✓ | ✓ | ✓ |
| Wejście do swojego kanału bez hasła | | ✓ | ✓ |
| Edycja swojego kanału (nazwa, kolor, hasło) | | ✓ | ✓ |
| Przenoszenie osób ze swojego kanału do Lobby | | ✓ | ✓ |
| Tworzenie, usuwanie i przesuwanie kanałów | | | ✓ |
| Nadawanie rang (przycisk **Zarządzaj** przy użytkowniku) | | | ✓ |
| Ustawianie, które rangi mogą wejść do kanału | | | ✓ |
| Hasło wejścia na serwer (Panel administratora) | | | ✓ |
| Lista klientów online i offline, bany, rozłączanie, usuwanie z listy | | | ✓ |

- **Rangi nadane przez Admina są stałe.** Serwer zapisuje je w `ranks.json` i przywraca przy każdym kolejnym połączeniu, także po restarcie serwera. Użytkownika rozpoznaje po losowym identyfikatorze komputera (`ClientId` w `%AppData%\Voxly\settings.json`), a nie po nicku, więc zmiana nicku nie zabiera rangi, a podszycie się pod cudzy nick nie daje jego rangi.
- **Admin:** nadaje go Admin (prawy przycisk → „Ranga: Admin") albo każdy, kto w *Ustawienia → Administrator* wpisze hasło z `config.json` serwera (`AdminPassword`). Hasło daje najwyższą rangę (Admin) **na stałe**: serwer zapisuje ją tak samo jak rangę nadaną ręcznie. Cofnięcie: prawy przycisk na użytkowniku → „Ranga: User\" albo usunięcie wpisu z `ranks.json`.
- **Channel Admin:** nadaje go Admin (prawy przycisk na użytkowniku → „Ranga: Channel Admin"). Dotyczy kanału, w którym użytkownik jest w chwili nadania. Po usunięciu kanału ranga znika.
- Ranga „User" odbiera zapisaną rangę. Reinstalacja Windows lub usunięcie `settings.json` tworzy nowy identyfikator, więc ranga nie wróci (trzeba ją nadać ponownie); skopiowanie `settings.json` na inny komputer przenosi tożsamość.

## Zarządzanie użytkownikami i dostęp do kanałów

- **Zarządzanie:** przy każdym użytkowniku, którym możesz zarządzać, widać przycisk **Zarządzaj** (na liście kanałów i na stronie *Użytkownicy*; prawy przycisk na użytkowniku też go otwiera). Okno pokazuje trzy rangi z opisami, zaznacza aktualną i podświetla wybraną. Dla Channel Admina wybierasz kanał z listy. Przycisk „Zapisz rangę" jest aktywny dopiero po zmianie. Nie zmienisz własnej rangi. Channel Admin widzi tylko akcję „Przenieś do Lobby" dla osób ze swojego kanału.
- **Dostęp do kanału:** *Dodaj kanał* lub prawy przycisk na kanale → *Kto może wejść do kanału* (User / Channel Admin; Admin zawsze ma dostęp). Kanał z ograniczeniem ma plakietkę z listą rang i jest przygaszony dla osób bez dostępu. Lobby jest zawsze otwarte. Channel Admin zawsze wchodzi do swojego kanału. Dostęp zmienia tylko Admin. Jeśli zmiana dostępu lub rangi odbiera komuś wstęp do kanału, w którym jest, serwer przenosi go do Lobby.
- Dostęp według rang działa razem z hasłem kanału: najpierw sprawdzana jest ranga, potem hasło.

## Klienci online i offline (Admin)

Serwer zapamiętuje każdego klienta (po identyfikatorze, który program nadaje sobie przy pierwszym uruchomieniu) w pliku `clients.json`. Admin widzi ich na stronie **Klienci** (przycisk w pasku bocznym pojawia się po zalogowaniu jako Admin).

- **Lista na żywo:** status (zielony = online, szary = offline, czerwony = zbanowany), kanał, w którym jest osoba online, albo „ostatnio widziany" dla offline. Online na górze. Licznik oraz wyszukiwanie po nicku i filtr (Wszyscy, Online, Offline, Zbanowani). Lista odświeża się sama, dopóki masz otwartą tę stronę.
- **Zarządzaj** przy kliencie otwiera okno:
  - **ranga** (User, Channel Admin, Admin), także dla klienta **offline**: obowiązuje od jego następnego wejścia i jest zapisana na stałe,
  - **Rozłącz z serwera** (tylko online): klient dostaje komunikat i nie łączy się automatycznie,
  - **Zbanuj / Odbanuj:** zbanowany klient jest rozłączany i odrzucany przy logowaniu, a ban zostaje po restarcie serwera,
  - **Usuń z listy** (tylko offline): kasuje wpis razem z zapisaną rangą.
- **Zabezpieczenia:** nie wykonasz akcji na samym sobie, a Admina trzeba najpierw zdegradować, żeby go zbanować. Wpisy bez rangi i bez bana, nieaktywne od 90 dni, są usuwane automatycznie.
- **Ograniczenie:** ban dotyczy identyfikatora klienta, więc reinstalacja programu lub usunięcie `%AppData%\Voxly\settings.json` nadaje nowy identyfikator i omija ban. To ochrona przed przypadkowymi gośćmi, nie przed zdeterminowanym użytkownikiem.
- Dotychczasowy plik `ranks.json` zostaje automatycznie zaimportowany do `clients.json` przy pierwszym uruchomieniu nowego serwera.

## Awatary

Do wyboru jest 19 awatarów w stylu kreskówkowym (*Ustawienia → Awatar*) oraz opcja „Aa" (litera z nicku). Obrazki leżą w `Client/Assets/Avatars/` i są wkompilowane w program. Nowy awatar widzą inni tylko wtedy, gdy serwer jest w tej samej wersji.
Uwaga prawna: dopilnuj, żeby grafiki w publicznym repozytorium były Twoje albo miały odpowiednią licencję. Postacie przypominające bohaterów znanych gier mogą być zgłaszane jako naruszenie praw autorskich.

## Test mikrofonu

*Ustawienia → Test mikrofonu (słyszę siebie)* odtwarza Twój głos po przejściu przez ten sam kodek, który słyszą inni. Test działa według wybranego trybu nadawania (wykrywanie głosu lub push-to-talk), więc sprawdzasz realne ustawienia. Użyj słuchawek, bo na głośnikach pojawi się sprzężenie. Test działa po połączeniu z serwerem i wyłącza się po rozłączeniu.

## Głośność poszczególnych użytkowników

Przy każdej innej osobie jest ikona głośnika (na liście kanałów i na stronie *Użytkownicy*), a prawy przycisk na użytkowniku → *Głośność użytkownika...* otwiera to samo okienko. Suwak ustawia głośność od 0 do 200% (powyżej 100% dźwięk jest wzmacniany, z ograniczeniem przesterowania), jest też „Wycisz tego użytkownika" i „Przywróć 100%". Zmiana działa od razu, dotyczy tylko Ciebie i jest zapamiętywana **dla danego nicku** w `settings.json`. Gdy głośność jest zmieniona, przy użytkowniku widać procent (albo „wyciszony").

## Aktualizacje z poziomu programu (GitHub)

Klient sprawdza przy starcie (można wyłączyć w *Ustawienia → Aktualizacje*) najnowsze wydanie w repozytorium GitHub. Jeśli jest nowsze od zainstalowanej wersji, na górze okna pojawia się baner **„Aktualizuj teraz"**. Program pobiera `Voxly-Setup.exe`, **sprawdza jego sumę SHA-256**, uruchamia instalator po cichu, zamyka się i po instalacji startuje sam w nowej wersji. Ręczne sprawdzenie: *Ustawienia → Sprawdź aktualizacje*.

### Jednorazowa konfiguracja

1. Załóż **publiczne** repozytorium na GitHubie (dla prywatnego klient nie ma jak pobrać wydania) i wgraj do niego zawartość tego folderu:
   ```
   git init
   git add .
   git commit -m "VOXLY"
   git branch -M main
   git remote add origin https://github.com/TWOJE_KONTO/TWOJE_REPO.git
   git push -u origin main
   ```
2. W `Client/VoiceClient.csproj` ustaw swoje repozytorium (domyślnie `Marcoxxdev/Voxly`):
   ```xml
   <AssemblyMetadata Include="GitHubRepo" Value="TWOJE_KONTO/TWOJE_REPO" />
   ```
3. Wydanie pierwszej wersji (to ją będą instalować użytkownicy, pobierając `Voxly-Setup.exe` z zakładki *Releases*).

### Wydawanie nowej wersji

```
git tag v1.3.1
git push origin v1.3.1
```

Workflow `.github/workflows/release.yml` sam zbuduje klienta i instalator (wersja z tagu), policzy sumę SHA-256 i utworzy wydanie z plikami `Voxly-Setup.exe`, `Voxly-Setup.exe.sha256` oraz instalatorem serwera Linux (`voxly-server-install.sh`, `Voxly-Server-linux.tar.gz`). Użytkownicy zobaczą baner przy następnym uruchomieniu. Numer wersji w tagu musi być wyższy od poprzedniego (`v1.3.1` > `v1.3.0`).

Uwagi:
- Aktualizowany jest **klient**. Serwer aktualizujesz ręcznie: nowym `voxly-server-install.sh` (konfiguracja i kanały zostają). Gdy zmienia się protokół (klucz `VOXLY-N` w `Shared/Protocol.cs`), zaktualizuj oba, bo inaczej serwer odrzuci stare klienty.
- Wydanie bez pliku `.sha256` (i bez pola `digest` GitHuba przy instalatorze) jest odrzucane przez klienta, żeby nie uruchamiać niezweryfikowanego pliku.
- Instalator nie jest podpisany certyfikatem, więc przy pierwszej instalacji SmartScreen może pokazać ostrzeżenie. Aktualizacje z poziomu programu uruchamiają instalator po cichu.
- Limit GitHub API bez logowania to 60 zapytań na godzinę z jednego adresu IP, co w zupełności wystarcza.

## Instalacja

### Serwer (Linux, np. VPS z Ubuntu/Debian)

```
tar xzf Voxly-Server-linux.tar.gz
cd voxly-server
sudo bash voxly-server-install.sh
```

Skrypt sam doinstaluje .NET, zbuduje serwer, zainstaluje usługę systemd `voxly`, wygeneruje hasło admina i otworzy port UDP 9050 w `ufw`/`firewalld`. Otwórz ten port także u dostawcy VPS lub na routerze. Szczegóły: `Installer/linux/INSTALACJA.md`.

**Hasło wejścia na serwer** jest domyślnie wyłączone. Admin włącza je w kliencie: *Ustawienia → Panel administratora → „Wymagaj hasła do wejścia na serwer" → hasło → Zapisz*. Hasło zapisuje się w `/opt/voxly/config.json`. Gdy jest włączone, klient musi je wpisać na ekranie logowania.

### Klient (Windows 10/11)

Uruchom **`Voxly-Setup.exe`**. Środowisko .NET jest wbudowane w program, a instalator sam pobierze i zainstaluje Microsoft Visual C++ Runtime, jeśli go brakuje. Potem: adres serwera (np. `203.0.113.5` lub `203.0.113.5:9050`), nick, hasło (tylko jeśli serwer je wymaga).

## Budowanie ze źródeł (Windows)

Uruchom `build-all.bat`. Skrypt **sam zainstaluje przez winget** .NET 8 SDK i Inno Setup, jeśli ich brakuje (przy pierwszym razie może poprosić o ponowne uruchomienie). Wynik w `dist\`:

- `Voxly-Setup.exe`: instalator klienta,
- `dist\client\Voxly.exe`: klient bez instalatora,
- `voxly-server-install.sh` i `Voxly-Server-linux.tar.gz`: instalator serwera (wymaga `bash`, czyli WSL lub Git Bash; bez niego uruchom `./make-linux-installer.sh` na Linuksie).

Po każdej zmianie w `Server/` lub `Shared/` wygeneruj instalator serwera ponownie, bo zawiera kopię źródeł.

Struktura: `Client/` (WPF), `Server/` (konsola .NET), `Shared/Protocol.cs` (wspólny protokół i kryptografia), `Installer/` (Inno Setup + Linux).

## Bezpieczeństwo

- Przy połączeniu klient i serwer wymieniają klucze (ECDH P-256). Całe sterowanie (hasła, czat, polecenia) idzie zaszyfrowane kluczem sesji (AES-256-GCM).
- Głos jest szyfrowany AES-256-GCM kluczem wydawanym po zalogowaniu. Serwer przekazuje go dalej tylko w obrębie kanału.
- **Nie ma uwierzytelniania serwera** (ochrona przed podsłuchem, nie przed aktywnym atakiem „man in the middle"). Dla grupy znajomych to wystarczy.
- Hasło serwera i hasło admina są w `config.json` jawnym tekstem (plik ma uprawnienia 600). Hasła kanałów są zapisywane jako hasze (PBKDF2).
- Po 5 błędnych hasłach połączenie jest zrywane, a niezalogowane połączenia wygasają po 10 s.

## Ustawienia klienta

Zapisują się w `%AppData%\Voxly\settings.json`: urządzenia audio, tryb nadawania, język, automatyczne łączenie i identyfikator klienta `ClientId` (zawsze, bo odpowiada za stałe rangi; nie udostępniaj tego pliku). Przy „Zapamiętaj dane" zapisuje się tam też adres, nick i hasło serwera (jawnym tekstem).

## Rozwiązywanie problemów

| Problem | Co sprawdzić |
|---|---|
| „Nie można połączyć się z serwerem" | Adres, port UDP 9050 (firewall, router, panel VPS), `systemctl status voxly`. |
| „Serwer wymaga hasła" / „Nieprawidłowe hasło serwera" | Hasło ustawia admin w Panelu administratora. |
| „Serwer odrzucił połączenie" | Limit 30 osób albo klient i serwer z różnych wersji (wersja 1.4 klienta i serwera musi być zainstalowana po obu stronach, protokół się zmienił). |
| Nikt mnie nie słyszy | Mikrofon wyciszony (czerwony przycisk), zbyt wysoki próg wykrywania głosu, tryb push-to-talk bez wciśniętego klawisza, złe urządzenie. |
| Admin: „Administrator nie ustawił hasła" | W `config.json` zmień `AdminPassword` z wartości domyślnej i zrestartuj usługę. |
| SmartScreen blokuje instalator | „Więcej informacji → Uruchom mimo to" (program nie jest podpisany certyfikatem). |

## Licencja

Kod VOXLY jest udostępniony na licencji **MIT** (plik `LICENSE`, autor: Marcoxxdev): możesz go używać, zmieniać i rozpowszechniać, zachowując informację o prawach autorskich.
Program korzysta z bibliotek innych autorów (LiteNetLib, NAudio, Concentus), które mają własne licencje. Ich warunki znajdziesz na stronach tych pakietów w serwisie NuGet. Gotowy instalator zawiera też środowisko uruchomieniowe .NET od Microsoftu (licencja MIT).
Awatary to zwykłe emoji Unicode, a w repozytorium nie ma grafik ani postaci z cudzych gier.
