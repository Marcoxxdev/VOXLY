#!/usr/bin/env bash
# =====================================================================
#  VOXLY Server - instalator dla Linuksa (jeden plik), autor: Marcoxxdev
#
#  Kazdy serwer dziala niezaleznie: port UDP = nazwa instancji.
#    port 9050  ->  katalog /opt/voxly-9050,  usluga systemd voxly-9050
#    port 9100  ->  katalog /opt/voxly-9100,  usluga systemd voxly-9100
#  Na jednym VPS mozna zainstalowac dowolnie wiele serwerow (rozne porty).
#
#  Uzycie:
#    sudo bash voxly-server-install.sh                    instalacja / aktualizacja (pyta o port)
#    sudo bash voxly-server-install.sh --port 9100        bez pytania o port
#    sudo bash voxly-server-install.sh --uninstall [--port N]   usuniecie uslugi (dane zostaja)
#    sudo bash voxly-server-install.sh --purge [--port N]       usuniecie uslugi i danych serwera
#    bash voxly-server-install.sh --extract DIR           tylko rozpakuj zrodla do DIR
#
#  Zarzadzanie:  sudo systemctl start|stop|restart|status voxly-PORT
#
#  Skrypt: instaluje .NET SDK (jesli brak), buduje serwer, instaluje go w
#  /opt/voxly-PORT jako usluge systemd, generuje haslo admina i otwiera port w firewallu.
#  Ponowne uruchomienie z tym samym portem aktualizuje serwer (dane zostaja).
#  Wspierane: Linux x86_64 / arm64 z systemd (Ubuntu, Debian i pochodne).
# =====================================================================
set -eu

BASE=/opt
PREFIX=voxly
SVCUSER=voxly
PORT=""
MODE=install
EXTRACT_DIR=""
LEGACY_DIR=$BASE/voxly                          # stary uklad (jeden serwer, usluga "voxly")
LEGACY_UNIT=/etc/systemd/system/voxly.service
MIGRATE=0

say()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m!!  %s\033[0m\n' "$*"; }
die()  { printf '\033[1;31mBLAD: %s\033[0m\n' "$*" >&2; exit 1; }

while [ $# -gt 0 ]; do
  case "$1" in
    --port)      PORT="${2:-}"; shift 2 ;;
    --uninstall) MODE=uninstall; shift ;;
    --purge)     MODE=purge; shift ;;
    --extract)   MODE=extract; EXTRACT_DIR="${2:-}"; shift 2 ;;
    -h|--help)   awk 'NR>1 && /^#/ {print; next} NR>1 {exit}' "$0"; exit 0 ;;
    *) die "Nieznana opcja: $1 (uzyj --help)" ;;
  esac
done

extract_payload() {   # $1 = katalog docelowy
  local line
  line=$(grep -a -n '^__PAYLOAD_BELOW__$' "$0" | head -n 1 | cut -d: -f1)
  [ -n "$line" ] || die "Uszkodzony instalator (brak danych)."
  mkdir -p "$1"
  tail -n +"$((line + 1))" "$0" | tar xz -C "$1"
}

if [ "$MODE" = extract ]; then
  [ -n "$EXTRACT_DIR" ] || die "Podaj katalog: --extract DIR"
  extract_payload "$EXTRACT_DIR"
  echo "Zrodla rozpakowane do: $EXTRACT_DIR"
  exit 0
fi

[ "$(id -u)" -eq 0 ] || die "Uruchom jako root: sudo bash $0"
command -v systemctl >/dev/null 2>&1 || die "Wymagany jest systemd (polecenie systemctl)."

# ---------- pomocnicze ----------
have_tty() { ( : </dev/tty ) 2>/dev/null; }

ask() {   # $1 = pytanie, $2 = domyslna odpowiedz
  local ans=""
  printf '%s [%s]: ' "$1" "$2" >/dev/tty
  read -r ans </dev/tty || ans=""
  printf '%s' "${ans:-$2}"
}

legacy_port() {
  local p=""
  [ -f "$LEGACY_DIR/config.json" ] && p=$(grep -o '"Port": *[0-9]*' "$LEGACY_DIR/config.json" | grep -o '[0-9]*$' || true)
  printf '%s' "${p:-9050}"
}

list_ports() {   # porty zainstalowanych serwerow (takze starej instalacji w /opt/voxly)
  local d
  for d in "$BASE/$PREFIX"-[0-9]*; do [ -d "$d" ] && echo "${d##*-}"; done
  [ -d "$LEGACY_DIR" ] && legacy_port
  return 0
}

valid_port() {
  case "$1" in *[!0-9]*|"") return 1 ;; esac
  [ "$1" -ge 1 ] && [ "$1" -le 65535 ]
}

set_names() {
  APP=$PREFIX-$PORT
  TARGET=$BASE/$APP
  UNIT=/etc/systemd/system/$APP.service
}

# ---------- wybor portu ----------
EXISTING=$(list_ports | sort -un | tr '\n' ' ')
if [ -z "$PORT" ]; then
  if [ "$MODE" = install ]; then
    DEF=9050
    if [ -d "$LEGACY_DIR" ] && [ -z "$(ls -d "$BASE/$PREFIX"-[0-9]* 2>/dev/null || true)" ]; then
      DEF=$(legacy_port)   # jedyny, stary serwer: domyslnie zaktualizuj go (zostanie przeniesiony do voxly-PORT)
    else
      while [ -d "$BASE/$PREFIX-$DEF" ]; do DEF=$((DEF + 1)); done
    fi
    if have_tty; then
      [ -n "$EXISTING" ] && printf 'Zainstalowane serwery (porty UDP): %s\n' "$EXISTING" >/dev/tty
      printf 'Podaj port UDP tego serwera. Nowy port = nowy serwer, istniejacy port = aktualizacja.\n' >/dev/tty
      PORT=$(ask "Port UDP" "$DEF")
    else
      PORT=$DEF
      warn "Brak terminala - uzywam portu $PORT (mozna podac: --port N)."
    fi
  else
    set -- $EXISTING
    if [ $# -eq 0 ]; then die "Nie znaleziono zainstalowanych serwerow Voxly."
    elif [ $# -eq 1 ]; then PORT=$1
    elif have_tty; then
      printf 'Zainstalowane serwery (porty UDP): %s\n' "$EXISTING" >/dev/tty
      PORT=$(ask "Ktory port usunac" "$1")
    else die "Jest kilka serwerow ($EXISTING) - podaj --port N."
    fi
  fi
fi
valid_port "$PORT" || die "Port musi byc liczba z zakresu 1-65535."
PORT=$((10#$PORT))
set_names

# ---------- odinstalowanie ----------
if [ "$MODE" = uninstall ] || [ "$MODE" = purge ]; then
  if [ ! -d "$TARGET" ] && [ -d "$LEGACY_DIR" ] && [ "$(legacy_port)" = "$PORT" ]; then
    APP=voxly; TARGET=$LEGACY_DIR; UNIT=$LEGACY_UNIT     # stara instalacja
  fi
  [ -d "$TARGET" ] || die "Brak serwera na porcie $PORT ($TARGET). Zainstalowane porty: ${EXISTING:-brak}"
  say "Usuwanie uslugi $APP"
  systemctl disable --now "$APP" 2>/dev/null || true
  rm -f "$UNIT"
  systemctl daemon-reload
  if [ "$MODE" = purge ]; then
    rm -rf "$TARGET"
    if [ -z "$(list_ports)" ]; then
      id "$SVCUSER" >/dev/null 2>&1 && userdel "$SVCUSER" 2>/dev/null || true
    fi
    echo "Usunieto usluge i wszystkie dane ($TARGET)."
  else
    rm -f "$TARGET/VoxlyServer"
    echo "Usunieto usluge. Konfiguracja i kanaly zostaly w $TARGET (pelne usuniecie: --purge --port $PORT)."
  fi
  exit 0
fi

# ---------- stara instalacja (/opt/voxly) na tym samym porcie: przeniesienie ----------
if [ ! -d "$TARGET" ] && [ -d "$LEGACY_DIR" ] && [ "$(legacy_port)" = "$PORT" ]; then
  MIGRATE=1
  warn "Wykryto stary serwer w $LEGACY_DIR (port $PORT). Zostanie przeniesiony do $TARGET (dane zostaja)."
fi

# ---------- czy port jest wolny (tylko dla nowego serwera) ----------
if [ ! -d "$TARGET" ] && [ "$MIGRATE" -eq 0 ] && command -v ss >/dev/null 2>&1; then
  if ss -H -lun 2>/dev/null | awk '{print $4}' | grep -qE "[:.]$PORT$"; then
    die "Port UDP $PORT jest juz zajety przez inny program. Wybierz inny port (--port N)."
  fi
fi

case "$(uname -m)" in
  x86_64|amd64)  RID=linux-x64 ;;
  aarch64|arm64) RID=linux-arm64 ;;
  *) die "Nieobslugiwana architektura: $(uname -m)" ;;
esac

# ---------- zaleznosci: curl/tar ----------
if ! command -v curl >/dev/null 2>&1 && ! command -v wget >/dev/null 2>&1; then
  if command -v apt-get >/dev/null 2>&1; then
    say "Instalacja curl"; apt-get update -qq && apt-get install -y -qq curl ca-certificates
  else
    die "Zainstaluj curl lub wget i uruchom ponownie."
  fi
fi

# ---------- .NET SDK ----------
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
have_sdk() { command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -qE '^(8|9|10|11)\.'; }

if ! have_sdk; then
  say "Instalacja .NET SDK (potrzebny tylko do zbudowania serwera)"
  if command -v apt-get >/dev/null 2>&1; then
    apt-get update -qq || true
    apt-get install -y -qq dotnet-sdk-8.0 >/dev/null 2>&1 || warn "apt nie ma pakietu dotnet-sdk-8.0, probuje oficjalnego skryptu Microsoftu"
  fi
  if ! have_sdk; then
    DI=$(mktemp)
    if command -v curl >/dev/null 2>&1; then curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$DI"
    else wget -q https://dot.net/v1/dotnet-install.sh -O "$DI"; fi
    bash "$DI" --channel 8.0 --install-dir /opt/dotnet-sdk --no-path
    rm -f "$DI"
    export DOTNET_ROOT=/opt/dotnet-sdk PATH="/opt/dotnet-sdk:$PATH"
  fi
  have_sdk || die "Nie udalo sie zainstalowac .NET SDK. Zainstaluj recznie .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
fi

# ---------- budowanie ----------
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
say "Rozpakowanie zrodel i budowanie serwera ($RID) - moze potrwac 1-2 minuty"
extract_payload "$WORK/src"
dotnet publish "$WORK/src/Server/VoiceServer.csproj" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:DebugType=none -o "$WORK/out" >"$WORK/build.log" 2>&1 \
  || { tail -n 30 "$WORK/build.log"; die "Budowanie nie powiodlo sie (log powyzej)."; }
[ -f "$WORK/out/VoxlyServer" ] || die "Brak pliku wynikowego VoxlyServer."

# ---------- instalacja ----------
say "Instalacja w $TARGET (usluga $APP, port UDP $PORT)"
id "$SVCUSER" >/dev/null 2>&1 || useradd --system --home "$BASE" --shell /usr/sbin/nologin "$SVCUSER"

if [ "$MIGRATE" -eq 1 ]; then
  systemctl disable --now voxly 2>/dev/null || true
  rm -f "$LEGACY_UNIT"
  mv "$LEGACY_DIR" "$TARGET"
fi
mkdir -p "$TARGET"
systemctl stop "$APP" 2>/dev/null || true
install -m 755 "$WORK/out/VoxlyServer" "$TARGET/VoxlyServer"

cat > "$UNIT" <<UNIT
[Unit]
Description=VOXLY - serwer komunikatora glosowego (port UDP $PORT)
After=network.target

[Service]
Type=simple
User=$SVCUSER
WorkingDirectory=$TARGET
ExecStart=$TARGET/VoxlyServer
Restart=always
RestartSec=3
NoNewPrivileges=true
ProtectSystem=full
ReadWritePaths=$TARGET

[Install]
WantedBy=multi-user.target
UNIT

NEWCFG=0
if [ ! -f "$TARGET/config.json" ]; then
  NEWCFG=1
  (cd "$TARGET" && ./VoxlyServer >/dev/null 2>&1 || true)   # pierwsze uruchomienie tworzy config.json i konczy dzialanie
  [ -f "$TARGET/config.json" ] || die "Serwer nie utworzyl config.json."
  ADMIN=$(head -c 96 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 16 || true)
  [ ${#ADMIN} -eq 16 ] || die "Nie udalo sie wygenerowac hasla."
  sed -i "s/ZMIEN-MNIE-haslo-admina/$ADMIN/" "$TARGET/config.json"
fi
sed -i -E "s/(\"Port\": *)[0-9]+/\1$PORT/" "$TARGET/config.json"
chown -R "$SVCUSER:$SVCUSER" "$TARGET"
chmod 600 "$TARGET/config.json"
CFGPORT=$PORT

# ---------- firewall ----------
FW="brak aktywnego firewalla (UFW/firewalld) - sprawdz recznie, czy port jest otwarty"
if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q "Status: active"; then
  ufw allow "$CFGPORT/udp" >/dev/null && FW="ufw: otwarto $CFGPORT/udp"
elif command -v firewall-cmd >/dev/null 2>&1 && firewall-cmd --state >/dev/null 2>&1; then
  firewall-cmd --permanent --add-port="$CFGPORT/udp" >/dev/null && firewall-cmd --reload >/dev/null && FW="firewalld: otwarto $CFGPORT/udp"
fi

# ---------- start ----------
systemctl daemon-reload
systemctl enable "$APP" >/dev/null 2>&1
systemctl restart "$APP"
sleep 1
systemctl is-active --quiet "$APP" || { journalctl -u "$APP" -n 20 --no-pager || true; die "Usluga nie wystartowala."; }

IP=$( (curl -fsS -m 4 https://api.ipify.org 2>/dev/null || wget -qO- -T 4 https://api.ipify.org 2>/dev/null) || true)

say "GOTOWE - serwer dziala"
echo "  Port UDP:      $CFGPORT"
echo "  Adres dla klientow: ${IP:-<adres IP serwera>}:$CFGPORT"
echo "  Katalog:       $TARGET"
echo "  Usluga:        $APP"
echo "  Firewall:      $FW"
if [ "$NEWCFG" -eq 1 ]; then
  echo
  echo "  Haslo administratora (tylko dla Ciebie): $ADMIN"
  echo "  Zapisane w $TARGET/config.json - zapisz je teraz w bezpiecznym miejscu."
  echo "  Serwer jest otwarty (bez hasla wejscia). Zalogowany admin moze wlaczyc haslo serwera"
  echo "  w kliencie: po zalogowaniu hasla admina (Ustawienia) wybierz w menu Panel administratora (zapisze sie w config.json)."
else
  echo "  Konfiguracja zachowana ($TARGET/config.json)."
fi
echo
echo "  Start:     sudo systemctl start $APP"
echo "  Stop:      sudo systemctl stop $APP"
echo "  Restart:   sudo systemctl restart $APP"
echo "  Status:    systemctl status $APP        Logi: journalctl -u $APP -f"
echo "  Kolejny serwer: uruchom instalator ponownie i podaj inny port."
echo "  Pamietaj o otwarciu portu UDP $CFGPORT u dostawcy VPS / na routerze."
exit 0
__PAYLOAD_BELOW__
