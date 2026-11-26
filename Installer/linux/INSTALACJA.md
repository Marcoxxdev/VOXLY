# VOXLY Server dla Linuksa (autor: Marcoxxdev)

## Instalacja (jedna komenda)

```
tar xzf Voxly-Server-linux.tar.gz
cd voxly-server
sudo bash voxly-server-install.sh
```

Skrypt sam:
1. doinstaluje .NET SDK, jeśli go nie ma (potrzebny tylko do zbudowania serwera),
2. zbuduje serwer (1-2 minuty, wymaga internetu),
3. zainstaluje go w `/opt/voxly` jako usługę systemd `voxly` (start z systemem, automatyczny restart),
4. wygeneruje losowe **hasło administratora** i wypisze je na ekranie,
5. otworzy port UDP w `ufw` lub `firewalld`, jeśli któryś jest aktywny.

## Hasło wejścia na serwer

Domyślnie serwer jest **otwarty**: wejść może każdy, kto zna adres. Hasło wejścia włącza administrator **z poziomu klienta**:
*Ustawienia → Administrator (zaloguj hasłem admina) → w menu po lewej pojawi się „Panel administratora" → „Wymagaj hasła do wejścia na serwer" → wpisz hasło → Zapisz.*
Hasło zapisuje się w `/opt/voxly/config.json` (`ServerPasswordEnabled`, `ServerPassword`), działa od razu i przetrwa restart.

## Opcje instalatora

| Polecenie | Działanie |
|---|---|
| `sudo bash voxly-server-install.sh --port 9100` | Instalacja na innym porcie UDP (domyślnie 9050). |
| `sudo bash voxly-server-install.sh` (ponownie) | Aktualizacja serwera, konfiguracja i kanały zostają. |
| `sudo bash voxly-server-install.sh --uninstall` | Usuwa usługę, zostawia dane. |
| `sudo bash voxly-server-install.sh --purge` | Usuwa usługę i wszystkie dane. |
| `bash voxly-server-install.sh --extract katalog` | Tylko rozpakowuje źródła (do wglądu). |

## Po instalacji

- Port UDP musi być otwarty także u dostawcy VPS (panel „firewall / security group") albo na routerze.
- `/opt/voxly/config.json`: `Port`, `AdminPassword`, `MaxUsers`, `ServerPasswordEnabled`, `ServerPassword`. Po ręcznej zmianie: `sudo systemctl restart voxly`.
- `/opt/voxly/channels.json`: kanały (zarządzane z klienta). Hasła kanałów są tu zapisane jako hasze.
- `/opt/voxly/ranks.json`: rangi nadane przez Admina (stałe, przetrwają restart). Klucze to hasze identyfikatorów klientów; nick w pliku służy tylko do podglądu. Rangę możesz cofnąć, usuwając wpis i restartując usługę (`sudo systemctl restart voxly`).
- Status: `systemctl status voxly`, logi: `journalctl -u voxly -f`.

## Wymagania

Linux z systemd (Ubuntu 22.04+, Debian 12+ i pochodne), x86_64 lub arm64, internet podczas instalacji, root.
