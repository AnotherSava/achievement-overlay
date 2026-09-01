---
layout: default
title: Usage
nav_order: 3
has_children: true
---

# Usage

The app has no main window. It starts minimised to the system tray, watches for unlocks in the background, and shows a popup when one arrives. Everything you can change lives behind the [tray menu](usage/tray-menu).

## How it works

The Steam emulator keeps each game's achievement state in a JSON file under `%appdata%\GSE Saves\<app_id>\`, and rewrites it the moment the game reports an unlock. Achievement Overlay watches those files. A file that gains an earned achievement produces one popup, styled after Steam's own.

The popup's text — the achievement's name and description — normally comes from the game's own `steam_settings/achievements.json`, which is what the emulator itself reads. That is the file the [Add game](usage/adding-a-game) wizard generates.

Some emulators instead write the name and description straight into the unlock file. A game like that describes itself, and is tracked with no setup at all — see [Other emulators](usage/other-emulators).

Nothing here involves the game's process. The overlay never hooks, injects into, or reads memory from a running game, which is why it works with titles that refuse an in-game overlay.
