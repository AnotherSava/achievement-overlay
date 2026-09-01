---
layout: default
title: Other emulators
parent: Usage
nav_order: 6
---

# Other emulators

The overlay is built around GBE, but it tracks any emulator that writes a GSE-Saves-style `achievements.json` — one JSON object per achievement, keyed by achievement name, with an `earned` flag and an `earned_time`. Both `true`/`false` and `1`/`0` are accepted for `earned`.

Where such an emulator also writes `displayName` and `description` into each entry — the Goldberg Uplay R2 emulator does, when pointed at the GSE Saves folder — the unlock file describes itself, and the game needs **no `steam_settings/` folder, no `steam_appid.txt`, and no entry in `gamesPaths`**. Point the emulator's achievement output at `%appdata%\GSE Saves\<id>\` and it is tracked on the next unlock.

Two limitations apply to these games, both because the emulator provides nothing to work with:

- **No achievement icons** — notifications use the default icon.
- **No game name** — the recent achievements panel labels the game with the folder's id.

## Getting icons and the game name back

Both limitations lift if the game also has a GBE-style config, because the overlay reads the schema first and treats the inline text as the fallback. Put a `steam_settings/` folder (with `achievements.json` and its `achievement_images/`) and a `steam_appid.txt` holding the same id as the GSE Saves folder into the game's own folder, and make sure `gamesPaths` covers it. Notifications then use the schema's icons and text, and the recent achievements panel shows the game's name.

This works where the emulator emits the same achievement names as the Steam schema — for the Uplay R2 emulator that is what its `AchKeyPrefix` setting is for. Names the schema does not define fall back to the unlock file's own text, and so does any single field the schema leaves blank (hidden achievements often have no description), so a partial match is fine. The config is picked up on that game's next unlock; restart the overlay if it has already shown notifications for it.

Leading zeros are the one difference the overlay bridges by itself. Some Steam games number their achievements `001`, `002`, … while an emulator handed a bare id writes `1`, `2`, … — Assassin's Creed Odyssey and Origins are both like this, and no emulator setting can add the padding. Where a name is written entirely in digits on both sides, the two match once leading zeros are ignored, so those games get their icons with no hand-editing. Because that is a guess about which achievement a number means rather than a name the schema states, such a match adds the icon and fills anything the unlock file left blank, but leaves the text the unlock file already carries alone.

The [Add game…](adding-a-game) wizard is Steam-only: it works by replacing the game's Steam library with GBE's, which does not apply to other emulators. Use it on the Steam version of the game — or any GBE config generator — to produce the `steam_settings/` folder, then copy that folder across.
