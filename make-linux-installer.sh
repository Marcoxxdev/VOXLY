#!/usr/bin/env bash
# Generuje instalator serwera VOXLY dla Linuksa z aktualnych zrodel (Server/ + Shared/).
# Uruchamiaj po kazdej zmianie w serwerze lub protokole. Wynik w dist/:
#   voxly-server-install.sh        - samorozpakowujacy sie instalator (jeden plik)
#   Voxly-Server-linux.tar.gz      - instalator + INSTALACJA.md
set -eu
cd "$(dirname "$0")"
mkdir -p dist
rm -rf dist/_stage; mkdir -p dist/_stage/voxly-server dist/_stage/src
cp -r Server Shared dist/_stage/src/
rm -rf dist/_stage/src/Server/bin dist/_stage/src/Server/obj
tar czf dist/_stage/payload.tgz -C dist/_stage/src Server Shared
cat Installer/linux/header.sh dist/_stage/payload.tgz > dist/voxly-server-install.sh
chmod +x dist/voxly-server-install.sh
cp dist/voxly-server-install.sh dist/_stage/voxly-server/
cp Installer/linux/INSTALACJA.md dist/_stage/voxly-server/
tar czf dist/Voxly-Server-linux.tar.gz -C dist/_stage voxly-server
rm -rf dist/_stage
echo "Gotowe: dist/voxly-server-install.sh oraz dist/Voxly-Server-linux.tar.gz"
