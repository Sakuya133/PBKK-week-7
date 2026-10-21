#!/usr/bin/env bash
set -u

APP_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
APP="$APP_DIR/PosDesktop"
STATE_ROOT="${XDG_STATE_HOME:-$HOME/.local/state}"
STATE_DIR="$STATE_ROOT/posdesktop"
LOG_DIR="$STATE_DIR/logs"
LOG_FILE="$LOG_DIR/posdesktop.log"
mkdir -p "$LOG_DIR" || { echo "Tidak dapat membuat folder log: $LOG_DIR" >&2; exit 2; }
touch "$LOG_FILE" 2>/dev/null || { echo "Tidak dapat menulis log: $LOG_FILE" >&2; exit 2; }

if [[ ! -x "$APP" ]]; then
  echo "Executable tidak ditemukan atau tidak dapat dijalankan: $APP" >&2
  echo "Ekstrak ulang paket POS-Desktop-linux-x64.tar.gz." >&2
  exit 2
fi

report_error() {
  printf '%s\n' "$*" >&2
  printf '%s %s\n' "$(date --iso-8601=seconds)" "$*" >>"$LOG_FILE"
  if [[ -n "${DISPLAY:-}" ]] && command -v notify-send >/dev/null 2>&1; then
    notify-send --urgency=critical "POS Desktop" "$*" >/dev/null 2>&1 || true
  fi
}

# Avalonia uses X11/XWayland by default. Never guess a display or bypass X authorization.
if [[ -z "${DISPLAY:-}" ]]; then
  if [[ -n "${WAYLAND_DISPLAY:-}" ]]; then
    report_error "POS Desktop memakai X11/XWayland, tetapi sesi Wayland tidak menyediakan DISPLAY. Aktifkan XWayland dan jalankan dari terminal desktop."
  elif [[ -n "${SSH_CONNECTION:-}" || -n "${SSH_TTY:-}" ]]; then
    report_error "Sesi SSH ini tidak memiliki display grafis. Jalankan dari terminal desktop Ubuntu atau gunakan X11 forwarding terotorisasi; jangan mengisi DISPLAY=:0 manual."
  else
    report_error "Tidak ada DISPLAY. Jalankan POS Desktop dari sesi desktop Linux dengan X11/XWayland."
  fi
  exit 2
fi

if command -v xdpyinfo >/dev/null 2>&1 && ! xdpyinfo -display "$DISPLAY" >/dev/null 2>&1; then
  report_error "Tidak dapat mengakses display $DISPLAY dengan otorisasi sesi ini. Jalankan dari sesi desktop yang sama dan jangan mengubah XAUTHORITY."
  exit 2
fi

unset POS_CONNECTION_STRING
export POS_DESKTOP_MODE=offline

echo "Menjalankan POS Desktop dari $APP_DIR" | tee -a "$LOG_FILE"
cd "$APP_DIR"
"$APP" "$@"
app_status=$?
exit "$app_status"
