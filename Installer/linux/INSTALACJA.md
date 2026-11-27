# VOXLY Server dla Linuksa (autor: Marcoxxdev)

## Instalacja (jedna komenda)

```
tar xzf Voxly-Server-linux.tar.gz
cd voxly-server
sudo bash voxly-server-install.sh
```

Skrypt **zapyta o port UDP** (domyślnie 9050) i dla niego utworzy osobny serwer: katalog `/opt/voxly-PORT` i usługę systemd `voxly-PORT`. Dzięki temu na jednym VPS możesz mieć wiele niezależnych serwerów: uruchom instalator ponownie i podaj inny port.

Skrypt sam:
1. doinstaluje .NET SDK, jeśli go nie ma (potrzebny tylko do zbudowania serwera),
2. zbuduje serwer (1-2 minuty, wymaga internetu),
3. zainstaluje go w `/opt/voxly-PORT` jako usługę systemd `voxly-PORT` (start z systemem, automatyczny restart),
4. wygeneruje losowe **hasło administratora** i wypisze je na ekranie,
5. otworzy port UDP w `ufw` lub `firewalld`, jeśli któryś jest aktywny.

## Hasło wejścia na serwer

Domyślnie serwer jest **otwarty**: wejść może każdy, kto zna adres. Hasło wejścia włącza administrator **z poziomu klienta**:
*Ustawienia → Administrator (zaloguj hasłem admina) → w menu po lewej pojawi się „Panel administratora" → „Wymagaj hasła do wejścia na serwer" → wpisz hasło → Zapisz.*
Hasło zapisuje się w `/opt/voxly-PORT/config.json` (`ServerPasswordEnabled`, `ServerPassword`), działa od razu i przetrwa restart.

## Opcje instalatora

| Polecenie | Działanie |
|---|---|
| `sudo bash voxly-server-install.sh` | Pyta o port. Nowy port = nowy serwer, port istniejącego serwera = jego aktualizacja (konfiguracja i kanały zostają). |
| `sudo bash voxly-server-install.sh --port 9100` | To samo bez pytania o port. |
| `sudo bash voxly-server-install.sh --uninstall --port 9100` | Usuwa usługę serwera z portu 9100, zostawia dane (bez `--port` skrypt zapyta, który serwer). |
| `sudo bash voxly-server-install.sh --purge --port 9100` | Usuwa usługę i wszystkie dane tego serwera. |
| `bash voxly-server-install.sh --extract katalog` | Tylko rozpakowuje źródła (do wglądu). |

## Wiele serwerów na jednym VPS

Każdy serwer ma własny port UDP, katalog, konfigurację, kanały, rangi i hasło admina:

| Port | Katalog | Usługa |
|---|---|---|
| 9050 | `/opt/voxly-9050` | `voxly-9050` |
| 9100 | `/opt/voxly-9100` | `voxly-9100` |

Start, stop i restart: `sudo systemctl start|stop|restart voxly-9100`. Klienci łączą się przez `adres:port`, np. `203.0.113.5:9100`. Aktualizacja wszystkich serwerów: uruchom instalator osobno dla każdego portu.

Stara instalacja z `/opt/voxly` (usługa `voxly`) zostaje przeniesiona do `/opt/voxly-PORT` przy ponownym uruchomieniu instalatora z jej portem; dane się zachowują, a usługa zmienia nazwę na `voxly-PORT`.

## Po instalacji

- Port UDP musi być otwarty także u dostawcy VPS (panel „firewall / security group") albo na routerze.
- `/opt/voxly-PORT/config.json`: `Port`, `AdminPassword`, `MaxUsers`, `ServerPasswordEnabled`, `ServerPassword`. Po ręcznej zmianie: `sudo systemctl restart voxly-PORT`.
- `/opt/voxly-PORT/channels.json`: kanały (zarządzane z klienta). Hasła kanałów są tu zapisane jako hasze.
- `/opt/voxly-PORT/ranks.json`: rangi nadane przez Admina (stałe, przetrwają restart). Klucze to hasze identyfikatorów klientów; nick w pliku służy tylko do podglądu. Rangę możesz cofnąć, usuwając wpis i restartując usługę (`sudo systemctl restart voxly-PORT`).
- Status: `systemctl status voxly-PORT`, logi: `journalctl -u voxly-PORT -f`.

## Wymagania

Linux z systemd (Ubuntu 22.04+, Debian 12+ i pochodne), x86_64 lub arm64, internet podczas instalacji, root.
