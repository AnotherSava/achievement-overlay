# Achievement Overlay

[![Build](https://github.com/AnotherSava/achievement-overlay/actions/workflows/build.yml/badge.svg)](https://github.com/AnotherSava/achievement-overlay/actions/workflows/build.yml)

A Windows background app that displays Steam-like achievement popup notifications for games running in [Goldberg Steam Emulator](https://github.com/Detanup01/gbe_fork) — and in other emulators that write their unlocks to the same GSE Saves folder.

<img src="docs/screenshots/sample-notification.png" alt="Achievement notification">

## How it works

The Steam emulator stores achievement data in JSON files and updates them as soon as the next one gets unlocked. Achievement Overlay monitors these files and notifies the user with Steam-style pop-up notifications.

Achievement names and descriptions normally come from the game's own `steam_settings/achievements.json`. Some emulators instead write that text straight into the unlock file — those games are tracked with no setup at all, as described under [Other emulators](#other-emulators).

## Features

- **Steam-style notifications** — achievement icon, name, and description slide in at the bottom-right of the game window
- **Non-invasive** — works even with particularly sensitive games like Red Dead Redemption
- **Recent achievements** — press Ctrl+Shift+H (shortcut is configurable) to review recent achievements. Also the easiest way to test that the overlay is working. Press again or Esc to dismiss
- **Automatic game detection** — scans configured directories for games with achievement metadata
- **Other emulators** — a game whose unlock file carries its own achievement names is tracked with no configuration at all (see [Other emulators](#other-emulators))
- **Setup confirmation** — a one-time "Gearhead" popup confirms tracking is working, either when a newly configured game first runs or as soon as you add a game that has run before (shown only while the game has no unlocks yet, so it never masks a real first achievement)
- **Multi-monitor support** — notifications appear on the monitor with the foreground window, with correct DPI scaling across mixed-DPI setups
- **Unlock sound** — plays a default or user-defined sound on achievement unlock
- **Per-game settings** — a game that arrived with its own `steam_settings/` can supply its unlock sound, display duration and font, used for that game only (see [Per-game settings](#per-game-settings))
- **Adjustable popup** — set how wide the notification is drawn (a share of the screen, or a fixed pixel width) and which font it uses, with a **Show me** preview
- **Configurable** — a [Settings](#settings) window covers every option, and saving applies it immediately
- **Start with Windows** option

## Installation

Download the latest release from [GitHub Releases](https://github.com/AnotherSava/achievement-overlay/releases). Choose one of the two options:

- **Self-contained** — single exe, just unzip and run (no dependencies, larger size)
- **Framework-dependent** — smaller download, requires [.NET Desktop Runtime 10](https://dotnet.microsoft.com/download/dotnet/10.0)

After extracting, check `gamesPaths` in [`config.json`](#configuration) — it should point to the directories where your games are installed.

You can also build the most recent (and potentially less stable) version [from source](#building-from-source).

## System tray menu

Right-click the tray icon for these options:

- **Show recent achievements** *(keyboard shortcut)* — display recent achievements. Press again or Esc to dismiss.
- **Add game…** — open the [Add game](#adding-a-game) dialog to generate achievement metadata for a game and start tracking it
- **Pause notifications** — suppress popups while checked (resets on restart)
- **Settings…** — open the [Settings](#settings) window
- **Open config/logs location** — opens Explorer with `config.json` selected (`overlay.log` is in the same folder)
- **Exit** — stops watching and exits the app

<img src="docs/screenshots/tray-menu.png" alt="System tray menu">

## Settings

**Settings…** in the tray menu opens a window covering every value in [`config.json`](#configuration), plus the Windows startup entry (which lives in the registry rather than the config). It follows your Windows light/dark setting and accent colour. Clicking **Save** writes only the settings that changed and applies them straight away: a new shortcut is re-registered, new **Game folders** trigger a rescan, and new **GSE Saves folders** restart the watcher. Nothing in here needs a restart.

<img src="docs/screenshots/settings.png" alt="Settings window">

Four pages:

- **General** — start with Windows, and the shortcut and count for the recent achievements panel.
- **Notifications** — everything about the popup: language, font, size, duration, sound, and whether a game's own settings may override them. **Show me** fires a real notification with the settings as they stand, and the footer states the popup's computed width and duration.
- **Folders** — game folders and GSE Saves folders, one card each, with a live status line saying what's actually there (how many games were found, or that a drive isn't connected).
- **Advanced** — the Steam Web API and Firecrawl keys.

A few fields are worth a note:

- **Achievement text** picks the language achievement names and descriptions appear in. The list holds the languages your installed games actually provide; a game that doesn't have the chosen one falls back to english.
- **Shortcut** is captured rather than typed — click it and press the combination you want. Backspace clears it, leaving **Show recent achievements** in the tray menu as the way in. While the field has focus, the combination you press is recorded instead of running whatever normally owns it, so you can reassign a shortcut that's already taken — by this app, by another program, or by a desktop shortcut's **Shortcut key**. Nothing is intercepted once you click away from the field.
- **Popup size** scales the whole popup — text, icon, padding and wrap width grow together — so this is the setting to reach for if notifications read too small. It never draws smaller than the popup's design size, so the text stays legible whatever unit you pick. Pick the unit: **% of screen width** keeps the popup the same apparent size on any monitor (the default 15% is what the overlay has always used), while **Pixels** pins it to one width everywhere. The footer states the width it actually works out to.
- **Game folders** and **GSE Saves folders** are edited a folder at a time with **Add folder**, **Change** and **Remove**. A folder you pick is stored with an environment variable where one fits — choosing your AppData GSE Saves folder is saved as `%appdata%\GSE Saves`, not as your user profile's full path — so the config stays portable between machines even after editing it here.
- **Use each game's own overlay settings** lets a game that arrived with a `steam_settings/` folder of its own supply its unlock sound, its display duration and its font, for that game only. See [Per-game settings](#per-game-settings).
- **Metadata providers** holds the two keys the [Add game](#adding-a-game) wizard asks for and reuses: the Steam Web API key that fetches achievement schemas and icons, and the optional Firecrawl key that fills in hidden-achievement descriptions.

**Pause notifications** isn't here: it's a momentary toggle rather than a setting, so it stays in the tray menu and a restart clears it.

Two entries are refused because they would fail silently: a **GSE Saves folders** list where none of the folders exist (the app won't start without one), and a **Custom file** sound that isn't there. Everything else is saved as entered.

## Configuration

Every setting below has a field in the [Settings](#settings) window, which is the easiest way to change one. The `config.json` file that ships next to the executable can also be edited by hand: `language`, `font`, `scale`, `soundEnabled`, `soundPath`, `displayDuration`, `useGameOverlaySettings`, and `recentAchievementsCount` are picked up automatically, while `gamesPaths`, `gseSavesPaths`, and `recentAchievementsShortcut` are only re-read on the next start. Saving from the window applies all of them at once.

### Settings

| Setting | Description | Default |
|---|---|---|
| `gamesPaths` | Semicolon-separated list of directories to scan for games with `steam_appid.txt` (in the game root or inside `steam_settings/`). May be left empty if all your games are tracked via [Other emulators](#other-emulators), but the key itself must be present. | `C:\Games` |
| `gseSavesPaths` | Semicolon-separated list of GSE Saves directories. Supports `%appdata%` and other env vars. | `%appdata%\GSE Saves` |
| `language` | Preferred language for achievement display text (**Achievement text** in the settings window). Falls back to english. | `english` |
| `font` | Font family for the popup's name, description and game line. Empty uses the built-in default; an unavailable family falls back to it too. | `Segoe UI` |
| `scale` | How wide the popup is drawn: `"15%"` is a share of the display's width, `"384px"` an absolute width. Clamped to a readable range either way, and never below the popup's design width. | `15%` |
| `soundEnabled` | Play a sound on achievement unlock. | `true` |
| `soundPath` | Custom `.wav` sound file path. Empty is the **Built-in sound** choice in the dialog. | (empty) |
| `displayDuration` | How long the unlock notification stays on screen, in seconds. | `7` |
| `useGameOverlaySettings` | Let a game's own `steam_settings/` override the unlock sound, duration and font for that game. See [Per-game settings](#per-game-settings). | `true` |
| `recentAchievementsShortcut` | Global keyboard shortcut to show/hide recent achievements. | `Ctrl+Shift+H` |
| `recentAchievementsCount` | Number of recent achievements to display. | `5` |
| `steamWebApiKey` | Steam Web API key used by [Add game…](#adding-a-game). Set via the wizard or the Settings window; you rarely edit it by hand. | (none) |
| `firecrawlApiKey` | Optional [Firecrawl](https://firecrawl.dev) API key, used to fetch hidden-achievement descriptions from SteamDB. | (none) |

### Example config

```json
{
  "gamesPaths": "C:\\Games;D:\\Games",
  "gseSavesPaths": "%appdata%\\GSE Saves",
  "language": "english",
  "font": "Segoe UI",
  "scale": "15%",
  "soundEnabled": true,
  "soundPath": "",
  "displayDuration": 7,
  "useGameOverlaySettings": true,
  "recentAchievementsShortcut": "Ctrl+Shift+H",
  "recentAchievementsCount": 5
}
```

## Adding a game

For the overlay to show anything, each game needs a `steam_settings/achievements.json` next to its `steam_api64.dll`. The app can generate this for you — right-click the tray icon and choose **Add game…** to launch a short wizard. It's a self-contained replacement for the unmaintained `generate_emu_config` tool.

The wizard only asks for what it can't work out on its own:

1. **Game folder** — pick the game's install folder. The wizard finds the Steam DLL below it (even nested deep in an Unreal Engine layout) and tries to detect the AppID.
2. **Steam AppID** — shown only if the AppID couldn't be detected from the game folder. If a Steam store search guessed one, it's pre-filled for you to verify.
3. **Steam Web API key** — shown only the first time (it's saved to `config.json` and reused afterwards). Required for the achievement schema; get one at [steamcommunity.com/dev/apikey](https://steamcommunity.com/dev/apikey). The key is stored unencrypted; if that's a concern, you can revoke it right after the game is added — it's only needed while adding a game, and you'll just enter a fresh key next time you add one.
4. **Hidden achievements** — shown only if the game actually has hidden achievements and you haven't already saved a Firecrawl API key. Steam blanks out the descriptions of secret achievements; the real text lives on SteamDB (behind Cloudflare), so the tool fetches it through [Firecrawl](https://firecrawl.dev), a hosted scraper. Paste a free Firecrawl API key, or leave it blank to skip (those descriptions stay as placeholders).
5. **Ready** — review the summary and options (back up the original DLL, and an **Advanced** section for the GBE release folder), then click **Add game**.

It then fetches the achievement icons from Steam, downloads the matching GBE release, backs up and replaces the Steam DLL, and writes a `steam_settings/` folder with GBE's own overlay disabled (this app replaces it) — showing live progress on the final page. When it finishes, the game's folder is added to `gamesPaths` if needed and the overlay starts tracking it immediately — no restart.

If a configuration already exists for the game, the wizard shows its location and asks before overwriting it.

Notes:

- **Denuvo games** — these won't load `steam_api64.dll`. If Denuvo is detected with no crack present, the tool stops before changing anything.
- **Windows Defender** — current GBE releases sometimes trigger a false positive on download. If that happens, the wizard offers to add the needed Defender exclusions (with a UAC prompt) and retries automatically. Alternatively, point the Advanced **GBE release folder** at an already-extracted release and uncheck "Download the latest GBE release".

The [GBE reference](docs/gbe-reference.md) covers what the wizard is doing underneath — where unlocks are stored, the achievement schema format, why the overlay disables GBE's own, and why hidden achievement descriptions arrive blank. You don't need any of it for normal use.

## Per-game settings

Some games arrive with a `steam_settings/` folder someone else prepared — a repack, a scene release,
or an older config generator — and that folder can already say how notifications should look and
sound. When **Use each game's own overlay settings** is on (it is by default), three of those choices
are honoured for that game's popups:

| What the game supplies | Where | Effect |
|---|---|---|
| Unlock sound | `steam_settings/sounds/overlay_achievement_notification.wav` | Played instead of the app's sound |
| Display duration | `Notification_Duration_Achievement` in `steam_settings/configs.overlay.ini` | How long the popup stays, 1–60 s |
| Font | `Font_Override` in the same file, resolved against `steam_settings/fonts` | The popup's font family |

Anything the game doesn't set stays on the [Settings](#settings) value, and a game that supplies none
of the three is unaffected. **Play a sound on unlock** stays in charge: with it off, nothing plays,
whatever a game ships. A game's sound file that can't be played falls back to the built-in one rather
than to silence.

Deliberately not honoured: position, colours, rounding, margins, and font/icon *sizes*. The popup's
look is this app's, not the emulator's, and GBE's sizes are measured against a differently shaped
notification, so copying the numbers across would not reproduce the layout — use **Popup size**
instead. Nothing in a game's config can suppress a notification either, even though the equivalent
key does exactly that in GBE's own overlay.

A game often has **more than one** `steam_settings` folder — a repack drops a decorated copy at the
game root while the emulator reads a plainer one beside its DLL (`bin/coldclient/`,
`www/greenworks/lib/`). All of them are read, nearest-the-DLL first, so a sound or font that lives only
in the copy the emulator ignores is still used. Hidden folders count: repacks mark them hidden often,
and both the tracking scan and this one look inside them.

This only applies to games the app can locate on disk, which means a `steam_appid.txt` and a folder
covered by `gamesPaths`. A game tracked purely through a [self-describing unlock file](#other-emulators)
has no such folder, so there is nothing to read.

The values are re-read when the files change, so editing an ini takes effect on the next unlock
without restarting the app. The log names what was picked up: `Game overlay settings: duration=12s,
sound='…' (from '…')`.

## Other emulators

The overlay is built around GBE, but it tracks any emulator that writes a GSE-Saves-style
`achievements.json` — one JSON object per achievement, keyed by achievement name, with an `earned`
flag and an `earned_time`. Both `true`/`false` and `1`/`0` are accepted for `earned`.

Where such an emulator also writes `displayName` and `description` into each entry — the Goldberg
Uplay R2 emulator does, when pointed at the GSE Saves folder — the unlock file describes itself, and
the game needs **no `steam_settings/` folder, no `steam_appid.txt`, and no entry in `gamesPaths`**.
Point the emulator's achievement output at `%appdata%\GSE Saves\<id>\` and it is tracked on the next
unlock.

Two limitations for these games, both because the emulator provides nothing to work with:

- **No achievement icons** — notifications use the default icon.
- **No game name** — the Recent-achievements panel labels the game with the folder's id.

### Getting icons and the game name back

Both limitations lift if the game also has a GBE-style config, because the overlay reads the schema
first and treats the inline text as the fallback. Put a `steam_settings/` folder (with
`achievements.json` and its `achievement_images/`) and a `steam_appid.txt` holding the same id as the
GSE Saves folder into the game's own folder, and make sure `gamesPaths` covers it. Notifications then
use the schema's icons and text, and the Recent panel shows the game's name.

This works only where the emulator emits the same achievement names as the Steam schema — for the
Uplay R2 emulator that is what its `AchKeyPrefix` setting is for. Names the schema doesn't define fall
back to the unlock file's own text, and so does any single field the schema leaves blank (hidden
achievements often have no description), so a partial match is fine. The config is picked up on that
game's next unlock; restart the overlay if it has already shown notifications for it.

The [Add game…](#adding-a-game) wizard is Steam-only: it works by replacing the game's Steam library
with GBE's, which does not apply to other emulators. Use it on the Steam version of the game (or any
GBE config generator) to produce the `steam_settings/` folder, then copy that folder across.

## Playnite

An emulator-tracked game launched through [Playnite](https://playnite.link) won't show achievements in
the SuccessStory plugin on its own, because the game carries no Steam AppID for the plugin to look up.
One line of configuration fixes it, and SuccessStory then reads unlock state from the same GSE Saves
files this overlay watches — see [Playnite and SuccessStory](docs/playnite.md).

## Troubleshooting

The app writes a log file (`overlay.log`) next to the config file (use the tray context menu to find it). Check it for diagnostic information. Look for `[WARN]` and `[ERROR]` entries.

### App won't start

The app shows an error dialog on startup if the config is missing, has invalid JSON, or has invalid settings. Click "Details" to see the full log. Common causes:

- **Config file not found** — make sure `config.json` is in the same folder as the executable. Re-extract it from the release archive if needed.
- **Invalid JSON** — fix the syntax in `config.json`. Use the [example config](#example-config) above as a reference.
- **Invalid settings** — required fields like `gseSavesPaths`, `gamesPaths`, `displayDuration`, or `recentAchievementsCount` may be missing or have invalid values.
- **GSE Saves directory does not exist** — check that `gseSavesPaths` points to valid directories (default: `%appdata%\GSE Saves`). Non-existent paths are logged as warnings and skipped; the app exits only if none are valid.
- **No games with achievement metadata found** — a `[WARN]`, no longer fatal, since a game may still be tracked via [Other emulators](#other-emulators). Check that `gamesPaths` points to directories containing games with `steam_appid.txt` and `steam_settings/achievements.json`. Generate metadata with the tray [Add game…](#adding-a-game) dialog if needed.

### Game is not found

If the log shows `[WARN] Game path does not exist`, check that `gamesPaths` in `config.json` points to valid directories.

If the game doesn't appear in the log at all, make sure its directory is under one of the paths listed in `gamesPaths` and that it has a `steam_appid.txt` file (either in the game root or inside `steam_settings/`). A hidden `steam_settings` folder is fine — those are scanned too.

If the log shows `[WARN] Skipped: appid=... (no 'achievements.json')`, the game is detected but has no achievement metadata. Generate it with the tray [Add game…](#adding-a-game) dialog.

If no games are found at all, the log shows `[WARN] No games with achievement metadata found` and the app keeps running — only games tracked via [Other emulators](#other-emulators) will produce notifications. Check `gamesPaths` in config.

### Game unlocked an achievement but nothing appeared

Check that `%appdata%\GSE Saves\<app_id>\achievements.json` exists and lists the unlock. If it doesn't, the emulator never recorded it and the overlay had nothing to show:

- Older emulator builds and some cracks write to `%appdata%\Goldberg SteamEmu Saves\` instead. Add that folder to `gseSavesPaths`.
- A folder named `4294967295` means the emulator never got a valid AppID — check `steam_appid.txt`.
- Achievements earned **before** you configured the game are never backfilled. The emulator only records an unlock at the moment the game reports it.

More detail in the [GBE reference](docs/gbe-reference.md#where-unlocks-are-stored).

### Notification shows default icon instead of achievement icon

The icon path in the game's `steam_settings/achievements.json` doesn't match an actual file. Check that the `icon` field (e.g. `"img/abc123.jpg"`) points to an existing file relative to the `steam_settings/` directory. The [schema format](docs/gbe-reference.md#the-achievement-schema-format) lists the layouts different generators produce.

For a game tracked through a non-GBE emulator, the default icon also means no schema was found for it — see [Getting icons and the game name back](#getting-icons-and-the-game-name-back).

### Wrong language

The log shows `[WARN] Language '...' not available, falling back to english`. Pick a different **Achievement text** language in the [Settings](#settings) dialog — the list offers the ones your installed games actually carry, though a single game can still be missing any of them.

### Hotkey not working

If you pick a shortcut another application already owns, the [Settings](#settings) dialog says so when you click OK; the log also shows `[WARN] Could not register hotkey`. Pick a different combination under **Shortcut**. The tray menu item still works as a fallback.

### Notifications are too small

Raise **Popup size** in the [Settings](#settings) dialog — it scales the text along with everything
else. The default is 15% of the screen width, which on a 1080p display works out to the smallest size
the popup will draw; anything above that gives you bigger text.

### No sound

Check that **Play a sound on unlock** is on in the [Settings](#settings) dialog. With **Custom file** selected, `[WARN] Sound file not found` means it has since been moved or deleted, and `[WARN] Could not play sound` means it isn't a valid PCM `.wav`. In both cases no sound plays — switch to **Built-in sound**. If one game sounds different from the rest, it is supplying its own — see [Per-game settings](#per-game-settings).

### Settings not saving

If a change made in the [Settings](#settings) dialog doesn't persist, the log shows `[WARN] Config file is malformed, could not update` or `[WARN] Could not write config`. Fix the JSON syntax in `config.json` or check file permissions.

### Still can't make it work?

[Create a GitHub issue](https://github.com/AnotherSava/achievement-overlay/issues/new) with a description of the problem and attach your log file. I'll be happy to update misleading parts of the documentation or fix bugs.

This is a hobby project I built to work around Red Dead Redemption's incompatibility with gbe_fork's built-in overlay. It may not work in every situation, but if it helps you as much as it helped me, it was definitely worth the effort.

## Building from source

**Prerequisites:** Windows 10+, [.NET 10 SDK](https://dotnet.microsoft.com/download)

```
dotnet build src/AchievementOverlay.csproj
dotnet test tests/AchievementOverlay.Tests.csproj
```

The built executable will be in `src/bin/Debug/net10.0-windows/`.

## Code signing policy

This project is planning to apply for free code signing through [SignPath Foundation](https://signpath.org) once community adoption requirements are met. Until then, Windows will show a SmartScreen warning when you run the executable.

**You can help!** Star the repo, fork it, or contribute — growing the community brings us closer to getting a trusted code signing certificate.

**Privacy:** The overlay's normal operation (watching for unlocks and showing notifications) transfers no information to other networked systems. The optional **Add game** feature does make outbound requests — to the Steam Web API, the Steam store, Firecrawl (which scrapes SteamDB), and GitHub — solely to fetch achievement metadata, icons, hidden-achievement descriptions, and GBE binaries.

## License

[GPL-3.0](LICENSE)
