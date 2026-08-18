# GBE reference

Background on how the Goldberg Steam Emulator stores achievements, for anyone configuring a game
by hand or working out why the overlay stays quiet. The [Add game…](../README.md#adding-a-game)
wizard handles all of this for you — nothing here is required reading for normal use.

## Where unlocks are stored

The emulator writes one folder per game, named after the Steam AppID:

| Emulator | Path |
|---|---|
| GBE (current) | `%appdata%\GSE Saves\<app_id>\` |
| Goldberg (older builds, and some cracks) | `%appdata%\Goldberg SteamEmu Saves\<app_id>\` |

The overlay watches `%appdata%\GSE Saves` by default. If a game is unlocking achievements but nothing
appears, check whether it is writing to the older Goldberg path instead, and either point
`gseSavesPaths` at that folder too or move the game onto a current GBE build.

A folder named `4294967295` means the emulator never got a valid AppID — `steam_appid.txt` is missing
or malformed. Fix the AppID and the next unlock writes to the right folder.

The `achievements.json` inside these folders is the *unlock* file: which achievements are earned and
when. It is created on the first unlock or stat write, **not** when the game launches, so its absence
before your first achievement means nothing is wrong.

## How unlocks get recorded

The emulator records an achievement only when the game calls Steam's `SetAchievement()` — at the
moment it is earned. There is no retroactive detection, so achievements you earned before setting the
game up will not appear, and neither will progress held in the game's own save files. The counter
starts from the moment the emulator is in place.

The game also needs its achievement schema (`steam_settings/achievements.json`, described below)
present for tracking to work at all.

## The two GBE builds

A GBE release ships the Steam API replacement in two variants:

- **Regular** (~7 MB) — no overlay code at all, and it ignores `configs.overlay.ini` entirely
- **Experimental** (~22 MB) — includes GBE's own in-game overlay and achievement popups

This app is built to replace the experimental overlay, so the wizard installs the **regular** build
and sets `enable_experimental_overlay=0`. That is not only a preference: some games treat any
rendering hook as tampering. Rockstar/Double Eleven titles such as Red Dead Redemption fail with
**error 25D11007** ("closed unexpectedly, possibly due to a third party overlay tool") when the
experimental build is present — regardless of the overlay setting, because it is the hook itself that
is detected. Achievement tracking is unaffected; only in-game popups are lost, which is exactly the
gap this app fills.

For the same reason, a GBE install should not include `steamclient64.dll`, `steamclient.dll`, or
`GameOverlayRenderer64.dll` unless a specific game needs them.

## The achievement schema format

Each configured game has a `steam_settings/achievements.json` next to its `steam_api64.dll`. This is
the *schema* — the display text and icons for every achievement in the game, unlocked or not. It is a
JSON array:

```json
[
  {
    "name": "Achievement_0",
    "displayName": "Tower Tussle",
    "description": "Complete the tower challenge",
    "hidden": "0",
    "icon": "images/Achievement_0.jpg",
    "icongray": "images/Achievement_0_gray.jpg",
    "progress": {
      "value": { "operation": ">=", "operand1": "stat_name" },
      "min_val": "0",
      "max_val": "100"
    }
  }
]
```

| Field | Meaning |
|---|---|
| `name` | The internal Steam API name. Required — an entry without it is discarded, and it is the key the unlock file matches on |
| `displayName`, `description` | Either a string or a per-language object, e.g. `{"english": "…", "german": "…"}` |
| `hidden` | `"0"` for a normal achievement, `"1"` for a secret one |
| `icon`, `icongray` | Unlocked and locked icon paths, relative to `steam_settings/` |
| `progress` | Optional, for stat-based achievements: `operand1` names the stat, `operation` is the comparison, and `min_val`/`max_val` bound the progress bar |

Icon paths differ by the tool that produced the config, and both work because the emulator simply
reads whatever path is written:

- The **Add game… wizard** and `generate_emu_config` write `img/` with `<name>_1.png` (unlocked) and `<name>_0.png` (locked)
- Some other generators write `images/` with `<name>.jpg` and `<name>_gray.jpg`

If notifications show the default icon rather than the real one, the paths in the JSON and the files
on disk have drifted apart — see [Troubleshooting](../README.md#notification-shows-default-icon-instead-of-achievement-icon).

## Why hidden descriptions come back blank

Steam's public `GetSchemaForGame` API redacts the `description` field for any achievement marked
`hidden=1`. Every tool that builds a config from that API — the wizard, `generate_emu_config`, and
configs shipped by third parties — inherits the gap and leaves "No description available" behind. A
config being complete by file count says nothing; scan the JSON for placeholders.

The text is not hiding in the game's files either. For Unreal Engine 5 titles the achievement assets
are metadata stubs holding an icon path, a hidden flag and an ID; the strings are fetched from Steam
at runtime through `GetAchievementDisplayAttribute`. Extracting the game's packages finds nothing.

Two places do have the real text:

- **SteamDB** — `https://steamdb.info/app/<app_id>/stats/` lists hidden descriptions inline. This is
  what the wizard reads through [Firecrawl](https://firecrawl.dev); the site sits behind Cloudflare,
  so a plain HTTP request gets a 403. Each achievement appears as its display name, then
  `Hidden achievement: <description>`, then its completion percentage, then the API name — match rows
  back to the JSON on that API name, never on display order.
- **A legitimate local Steam install** — `Steam/appcache/stats/UserGameStatsSchema_<app_id>.bin` is a
  binary KeyValues blob Steam writes after the real client pulls a game's schema, and it contains
  every string including the hidden ones.

Community achievement guides paraphrase rather than quote, so they are a last resort if you want the
text to match what Steam would have shown.
