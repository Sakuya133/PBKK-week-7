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

api_pid=""
cleanup() {
  if [[ -n "$api_pid" ]] && kill -0 "$api_pid" 2>/dev/null; then
    kill "$api_pid" 2>/dev/null || true
    wait "$api_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

if [[ "${POS_START_LOCAL_API:-false}" == "true" ]]; then
  api_url="${POS_LOCAL_API_URL:-http://127.0.0.1:5077/}"
  if curl --silent --show-error --max-time 1 --fail "${api_url%/}/health" >/dev/null 2>&1; then
    echo "API lokal sudah aktif di $api_url; tidak akan dihentikan oleh launcher." | tee -a "$LOG_FILE"
  elif [[ -z "${POS_CONNECTION_STRING:-}" ]]; then
    echo "POS_CONNECTION_STRING belum diatur; API lokal tidak dimulai. Login tetap dapat dibuka." | tee -a "$LOG_FILE" >&2
  elif [[ -x "$APP_DIR/LocalApi/PosServer" ]]; then
    (
      cd "$APP_DIR/LocalApi"
      export ASPNETCORE_ENVIRONMENT=Development
      export POS_API_ALLOW_HTTP_DEV=true
      export ASPNETCORE_URLS="$api_url"
      exec ./PosServer
    ) >>"$LOG_FILE" 2>&1 &
    api_pid=$!
    export POS_API_URL="$api_url"
    ready=false
    for _ in $(seq 1 30); do
      if curl --silent --max-time 1 --fail "${api_url%/}/health" >/dev/null 2>&1; then ready=true; break; fi
      if ! kill -0 "$api_pid" 2>/dev/null; then break; fi
      sleep 1
    done
    if [[ "$ready" != true ]]; then
      echo "API lokal tidak siap di $api_url; login tetap dapat dibuka untuk menampilkan status koneksi." | tee -a "$LOG_FILE" >&2
    fi
  else
    echo "Paket LocalApi/PosServer tidak ditemukan; login tetap dapat dibuka." | tee -a "$LOG_FILE" >&2
  fi
fi
unset POS_CONNECTION_STRING

echo "Menjalankan POS Desktop dari $APP_DIR" | tee -a "$LOG_FILE"
cd "$APP_DIR"
"$APP" "$@"
app_status=$?
exit "$app_status"
