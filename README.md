# Achievement Overlay

A Windows background app that displays Steam-like achievement popup notifications for games configured with the GBE/GSE Steam emulator.

## How it works

Achievement Overlay runs in the system tray and monitors `%appdata%/GSE Saves/` for achievement unlocks. When a game writes a new achievement to `achievements.json`, the app looks up the display name, description, and icon from the game's `steam_settings/achievements.json` metadata and shows a transparent overlay notification at the bottom-right of the foreground window — no game process interaction, purely filesystem-based.

## Features

- **Transparent overlay popup** — dark rounded rectangle with achievement icon, name, and description, positioned over the game window
- **Recent achievements display** — press `Ctrl+Shift+H` (configurable) to see the N most recent achievements stacked vertically with a cascade slide-in animation, with game name and timestamp for each. Press again or Esc to dismiss
- **Automatic game detection** — scans configured directories for `steam_appid.txt` and caches app ID to metadata mappings
- **Notification queue** — multiple simultaneous unlocks display one at a time with a short gap
- **Multi-monitor support** — notifications appear on the monitor with the foreground window, with correct DPI scaling across mixed-DPI setups
- **Configurable** via `config.json` (auto-generated on first run)
- **Unlock sound** — plays a default sound on unlock, or a custom `.wav` file
- **Pause notifications** from the tray menu — temporarily suppresses popups without exiting
- **Start with Windows** option in the tray menu
- **Single instance** — only one copy of the app can run at a time

## Building from source

**Prerequisites:** Windows 10+, [.NET 10 SDK](https://dotnet.microsoft.com/download)

```
dotnet build src/AchievementOverlay.csproj
dotnet test tests/AchievementOverlay.Tests.csproj
```

The built executable will be in `src/bin/Debug/net10.0-windows/`.

## Configuration

On first run, a `config.json` file is created next to the executable with sensible defaults. `soundEnabled`, `soundPath`, and `displayDuration` are picked up automatically on change. Changing `gseSavesPath` or `gamesPaths` requires a restart.

### Settings

| Setting | Description | Default |
|---|---|---|
| `gseSavesPath` | Path to GSE Saves directory. Supports `%appdata%` and other env vars. | `%appdata%\GSE Saves` |
| `gamesPaths` | Semicolon-separated list of directories to scan for games with `steam_appid.txt`. | `C:\Games` |
| `language` | Preferred language for achievement display text. Falls back to english. | `english` |
| `soundEnabled` | Play a sound on achievement unlock. | `true` |
| `soundPath` | Custom `.wav` sound file path. Empty uses the built-in default. | (empty) |
| `displayDuration` | How long the unlock notification stays on screen, in seconds. | `7` |
| `recentAchievementsShortcut` | Global keyboard shortcut to show/hide recent achievements. | `Ctrl+Shift+H` |
| `recentAchievementsCount` | Number of recent achievements to display. | `5` |

### Example config

```json
{
  "gseSavesPath": "%appdata%\\GSE Saves",
  "gamesPaths": "C:\\Games;D:\\Games",
  "language": "english",
  "soundEnabled": true,
  "soundPath": "",
  "displayDuration": 7,
  "recentAchievementsShortcut": "Ctrl+Shift+H",
  "recentAchievementsCount": 5
}
```

## System tray menu

Right-click the tray icon for these options:

- **Show Recent Ctrl+Shift+H** — display the N most recent achievements stacked vertically with game name and timestamp. Press again or Esc to dismiss.
- **Sound Enabled** — toggle notification sound (persists to `config.json`)
- **Pause Notifications** — suppress popups while checked (resets on restart)
- **Start with Windows** — add/remove from Windows startup via registry
- **Open Config Location** — opens Explorer with `config.json` selected
- **Exit** — stops watching and exits the app

## License

[GPL-3.0](LICENSE)
