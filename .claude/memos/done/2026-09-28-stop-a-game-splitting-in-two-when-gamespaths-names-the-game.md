---
created: 2026-09-27 19:38:27
---

# Stop a game splitting in two when gamesPaths names the game's own folder

GameCache.ScanDirectory groups a scan's finds by appid and first-level game folder: the deepest configured root containing the game, plus the first folder name below it (GameCache.NameGame). When a gamesPaths entry is the game's own folder rather than its parent, that rule splits one install in two. Take `D:\Games\A` configured as a root, holding `steam_appid.txt` at its top and a second `steam_settings` copy under `bin\coldclient` (the repack layout CLAUDE.md's per-game-settings notes call common). The root copy is filed under folder `D:\Games\A`, named "A". The nested copy is filed under `D:\Games\A\bin`, named "bin". Two GameInfo entries result for one appid:
- Their SettingsDirs are not pooled, so a sound or font living only in the other copy is not found.
- `_cache[appid]` keeps whichever group came last, so the popup's game line and the schema's source depend on dictionary order and can read "bin".

The behaviour predates the FolderPath change: HEAD before it split the same way, as "." against "bin" (found by the fix-pass verifier, 2026-09-27).

How a config gets there: the Add game wizard adds the game's parent (GamesPathPlanner), so only a folder picked by hand in Settings, or typed into config.json, is the game itself. That is a plausible thing for a user to do.

Options for the owner:
(a) When a configured root itself holds a steam_appid.txt (at its top or in its steam_settings), treat the whole root as that game's folder, so every copy under it with the same appid joins one group.
(b) More generally, group a find under the nearest ancestor, at or below its root, that holds a steam_appid.txt for the same appid.
Either way, add a GameCacheTests case with exactly this layout: SettingsDirs holds both folders and GameName is "A".
