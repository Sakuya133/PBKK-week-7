#!/usr/bin/env bash
set -euo pipefail
APP_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
BIN_DIR="${HOME}/.local/bin"
APP_DIR_USER="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
DESKTOP_DIR="$(xdg-user-dir DESKTOP 2>/dev/null || true)"
if [[ -z "$DESKTOP_DIR" ]]; then DESKTOP_DIR="$HOME/Desktop"; fi
mkdir -p "$BIN_DIR" "$APP_DIR_USER" "$DESKTOP_DIR"
chmod +x "$APP_DIR/PosDesktop" "$APP_DIR/launch-posdesktop.sh"
escaped_dir="$(printf '%s' "$APP_DIR" | sed 's/\\/\\\\/g; s/"/\\"/g')"
if [[ -f "$APP_DIR/Assets/mikogomi.png" ]]; then
  icon_path="$APP_DIR/Assets/mikogomi.png"
else
  icon_path="applications-office"
fi
write_shortcut() {
  local shortcut="$1"
  cat >"$shortcut" <<EOF
[Desktop Entry]
Type=Application
Name=MiKuGoMi POS
Comment=Aplikasi kasir MiKuGoMi
Exec="$escaped_dir/launch-posdesktop.sh"
Path=$escaped_dir
Icon=$icon_path
TryExec=$APP_DIR/launch-posdesktop.sh
Terminal=false
Categories=Office;Finance;
StartupWMClass=PosDesktop
EOF
  chmod +x "$shortcut"
  if command -v gio >/dev/null 2>&1; then gio set "$shortcut" metadata::trusted true 2>/dev/null || true; fi
  if command -v desktop-file-validate >/dev/null 2>&1; then desktop-file-validate "$shortcut"; fi
}

write_shortcut "$APP_DIR_USER/mikogomi-pos.desktop"
write_shortcut "$DESKTOP_DIR/MiKuGoMi POS.desktop"
if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$APP_DIR_USER/mikogomi-pos.desktop"
fi
echo "Shortcut menu aplikasi: $APP_DIR_USER/mikogomi-pos.desktop"
echo "Shortcut Desktop: $DESKTOP_DIR/MiKuGoMi POS.desktop"
echo "Mode demo offline: SQLite dibuat otomatis saat aplikasi pertama dibuka."
echo "Jalankan dari terminal: \"$APP_DIR/launch-posdesktop.sh\""
