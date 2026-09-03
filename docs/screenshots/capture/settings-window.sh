#!/bin/bash
# Captures docs/screenshots/settings.png — the Settings window on its Notifications page, with the
# desktop behind it transparent rather than baked in.
#
# Registered as the capture command for the "settings-window" entry in
# docs/screenshots/screenshots.json. Deploys first on purpose: the shot has to document the working
# tree, not whatever release happens to be installed.
#
# Usage: bash docs/screenshots/capture/settings-window.sh

set -euo pipefail

REPO="$(git rev-parse --show-toplevel)"
OUT="$REPO/docs/screenshots/settings.png"
DRIVER="$REPO/docs/screenshots/capture/settings-window.ps1"

echo "=== Deploying the current build so the shot matches the working tree ==="
bash "$REPO/scripts/deploy.sh" >/dev/null
sleep 3

echo "=== Capturing ==="
CAPTURE_OUT="$(cygpath -w "$OUT" 2>/dev/null || echo "$OUT")" \
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$DRIVER" 2>/dev/null || echo "$DRIVER")"

# The window's own edge is near-white and the docs page is white, so the shot has no visible
# boundary without this. Part of the capture, not a manual step: applied by hand it would be
# silently lost the next time this script runs.
echo "=== Adding the hairline ==="
python "$REPO/docs/screenshots/capture/lib/docborder.py" "$OUT"

echo "=== Wrote $OUT ==="
file "$OUT"
