---
layout: default
title: Troubleshooting
nav_order: 4
---

# Troubleshooting

The app writes a log file, `overlay.log`, next to the config file — the tray menu's **Open config/logs location** takes you there. Almost everything below is diagnosed from it, so start by looking for `[WARN]` and `[ERROR]` lines.

## The app will not start

The app shows an error dialog on startup if the config is missing, has invalid JSON, or has invalid settings. Click **Details** to see the full log. Common causes:

- **Config file not found** — make sure `config.json` is in the same folder as the executable. Re-extract it from the release archive if needed.
- **Invalid JSON** — fix the syntax in `config.json`, using the [example config](usage/configuration#example) as a reference.
- **Invalid settings** — required fields like `gseSavesPaths`, `gamesPaths`, `displayDuration` or `recentAchievementsCount` may be missing or hold invalid values.
- **GSE Saves directory does not exist** — check that `gseSavesPaths` points to valid directories (default `%appdata%\GSE Saves`). Non-existent paths are logged as warnings and skipped; the app exits only if none are valid.
- **No games with achievement metadata found** — a `[WARN]`, not fatal, since a game may still be tracked via [Other emulators](usage/other-emulators). Check that `gamesPaths` points to directories containing games with `steam_appid.txt` and `steam_settings/achievements.json`, and generate metadata with [Add game…](usage/adding-a-game) if needed.

## A game is not found

`[WARN] Game path does not exist` means `gamesPaths` names a directory that is not there.

If the game does not appear in the log at all, make sure its directory sits under one of the paths in `gamesPaths` and that it has a `steam_appid.txt` — either in the game root or inside `steam_settings/`. A hidden `steam_settings` folder is fine; those are scanned too.

`[WARN] Skipped: appid=... (no 'achievements.json')` means the game is detected but has no achievement metadata. Generate it with [Add game…](usage/adding-a-game).

`[WARN] No games with achievement metadata found` means none were found anywhere. The app keeps running, but only games tracked via [Other emulators](usage/other-emulators) will produce notifications.

## An achievement unlocked but nothing appeared

Check that `%appdata%\GSE Saves\<app_id>\achievements.json` exists and lists the unlock. If it does not, the emulator never recorded it and the overlay had nothing to show:

- Older emulator builds and some cracks write to `%appdata%\Goldberg SteamEmu Saves\` instead. Add that folder to `gseSavesPaths`.
- A folder named `4294967295` means the emulator never got a valid AppID — check `steam_appid.txt`.
- Achievements earned **before** you configured the game are never backfilled. The emulator only records an unlock at the moment the game reports it.

More detail in the [GBE reference](development/gbe-reference#where-unlocks-are-stored).

## The notification shows the default icon

The icon path in the game's `steam_settings/achievements.json` does not match an actual file. Check that the `icon` field (e.g. `"img/abc123.jpg"`) points to an existing file relative to the `steam_settings/` directory. The [schema format](development/gbe-reference#the-achievement-schema-format) lists the layouts different generators produce.

For a game tracked through a non-GBE emulator, the default icon also means no schema was found for it — see [Getting icons and the game name back](usage/other-emulators#getting-icons-and-the-game-name-back).

A third cause is that the emulator and the schema spell the same achievement differently, so the schema entry is never found. Compare a name in `%appdata%\GSE Saves\<app_id>\achievements.json` with the `name` fields in the game's `steam_settings/achievements.json`. Differences in capitalisation and in leading zeros are bridged automatically; anything else — a prefix the emulator adds or drops, a different separator — is not, and there is nothing to configure for it. If the log says an achievement `matches both '...' and '...' in the schema once leading zeros are ignored`, the schema lists that number under two spellings and the overlay declines to guess between them; delete the one that does not belong.

## Wrong language

The log shows `[WARN] Language '...' not available, falling back to english`. Pick a different **Achievement text** language in [Settings](usage/settings) — the list offers the ones your installed games actually carry, though a single game can still be missing any of them.

## The hotkey does nothing

If you pick a shortcut another application already owns, the [Settings](usage/settings) window says so when you save; the log also shows `[WARN] Could not register hotkey`. Pick a different combination under **Shortcut**. The tray menu item still works as a fallback.

## Notifications are too small

Raise **Popup size** in [Settings](usage/settings) — it scales the text along with everything else. The default is 15% of the screen width, which on a 1080p display works out to the smallest size the popup will draw; anything above that gives you bigger text.

## No sound

Check that **Play a sound on unlock** is on in [Settings](usage/settings). With **Custom file** selected, `[WARN] Sound file not found` means it has since been moved or deleted, and `[WARN] Could not play sound` means it is not a valid PCM `.wav`. In both cases no sound plays — switch to **Built-in sound**.

If one game sounds different from the rest, it is supplying its own — see [Per-game settings](usage/per-game-settings).

## Settings are not saving

If a change made in the [Settings](usage/settings) window does not persist, the log shows `[WARN] Config file is malformed, could not update` or `[WARN] Could not write config`. Fix the JSON syntax in `config.json`, or check file permissions on it.

## Still stuck?

[Create a GitHub issue](https://github.com/AnotherSava/achievement-overlay/issues/new) describing the problem, and attach your log file. Misleading documentation is worth reporting too.

This is a hobby project, built to work around Red Dead Redemption's incompatibility with gbe_fork's built-in overlay. It may not work in every situation, but if it helps you as much as it helped me, it was definitely worth the effort.
