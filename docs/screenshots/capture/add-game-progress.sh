#!/bin/bash
# Captures docs/screenshots/add-game-progress.png: the Add game wizard after a finished run, with its
# details log open.
#
# Registered as the capture command for the "add-game-progress" entry in
# docs/screenshots/screenshots.json. Deploys first on purpose: the shot has to document the working
# tree, not whatever release happens to be installed.
#
# A finished run means pressing Add game, which backs up and replaces the game's steam_api64.dll. So the
# game is a throwaway built here: an "Example Game" folder under the games root holding a copy of a stock
# Steam DLL and a steam_appid.txt naming Portal 2 (620), so the log shows no library game and the AppID
# page is skipped. The folder is deleted afterwards, and the installed config.json, which the run's
# registration can write to, is put back as it was.
#
# Usage: CAPTURE_STEAM_DLL=<a stock steam_api64.dll> bash docs/screenshots/capture/add-game-progress.sh
#
# CAPTURE_STEAM_DLL is required: any game's unmodified Valve steam_api64.dll, which is one machine's file.
# CAPTURE_GAMES_ROOT defaults to C:/Games and must be a folder in the app's gamesPaths, so registering the
# game changes no path setting.

set -euo pipefail

: "${CAPTURE_STEAM_DLL:?Set CAPTURE_STEAM_DLL to a stock Valve steam_api64.dll to copy into the throwaway game.}"
GAMES_ROOT="${CAPTURE_GAMES_ROOT:-C:/Games}"
GAME="$GAMES_ROOT/Example Game"
CONFIG="C:/Programs/achievement-overlay/config.json"

REPO="$(git rev-parse --show-toplevel)"
OUT="$REPO/docs/screenshots/add-game-progress.png"
DRIVER="$REPO/docs/screenshots/capture/add-game-window.ps1"
SCRATCH="$REPO/tmp/add-game-progress"

if [ -e "$GAME" ]; then
  echo "Refusing: $GAME already exists, and this script deletes the folder it builds." >&2
  exit 1
fi

echo "=== Deploying the current build so the shot matches the working tree ==="
(cd "$REPO" && bash ~/.claude/skills/deploy/scripts/run.sh) >/dev/null
sleep 3

mkdir -p "$SCRATCH"
cp "$CONFIG" "$SCRATCH/config.json"
cleanup() {
  rm -rf "$GAME"
  cp "$SCRATCH/config.json" "$CONFIG"
}
trap cleanup EXIT

mkdir -p "$GAME"
cp "$CAPTURE_STEAM_DLL" "$GAME/steam_api64.dll"
printf '620\n' > "$GAME/steam_appid.txt"

echo "=== Capturing ==="
CAPTURE_OUT="$(cygpath -w "$OUT" 2>/dev/null || echo "$OUT")" \
CAPTURE_GAME_DIR="$(cygpath -w "$GAME" 2>/dev/null || echo "$GAME")" \
CAPTURE_STAGE=progress CAPTURE_THROWAWAY=1 \
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$DRIVER" 2>/dev/null || echo "$DRIVER")"

# The window's own edge is near-white and the docs page is white, so the shot has no visible
# boundary without this. Part of the capture, not a manual step: applied by hand it would be
# silently lost the next time this script runs.
echo "=== Adding the hairline ==="
python "$REPO/docs/screenshots/capture/lib/docborder.py" "$OUT"

echo "=== Wrote $OUT ==="
file "$OUT"
