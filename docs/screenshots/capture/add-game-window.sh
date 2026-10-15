#!/bin/bash
# Captures docs/screenshots/add-game.png: the Add game wizard on its Ready page.
#
# Registered as the capture command for the "add-game-window" entry in
# docs/screenshots/screenshots.json. Deploys first on purpose: the shot has to document the working
# tree, not whatever release happens to be installed.
#
# The driver stops at Ready and never presses the button that would modify a game. See the header of
# add-game-window.ps1 for how that is enforced.
#
# Usage: CAPTURE_GAME_DIR=<game folder> bash docs/screenshots/capture/add-game-window.sh
#
# CAPTURE_GAME_DIR is required; the driver's header says what kind of game it must name.

set -euo pipefail

# Checked here rather than left to the driver, which runs only after the deploy has relaunched the app.
: "${CAPTURE_GAME_DIR:?Set CAPTURE_GAME_DIR to a game folder with a Steam DLL and no steam_settings of its own.}"

REPO="$(git rev-parse --show-toplevel)"
OUT="$REPO/docs/screenshots/add-game.png"
DRIVER="$REPO/docs/screenshots/capture/add-game-window.ps1"

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
