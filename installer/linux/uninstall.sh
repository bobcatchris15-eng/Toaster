#!/usr/bin/env sh
# Removes the Toaster user unit and program files. Your Toast is left alone
# unless you pass --purge.
set -eu

APP_DIR="${TOASTER_APP_DIR:-$HOME/.local/lib/toaster}"
BIN_DIR="${TOASTER_BIN_DIR:-$HOME/.local/bin}"
UNIT_DIR="$HOME/.config/systemd/user"
PORT="${TOASTER_PORT:-47321}"
PURGE=0

[ "${1:-}" = "--purge" ] && PURGE=1

say() { printf '%s\n' "$*"; }

# Ask the running service where its data actually lives, before stopping it. A
# privileged install uses /var/lib/toaster, not the XDG path, so guessing here
# would name the wrong directory — and --purge would leave the real one behind.
DATA_DIR=$(curl -fsS -m 2 "http://127.0.0.1:$PORT/api/v1/status" 2>/dev/null |
    sed -n 's/.*"dataPath":"\([^"]*\)".*/\1/p')

if [ -z "${DATA_DIR:-}" ]; then
    if [ "$(id -u)" -eq 0 ]; then
        DATA_DIR=/var/lib/toaster
    else
        DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/toaster"
    fi
    say "Service is not answering; assuming data at $DATA_DIR"
fi

if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then
    systemctl --user disable --now toaster.service >/dev/null 2>&1 || true
    rm -f "$UNIT_DIR/toaster.service"
    systemctl --user daemon-reload || true
    say "Removed the systemd user unit."
fi

rm -rf "$APP_DIR"
rm -f "$BIN_DIR/toaster"
say "Removed $APP_DIR and the CLI symlink."

if [ "$PURGE" -eq 1 ]; then
    rm -rf "$DATA_DIR"
    say "Purged $DATA_DIR — Toast, sources and observations are gone."
else
    say ""
    say "Your data was kept at: $DATA_DIR"
    say "Remove it with: $0 --purge"
fi
