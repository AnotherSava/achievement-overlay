# Scanner: support GBE's `steam_settings/steam_appid.txt` placement

## Problem

`GameCache.ScanGames` finds `steam_appid.txt` recursively and treats `Path.GetDirectoryName(appIdFile)` as the game root. This breaks when `generate_emu_config` (the standard GBE config tool) places `steam_appid.txt` *inside* `steam_settings/` — the most common real-world layout.

GBE itself accepts `steam_appid.txt` in two locations:
1. `<gameDir>/steam_appid.txt` (game root)
2. `<gameDir>/steam_settings/steam_appid.txt` (inside steam_settings)

`generate_emu_config` writes #2. Today the scanner only handles #1.

## Symptoms

For a game configured purely via `generate_emu_config` (only the inner `steam_appid.txt` exists):
- Scanner finds `<gameDir>/steam_settings/steam_appid.txt`, sets `gameDir = <gameDir>/steam_settings`
- Looks for `<gameDir>/steam_settings/steam_settings/achievements.json` — doesn't exist
- Logs `[WARN] Skipped: appid=... at '<gameDir>/steam_settings' (no 'achievements.json')` and the game is silently dropped

Workaround currently used in the track-achievements skill: hand-create `<gameDir>/steam_appid.txt` so the scanner finds the outer one. This makes the inner one redundant and produces a duplicate WARN line on every scan.

## Fix (Option A — collapse `steam_settings/` parent)

In `GameCache.cs`, after `var gameDir = Path.GetDirectoryName(appIdFile)!;`, add:

```csharp
if (string.Equals(Path.GetFileName(gameDir), "steam_settings", StringComparison.OrdinalIgnoreCase))
    gameDir = Path.GetDirectoryName(gameDir)!;
```

### Behavior matrix

| Layout | Before fix | After fix |
|---|---|---|
| Only `<gameDir>/steam_appid.txt` | ✓ cached | ✓ cached (unchanged) |
| Only `<gameDir>/steam_settings/steam_appid.txt` | ✗ skipped (WARN) | ✓ cached |
| Both files exist | ✓ cached + duplicate WARN | ✓ cached, no warning (idempotent overwrite — same appid → same metadataPath) |

## Why not Option B (invert the search)

Scanning for `steam_settings/achievements.json` directly and deriving the appid from `steam_settings/steam_appid.txt` is conceptually cleaner, but:
- Breaks the existing test fixtures (all create `gameDir/steam_appid.txt` as the marker)
- Loses the affordance of marking any folder with just `steam_appid.txt`
- Larger change surface for the same outcome

## Tasks

- [ ] Add the parent-collapse to `GameCache.cs`
- [ ] Add a test in `GameCacheTests.cs` for the inner-only placement (mirror the existing fixture but place `steam_appid.txt` inside `steam_settings/`)
- [ ] Add a test for the both-files-present case asserting only one cache entry and no skip warning
- [ ] Update `README.md` "scan for games with `steam_appid.txt`" line to mention either location
- [ ] Update `GameCache.cs` class summary comment (line 18) similarly
- [ ] Once shipped, remove the hand-create-outer-`steam_appid.txt` workaround from the track-achievements Claude Code skill — no longer needed once the scanner accepts the inner placement
