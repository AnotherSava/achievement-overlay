# CLAUDE.md

## What This Is

C# WPF app — Steam-like achievement overlay notifications for GBE-configured games. Monitors `%appdata%/GSE Saves/` for `achievements.json` changes via `FileSystemWatcher` and displays transparent popup notifications over the game window. No game process interaction — purely filesystem-based.

Stack: .NET 10, WinForms tray + WPF notification and settings windows.

## Tracking-configured notification

A synthetic "Gearhead" achievement fires the first time the app sees a game's `%appdata%/GSE Saves/<appid>/` folder appear (GBE creates it on first run) — confirming tracking is live before any real unlock. `AchievementWatcher` raises `GameFolderObserved` from two places — a second `FileSystemWatcher` watching for bare folder creation (the `achievements.json` filter doesn't fire on that), and the first successful read of a folder's `achievements.json`, which carries the parsed states on the event so the handler needn't re-read a file the emulator may still hold open. The folder-creation raise is deliberately unguarded, since at that moment there is usually no file yet to judge the game by. `TrayApplicationContext.NotifyTrackingConfiguredForExistingFolders` sweeps already-existing folders at startup and again after **Add game…** (a game configured mid-session may already have a folder from an earlier run, so its creation event has been and gone). `TrayApplicationContext.TryNotifyTrackingConfigured` gates it (game known in `GameCache`, not already shown, and a *known* earned count of zero — an unlock file that exists but can't be read counts as unknown, never as zero), shows it via `NotificationQueue.EnqueueSynthetic` (bypasses the schema lookup), and persists `appid → fire-time` in the `trackingConfigured` map in `config.json` (once per game). It also appears in the Recent panel, timestamped by fire-time. Bundled icon at `src/Assets/tracking_configured.jpg`; embedded-resource extraction shared via `src/EmbeddedAssets.cs`.

## Self-describing unlock files (non-GBE emulators)

GBE's GSE Saves `achievements.json` holds only `earned`/`earned_time`, so display text comes from the
game's `steam_settings/achievements.json` via `GameCache`. Other emulators — the Goldberg Uplay R2
one, per [issue #5](https://github.com/AnotherSava/achievement-overlay/issues/5) — write the same
file with a numeric `earned` and the `displayName`/`description` inlined per entry. Such a file is
*self-describing*: `AchievementMetadata.HasInlineText`/`IsSelfDescribing` detect it, and the game is
tracked with no `steam_appid.txt`, no `steam_settings/`, and no `gamesPaths` entry.

`AchievementMetadata.ResolvePreferringSchema` is the single place the source is chosen — the game's
schema leads where it defines that achievement name, inline text is the fallback, and the choice is
made **per field**: a schema can name an achievement and still leave a field blank (Steam redacts
hidden achievements' descriptions, and the Add game wizard writes them empty when no Firecrawl key
fills them in), so choosing wholesale would discard text the unlock file did carry. Schema-first is what
gives such a game icons and localised text: the reporter's Uplay emulator is configured to emit the
game's real Steam achievement names, so a game that also has a `steam_settings/` config under
`gamesPaths` is just a Steam game with an unusual writer. Matching on the achievement name is the
appid-collision guard that inline-first used to be — a schema cached under a colliding Ubisoft id
defines other achievements, so it doesn't match and the inline text stands. Both the popup path
(`Resolve`) and the Recent panel call it, so the two can't disagree about an achievement's text;
`Resolve` adds the cache lookup: a full rescan on a miss when there is no inline text (no schema means
no notification at all), and `GameCache.LookupScanningOnce` — one rescan per appid — when there is,
since then the schema only upgrades a notification that already works. Tolerance
lives in `FlexibleBooleanConverter`/`FlexibleInt64Converter` (property-scoped, so the shared
`JsonOptions` is untouched) and in `ParseUnlockStates`, which converts entries individually so one
bad value costs one achievement rather than the whole file.

Consequences elsewhere: `AchievementWatcher` seeds unconditionally (an appid that becomes resolvable
later would otherwise replay its backlog), seeds rather than notifies for a folder that appears after
`Start()`, subscribes `Renamed`, and raises `GameFolderObserved` from the file path as well as the
folder path. An empty `GameCache` is a warning, not a fatal error. A self-describing game with no
`steam_settings/` of its own gets no icons and no game name — the Recent panel falls back to the
appid. Plan: `docs/plans/completed/2026-08-11-uplay-emulator-support.md` (its final section covers the
schema-first follow-up).

## Config generator (Add game dialog)

The tray menu's **Add game…** item opens `AddGameForm` — a wizard (despite the legacy class name) that is a self-contained replacement for the unmaintained `generate_emu_config`. It walks through pages (game folder → AppID, shown only if not auto-detected → API key, shown only if not already saved → hidden-achievements/Firecrawl-key, shown only if the fetched schema has hidden achievements and no Firecrawl key is saved → ready/options → progress). Detection happens when leaving the folder page; the achievement schema is fetched when leaving the last input page (to validate the AppID/key and decide the hidden step) and reused by the generator via the `prefetchedSchema` ctor arg. It produces a GBE-compatible `steam_settings/` folder: locates the Steam DLL, resolves the AppID, fetches the achievement schema + icons from the Steam Web API, fills in hidden descriptions by scraping SteamDB through the Firecrawl API (`SteamDbScraper`; SteamDB is behind Cloudflare so a plain HttpClient can't reach it), downloads the GBE release (`SharpCompress` for 7z), backs up and replaces the DLL, and writes `steam_interfaces.txt` via GBE's bundled tool.

The engine lives under `src/GbeConfig/`. Modules keep parsing logic in pure static methods (unit-tested in `tests/GbeConfig/`) separate from the network/IO/subprocess work in `GbeConfigGenerator`, which is front-end agnostic — it reports progress through `IConfigProgress` (the dialog implements it to drive its checklist + log) and takes a `ConfigRequest`. The Steam Web API key and optional Firecrawl API key are stored as optional fields in the app's own `config.json` (via `SettingsData`/`AppConfig`).

After a run, `TrayApplicationContext.RegisterNewGame` ensures the game's folder is covered by `gamesPaths` (using `GamesPathPlanner`), rescans `GameCache`, and re-seeds the watcher so the game is tracked without a restart.

The original plan for this feature (written for a CLI; the front-end was later changed to the dialog) is at `docs/plans/completed/2026-06-18-gbe-config-generator.md`.

## Settings window

The tray menu's **Settings…** opens `SettingsWindow` — four pages behind a nav rail (General,
Notifications, Folders, Advanced), one card per setting with its explanation visible rather than
hidden in a tooltip. Built to the `5a`–`5d` frames of the Claude Design project *Windows app settings
dialog redesign*.

**WPF, not WinForms.** The design needs a toggle switch, sliders and OS dark mode, none of which
WinForms has; the app already hosts WPF for the notification itself, so this adds no dependency.
`ThemeMode="System"` (WPF's Fluent theme) supplies the control styles, the light/dark switch and the
system accent — only the chrome the theme has no key for (page, card and nav colours) is defined in
`ApplyThemeBrushes`, matched to the mode read from the registry. The popup deliberately keeps its own
dark `#1A1A2E` look: it sits over a game, not over Windows, so the two are not meant to match.

The window writes nothing itself. It returns a `SettingsResult` holding the values that changed —
`SettingsDiff.Compute` diffs the collected `SettingsData` against the snapshot the window opened
against, keyed by property name — and `TrayApplicationContext.ApplySettings` persists them through
`AppConfig.UpdateConfigValues` (one file write, not one per field) and re-wires whatever binds a
changed value at startup. That diff is load-bearing rather than an optimisation: it's how the host
knows to re-register the hotkey, rescan `GameCache`, or rebuild `AchievementWatcher` over new
`gseSavesPaths` (it binds its paths at construction, and `Start()` re-seeds from disk so the new
paths' backlog is recorded rather than replayed). Values read live on every use — sound, duration,
language, font, scale, recent count — need nothing beyond the write.

**Pause notifications** is deliberately absent: a momentary tray toggle, not a setting. The
`trackingConfigured` map stays out too — app-managed state, and `SettingsDiff` never compares it, so
a save can't drop a game's setup confirmation.

Page-specific notes:

- **Achievement text** (the `language` key) is a combo box listing what the installed games actually
  provide. `AchievementMetadata.CollectLanguages` reads the keys off a multi-language
  `displayName`/`description` object, and `TrayApplicationContext.AvailableLanguages` unions **both**
  sources of display text: every game's schema, and every GSE Saves unlock file — a self-describing
  game has no schema, so its unlock file is the only record of the languages it can show. Steam's
  `token` key is excluded; it sits in the same object but is a localisation token, and picking it
  would put `NEW_ACHIEVEMENT_1_0_NAME` on screen as the achievement name. It stays **editable on
  purpose**: the list cannot be proven complete (a fresh install offers only english), and
  select-only would leave a legitimate language reachable only by hand-editing `config.json`.
- **Popup width** (the `scale` key) is deliberately *one* setting rather than a size plus a separate
  text size. They overlap — scaling already enlarges the text — and a large popup with small text is a
  combination neither value alone would explain.

  There is also **no "automatic" mode**, because automatic was only ever *15% of the display's width*.
  Expressed in a unit the user picks, that is just the default value, so the mode disappears and the
  number on screen means something: `NotificationScale` is `"15%"` (a share of the display) or
  `"384px"` (absolute), written self-describing so the unit survives the round trip. The earlier
  design drew this as Automatic/Fixed with a percentage of the 322 px *design* width — an abstract
  number ("119%") that told the user nothing.

  `NotificationScale.WidthOn` gives the requested width; `NotificationWindow.ComputeScale` clamps it
  to the readable range and is the single pure calculation behind both what the footer reports and
  what is drawn, so the two cannot disagree. Switching unit in the window carries the current width
  across rather than the bare number, so the popup doesn't jump (15% of 2560 → 384 px).
- **Font** applies to the whole popup by being set on the window, which every `TextBlock` inherits —
  one assignment, no per-element drift. An unknown family is not an error: WPF falls back. The picker
  is a shortlist rather than every installed family, because achievement text is localised and a
  picker of everything invites one with no Cyrillic or CJK coverage.
- **Shortcut** captures keystrokes rather than accepting typed text, so its value always round-trips
  through `GlobalHotkey.ParseHotkeyString`; note `Keys.ToString()` can return an alias (`OemQuestion`
  → `Oem2`), which parses back fine.

  Capturing needs two defences, because **a global hotkey beats the focused window** — that is the
  whole point of `RegisterHotKey` — so a combination already spoken for never reaches the field, and
  the shortcuts most worth reassigning are exactly the unreachable ones:
  - *Ours*: `OpenSettingsDialog` suspends the hotkey for the window's lifetime and rebuilds it from
    config in a `finally`, so a cancel re-registers the unchanged value. `ApplySettings` therefore
    does **not** touch the hotkey.
  - *Everyone else's*: `LowLevelKeyboardHook` (`WH_KEYBOARD_LL`), installed only while the field has
    focus. It runs ahead of every hotkey owner and returns 1 to swallow the keystroke, so another
    app's hotkey — or an Explorer "Shortcut key" on a `.lnk`, registered the same way — does not
    fire. It is *system-wide*, so `OnHookedKeyDown` bails out unless this window is active with the
    field focused, and both blur and close uninstall it.

  Both paths funnel into `TryCaptureShortcut` so they cannot disagree. The hook reports WinForms
  `Keys` (physical, e.g. `LControlKey`); the WPF fallback converts through
  `KeyInterop.VirtualKeyFromKey`, so both spellings are filtered.
- **Sound** is a radio pair (Built-in / Custom file) rather than a path box that means "built-in" when
  empty. Choosing Built-in writes `soundPath: ""`, so config still says plainly which is in use.
- **Folder cards** show a live status line — how many games were found, or that a drive isn't
  connected — which is the check that used to run only on OK. Entries round-trip **raw** (no
  expansion), and a freshly picked folder is packed back through
  `AppConfig.CollapseEnvironmentVariables`, the inverse of `ExpandEnvironmentVariables`. Without that,
  editing the default `%appdata%\GSE Saves` would pin it to one machine's user profile, which matters
  because this config is used from more than one. Collapsing takes the *deepest* matching folder
  (`%localappdata%` over `%userprofile%`) and only on a separator boundary, so `C:\Users\Bobby` never
  collapses against `C:\Users\Bob`. Existing entries are left exactly as written.
- **Show me** needs no special handling for the window covering the corner: the notification is
  topmost, so it draws over the window, and the window can be moved.

Validation blocks only the two entries that would fail silently — a GSE Saves list where nothing
exists, and a missing custom sound file — and switches to the page that needs fixing before saying so.

The folder picker and browse button are shared with `AddGameForm` via `src/DialogControls.cs`;
`PickFolder` takes a nullable owner so the WPF window, which has no `IWin32Window`, uses the same one.
## Config files

`config/default.json` is the committed config that ships as `config.json` next to the exe. For local
builds/deploys, a gitignored `config/local.json` takes precedence (the csproj links whichever exists,
preferring local) — use it to keep your personal `steamWebApiKey` / `firecrawlApiKey` out of git. On
CI/release builds `local.json` doesn't exist, so `default.json` is used. `config/local.json` is
created from the committed defaults plus credential placeholders; fill them in after first checkout.

Fill the keys in **`config/local.json`**, not in the running app. Deploy copies `local.json` over the
installed `config.json`, so a key typed into the Add game wizard (which writes the *installed* file)
survives only until the next deploy — an empty placeholder in `local.json` silently blanks it.

## Encrypted files

The `track-achievements` skill under `.claude/skills/` is the operational runbook for putting a game
on this machine under GBE tracking — the counterpart to the Add game wizard, for the cases a GUI can't
cover. It is stored encrypted: `.gitattributes` routes that directory through transcrypt, so the files
are plaintext in the working tree and ciphertext in git.

The whole directory is encrypted rather than individual `*.secret.*` files because Claude Code
discovers a skill by its literal `SKILL.md` name, which leaves no room for the marker in the filename.

A fresh clone reads those files as base64 and the skill won't register until the repo is unlocked with
transcrypt and the shared passphrase. Everything the runbook covers that is safe to publish lives in
`docs/gbe-reference.md` and `docs/playnite.md` instead — put new findings there by preference, and
keep the encrypted files for what genuinely can't be public.

## Build & Test

```
dotnet build src/AchievementOverlay.csproj -c Release
dotnet test tests/AchievementOverlay.Tests.csproj
```
