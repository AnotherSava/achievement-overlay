#!/bin/bash
# Captures docs/screenshots/tray-menu.png — the system tray context menu.
#
# Registered as the capture command for the "tray-menu" entry in docs/screenshots/screenshots.json.
# Deploys first so the menu shown is the working tree's, not an installed release's.
#
# Usage: bash docs/screenshots/capture/tray-menu.sh

set -euo pipefail

REPO="$(git rev-parse --show-toplevel)"
OUT="$REPO/docs/screenshots/tray-menu.png"
DRIVER="$REPO/docs/screenshots/capture/tray-menu.ps1"

echo "=== Deploying the current build so the shot matches the working tree ==="
bash "$REPO/scripts/deploy.sh" >/dev/null
sleep 3

echo "=== Capturing ==="
CAPTURE_OUT="$(cygpath -w "$OUT" 2>/dev/null || echo "$OUT")" \
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$DRIVER" 2>/dev/null || echo "$DRIVER")"

echo "=== Wrote $OUT ==="
file "$OUT"
