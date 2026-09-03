# Achievement Overlay

[![Build](https://github.com/AnotherSava/achievement-overlay/actions/workflows/build.yml/badge.svg)](https://github.com/AnotherSava/achievement-overlay/actions/workflows/build.yml)

*Steam-style achievement popups for games running in the Goldberg Steam Emulator.*

A Windows background app that sits in the system tray. The emulator records every unlock in a JSON file; Achievement Overlay watches those files and slides a Steam-style popup over the game the moment one changes. It never touches the game process — no hooking, no injection — so it works even with games that reject an in-game overlay, such as Red Dead Redemption.

![Achievement notification](docs/screenshots/sample-notification.png)

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

## License

[GPL-3.0](LICENSE)

---

See full project documentation at **[anothersava.github.io/achievement-overlay](https://anothersava.github.io/achievement-overlay/)**:

- [Installation](https://anothersava.github.io/achievement-overlay/pages/installation)
- [Usage](https://anothersava.github.io/achievement-overlay/pages/usage)
  - [Tray menu](https://anothersava.github.io/achievement-overlay/pages/usage/tray-menu)
  - [Settings](https://anothersava.github.io/achievement-overlay/pages/usage/settings)
  - [Configuration](https://anothersava.github.io/achievement-overlay/pages/usage/configuration)
  - [Adding a game](https://anothersava.github.io/achievement-overlay/pages/usage/adding-a-game)
  - [Per-game settings](https://anothersava.github.io/achievement-overlay/pages/usage/per-game-settings)
  - [Other emulators](https://anothersava.github.io/achievement-overlay/pages/usage/other-emulators)
  - [Playnite](https://anothersava.github.io/achievement-overlay/pages/usage/playnite)
- [Troubleshooting](https://anothersava.github.io/achievement-overlay/pages/troubleshooting)
- [Development](https://anothersava.github.io/achievement-overlay/pages/development)
  - [GBE reference](https://anothersava.github.io/achievement-overlay/pages/development/gbe-reference)
- [Privacy](https://anothersava.github.io/achievement-overlay/pages/privacy)
