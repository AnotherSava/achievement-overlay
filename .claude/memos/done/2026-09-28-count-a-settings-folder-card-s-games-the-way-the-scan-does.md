---
created: 2026-09-27 19:38:29
---

# Count a Settings folder card's games the way the scan does

Each games-folder card in Settings shows a live status line from SettingsWindow.DescribeGameFolder. It counts `steam_appid.txt` files under the folder (`Directory.EnumerateFiles(path, "steam_appid.txt", AppUtilities.RecursiveScan).Count()`) and labels the number "N games with achievement metadata". Two differences from the real scan make the number wrong:
- A game carrying `steam_appid.txt` both at its root and inside `steam_settings/`, or in two `steam_settings` copies, is counted once per file.
- A `steam_appid.txt` with no `achievements.json` beside it is counted, although GameCache skips it with a Warn.

Measured on the maintainer's machine, 2026-09-27: `C:\Games` holds 14 `steam_appid.txt` files and GameCache caches 9 games from it. Three games carry two copies each, one has a `_crack` copy without `achievements.json`, and one game has no `achievements.json` at all. The card therefore says 14 where the log and the Recent panel know 9.

Next step: give the card the scan's own count rather than a second, simpler rule. The Single Source of Logic rule applies; a card and the cache must not disagree about what a game is. For example, factor the part of GameCache.ScanDirectory that turns a root into grouped games into something both can call, or have the card read the counts from a GameCache scan of that folder. Mind the Settings window's responsiveness: the card is computed when the list is drawn, and a full grouped scan reads each find's folder (see the open memo about moving the rescan out of the Settings window).
