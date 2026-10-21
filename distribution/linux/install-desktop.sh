#!/usr/bin/env bash
set -euo pipefail
APP_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
BIN_DIR="${HOME}/.local/bin"
APP_DIR_USER="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
CONFIG_FILE="${XDG_CONFIG_HOME:-$HOME/.config}/posdesktop/pos-client.json"
mkdir -p "$BIN_DIR" "$APP_DIR_USER" "$(dirname -- "$CONFIG_FILE")"
chmod +x "$APP_DIR/PosDesktop" "$APP_DIR/launch-posdesktop.sh"
if [[ ! -f "$CONFIG_FILE" ]]; then
  printf '{\n  "apiUrl": "http://127.0.0.1:5077/"\n}\n' >"$CONFIG_FILE"
fi
escaped_dir="$(printf '%s' "$APP_DIR" | sed 's/\\/\\\\/g; s/"/\\"/g')"
cat >"$APP_DIR_USER/posdesktop.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=POS Desktop
Comment=Aplikasi kasir POS
Exec="$escaped_dir/launch-posdesktop.sh"
Path=$escaped_dir
Terminal=false
Categories=Office;Finance;
StartupWMClass=PosDesktop
EOF
chmod 644 "$APP_DIR_USER/posdesktop.desktop"
if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$APP_DIR_USER/posdesktop.desktop"
fi
echo "Shortcut dibuat: $APP_DIR_USER/posdesktop.desktop"
echo "Konfigurasi API: $CONFIG_FILE"
echo "Jalankan dari terminal: \"$APP_DIR/launch-posdesktop.sh\""
