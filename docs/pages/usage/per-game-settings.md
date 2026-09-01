---
layout: default
title: Per-game settings
parent: Usage
nav_order: 5
---

# Per-game settings

Some games arrive with a `steam_settings/` folder someone else prepared — a repack, a scene release, or an older config generator — and that folder can already say how notifications should look and sound. When **Use each game's own overlay settings** is on (it is by default), three of those choices are honoured for that game's popups.

| What the game supplies | Where | Effect |
|---|---|---|
| Unlock sound | `steam_settings/sounds/overlay_achievement_notification.wav` | Played instead of the app's sound |
| Display duration | `Notification_Duration_Achievement` in `steam_settings/configs.overlay.ini` | How long the popup stays, 1–60 s |
| Font | `Font_Override` in the same file, resolved against `steam_settings/fonts` | The popup's font family |

Anything the game does not set stays on the [Settings](settings) value, and a game that supplies none of the three is unaffected. **Play a sound on unlock** stays in charge: with it off, nothing plays, whatever a game ships. A game's sound file that cannot be played falls back to the built-in one rather than to silence.

## What is deliberately ignored

Position (`PosAchievement`), colours, rounding, margins, and font and icon *sizes* are not read from a game's config.

Position and background colour are yours to set app-wide under [Settings](settings) instead — a game's ini quietly moving or recolouring a popup you just placed deliberately would be worse than not reading it, and GBE measures its position against the game's render surface where this app measures against the display, so the same key would not mean the same thing. GBE's sizes are likewise measured against a differently shaped notification, so copying the numbers across would not reproduce the layout; use **Popup size**.

Nothing in a game's config can suppress a notification either, even though the equivalent key does exactly that in GBE's own overlay.

## Where the files are read from

A game often has **more than one** `steam_settings` folder — a repack drops a decorated copy at the game root while the emulator reads a plainer one beside its DLL (`bin/coldclient/`, `www/greenworks/lib/`). All of them are read, nearest-the-DLL first, so a sound or font that lives only in the copy the emulator ignores is still used. Hidden folders count: repacks mark them hidden often, and both the tracking scan and this one look inside them.

This only applies to games the app can locate on disk, which means a `steam_appid.txt` and a folder covered by `gamesPaths`. A game tracked purely through a [self-describing unlock file](other-emulators) has no such folder, so there is nothing to read.

The values are re-read when the files change, so editing an ini takes effect on the next unlock without restarting the app. The log names what was picked up:

```
Game overlay settings: duration=12s, sound='…' (from '…')
```
