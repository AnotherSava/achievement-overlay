---
layout: default
title: Configuration
parent: Usage
nav_order: 3
---

# Configuration

Every setting below has a field in the [Settings](settings) window, which is the easiest way to change one. The `config.json` file that ships next to the executable can also be edited by hand.

Most keys are read live: `language`, `font`, `scale`, `notificationPosition`, `notificationBackground`, `soundEnabled`, `soundPath`, `displayDuration`, `useGameOverlaySettings` and `recentAchievementsCount` are picked up on the next popup. The remaining three — `gamesPaths`, `gseSavesPaths` and `recentAchievementsShortcut` — are bound at startup, so a hand edit to those needs a restart. Saving from the Settings window applies all of them at once either way.

## Keys

| Setting | Description | Default |
|---|---|---|
| `gamesPaths` | Semicolon-separated list of directories to scan for games with `steam_appid.txt` (in the game root or inside `steam_settings/`). May be left empty if all your games are tracked via [Other emulators](other-emulators), but the key itself must be present. | `C:\Games` |
| `gseSavesPaths` | Semicolon-separated list of GSE Saves directories. Supports `%appdata%` and other env vars. | `%appdata%\GSE Saves` |
| `language` | Preferred language for achievement display text (**Achievement text** in the settings window). Falls back to english. | `english` |
| `font` | Font family for the popup's name, description and game line. Empty uses the built-in default; an unavailable family falls back to it too. | `Segoe UI` |
| `scale` | How wide the popup is drawn: `"15%"` is a share of the display's width, `"384px"` an absolute width. Clamped to a readable range either way, and never below the popup's design width. | `15%` |
| `notificationPosition` | Which corner or edge of the display popups appear at: `bot_right`, `bot_center`, `bot_left`, `top_right`, `top_center` or `top_left` — the same spellings GBE's `PosAchievement` uses. Anything else reads as `bot_right`. | `bot_right` |
| `notificationBackground` | The colour behind the popup's text, as `#AARRGGBB` (`#RRGGBB`, `#ARGB` and `#RGB` are also accepted, and the `#` is optional). The text colours are derived from it. Opacity is clamped to at least `66` so the popup cannot be made invisible. | `#DD1A1A2E` |
| `soundEnabled` | Play a sound on achievement unlock. | `true` |
| `soundPath` | Custom `.wav` sound file path. Empty is the **Built-in sound** choice in the dialog. | (empty) |
| `displayDuration` | How long the unlock notification stays on screen, in seconds. | `7` |
| `useGameOverlaySettings` | Let a game's own `steam_settings/` override the unlock sound, duration and font for that game. See [Per-game settings](per-game-settings). | `true` |
| `recentAchievementsShortcut` | Global keyboard shortcut to show/hide recent achievements. | `Ctrl+Shift+H` |
| `recentAchievementsCount` | Number of recent achievements to display. | `5` |
| `steamWebApiKey` | Steam Web API key used by [Add game…](adding-a-game). Set via the wizard or the Settings window; you rarely edit it by hand. | (none) |
| `firecrawlApiKey` | Optional [Firecrawl](https://firecrawl.dev) API key, used to fetch hidden-achievement descriptions from SteamDB. | (none) |

## Example

```json
{
  "gamesPaths": "C:\\Games;D:\\Games",
  "gseSavesPaths": "%appdata%\\GSE Saves",
  "language": "english",
  "font": "Segoe UI",
  "scale": "15%",
  "notificationPosition": "bot_right",
  "notificationBackground": "#DD1A1A2E",
  "soundEnabled": true,
  "soundPath": "",
  "displayDuration": 7,
  "useGameOverlaySettings": true,
  "recentAchievementsShortcut": "Ctrl+Shift+H",
  "recentAchievementsCount": 5
}
```
