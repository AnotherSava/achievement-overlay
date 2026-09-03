---
layout: default
title: Home
nav_order: 1
---

*Steam-style achievement popups for games running in the Goldberg Steam Emulator.*

A Windows background app that sits in the system tray. The emulator records every unlock in a JSON file; Achievement Overlay watches those files and slides a Steam-style popup over the game the moment one changes. It never touches the game process — no hooking, no injection — so it works even with games that reject an in-game overlay, such as Red Dead Redemption.

<a href="screenshots/sample-notification.png"><img src="screenshots/sample-notification.png" alt="Achievement notification" width="1000"></a>

## Features

- **Steam-style notifications** — achievement icon, name and description slide in at the corner of the display you choose
- **Adjustable popup** — set its position, width, background colour, opacity and font, with a **Show me** preview. Text colours are worked out from the background you pick, so a light popup flips to dark text rather than becoming unreadable
- **Recent achievements** — press Ctrl+Shift+H to review the last few unlocks, and to check the overlay is alive
- **Add game wizard** — generates a game's achievement metadata from the Steam Web API, filling in hidden descriptions from SteamDB
- **Automatic game detection** — scans the folders you name for games carrying achievement metadata
- **Other emulators** — a game whose unlock file carries its own achievement names is tracked with no configuration at all
- **Setup confirmation** — a one-time "Gearhead" popup confirms tracking works before the first real unlock
- **Multi-monitor support** — notifications appear on the monitor holding the foreground window, with correct DPI scaling across mixed-DPI setups
- **Unlock sound** — the built-in sound, or a `.wav` of your own
- **Per-game settings** — a game that arrived with its own `steam_settings/` can supply its unlock sound, display duration and font, for that game only
- **Settings window** — every option has a field, and saving applies it straight away without a restart
- **Report a problem** — collects one game's settings, log and achievement files into a single file you read before attaching it to an issue, with your other games and any API keys left out
- **Start with Windows** option

## Next steps

- **[Installation](pages/installation)** — download a release, or build it yourself
- **[Usage](pages/usage)** — the tray menu, the settings window, adding a game, and what other emulators need
- **[Troubleshooting](pages/troubleshooting)** — reading the log when nothing appears
- **[Developer guide](pages/development)** — building from source, project layout, and how the emulator stores achievements
- **[Privacy](pages/privacy)** — what leaves your machine, and when
