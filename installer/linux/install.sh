#!/usr/bin/env sh
# Installs Toaster as a systemd *user* unit. No root, no system-wide state:
# everything lands under $HOME so the service runs as the same user whose agents
# talk to it, and the data directory is that user's own.
set -eu

APP_DIR="${TOASTER_APP_DIR:-$HOME/.local/lib/toaster}"
BIN_DIR="${TOASTER_BIN_DIR:-$HOME/.local/bin}"
UNIT_DIR="$HOME/.config/systemd/user"
SOURCE_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
PORT="${TOASTER_PORT:-47321}"

say() { printf '%s\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

[ -d "$SOURCE_DIR/service" ] || die "no service/ directory beside this script — run it from the unpacked tarball"

say "Installing Toaster to $APP_DIR"
mkdir -p "$APP_DIR" "$BIN_DIR"
rm -rf "$APP_DIR/service" "$APP_DIR/cli"
cp -r "$SOURCE_DIR/service" "$SOURCE_DIR/cli" "$APP_DIR/"

# A tarball built on Windows carries no execute bits.
chmod +x "$APP_DIR/service/Toaster.Service" "$APP_DIR/cli/toaster"

ln -sf "$APP_DIR/cli/toaster" "$BIN_DIR/toaster"
say "Linked CLI to $BIN_DIR/toaster"

# systemd is absent in containers and in WSL without systemd=true. Install the
# files anyway and say how to run it, rather than failing at the last step.
if ! command -v systemctl >/dev/null 2>&1 || [ ! -d /run/systemd/system ]; then
    say ""
    say "systemd is not running here, so no service was registered."
    say "Start Toaster manually with:"
    say "    $APP_DIR/service/Toaster.Service"
    exit 0
fi

mkdir -p "$UNIT_DIR"
sed "s|__APP_DIR__|$APP_DIR|g" "$SOURCE_DIR/toaster.service" > "$UNIT_DIR/toaster.service"

systemctl --user daemon-reload
systemctl --user enable --now toaster.service

# Without lingering, the unit stops when the last session for this user closes.
if command -v loginctl >/dev/null 2>&1; then
    loginctl enable-linger "$(id -un)" >/dev/null 2>&1 ||
        say "note: could not enable lingering; Toaster will stop when you log out."
fi

say "Waiting for the service to answer..."
i=0
while [ "$i" -lt 20 ]; do
    if curl -fsS -m 2 "http://127.0.0.1:$PORT/api/v1/status" >/dev/null 2>&1; then
        say ""
        say "Toaster is running."
        curl -fsS -m 2 "http://127.0.0.1:$PORT/api/v1/status" || true
        say ""
        say "MCP endpoint: http://127.0.0.1:$PORT/mcp"
        say "Status:       systemctl --user status toaster"
        say "Logs:         journalctl --user -u toaster -f"
        exit 0
    fi
    i=$((i + 1))
    sleep 1
done

say ""
die "service did not answer on port $PORT — check: journalctl --user -u toaster -n 50"
