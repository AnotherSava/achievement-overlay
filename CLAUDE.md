# CLAUDE.md

## What This Is

C# WPF app — Steam-like achievement overlay notifications for GBE-configured games. Monitors `%appdata%/GSE Saves/` for `achievements.json` changes via `FileSystemWatcher` and displays transparent popup notifications over the game window. No game process interaction — purely filesystem-based.

Stack: .NET 10, WinForms tray + WPF overlay window.

## Tracking-configured notification

A synthetic "Gearhead" achievement fires the first time the app sees a game's `%appdata%/GSE Saves/<appid>/` folder appear (GBE creates it on first run) — confirming tracking is live before any real unlock. `AchievementWatcher` raises `GameFolderObserved` from two places — a second `FileSystemWatcher` watching for bare folder creation (the `achievements.json` filter doesn't fire on that), and the first successful read of a folder's `achievements.json`, which carries the parsed states on the event so the handler needn't re-read a file the emulator may still hold open. The folder-creation raise is deliberately unguarded, since at that moment there is usually no file yet to judge the game by. `TrayApplicationContext.NotifyTrackingConfiguredForExistingFolders` sweeps already-existing folders at startup and again after **Add game…** (a game configured mid-session may already have a folder from an earlier run, so its creation event has been and gone). `TrayApplicationContext.TryNotifyTrackingConfigured` gates it (game known in `GameCache`, not already shown, and a *known* earned count of zero — an unlock file that exists but can't be read counts as unknown, never as zero), shows it via `NotificationQueue.EnqueueSynthetic` (bypasses the schema lookup), and persists `appid → fire-time` in the `trackingConfigured` map in `config.json` (once per game). It also appears in the Recent panel, timestamped by fire-time. Bundled icon at `src/Assets/tracking_configured.jpg`; embedded-resource extraction shared via `src/EmbeddedAssets.cs`.

## Self-describing unlock files (non-GBE emulators)

GBE's GSE Saves `achievements.json` holds only `earned`/`earned_time`, so display text comes from the
game's `steam_settings/achievements.json` via `GameCache`. Other emulators — the Goldberg Uplay R2
one, per [issue #5](https://github.com/AnotherSava/achievement-overlay/issues/5) — write the same
file with a numeric `earned` and the `displayName`/`description` inlined per entry. Such a file is
*self-describing*: `AchievementMetadata.HasInlineText`/`IsSelfDescribing` detect it, and the game is
tracked with no `steam_appid.txt`, no `steam_settings/`, and no `gamesPaths` entry.

`AchievementMetadata.ResolvePreferringInline` is the single place the source is chosen — inline text
wins when present (a self-describing file is not GBE's, so a cached schema under the same id is a
different game — Ubisoft and Steam id ranges overlap), otherwise the schema. Both the popup path
(`Resolve`) and the Recent panel call it, so the two can't disagree about an achievement's text;
`Resolve` adds the cache lookup around it and rescans only for an appid with neither source. Tolerance
lives in `FlexibleBooleanConverter`/`FlexibleInt64Converter` (property-scoped, so the shared
`JsonOptions` is untouched) and in `ParseUnlockStates`, which converts entries individually so one
bad value costs one achievement rather than the whole file.

Consequences elsewhere: `AchievementWatcher` seeds unconditionally (an appid that becomes resolvable
later would otherwise replay its backlog), seeds rather than notifies for a folder that appears after
`Start()`, subscribes `Renamed`, and raises `GameFolderObserved` from the file path as well as the
folder path. An empty `GameCache` is a warning, not a fatal error. Uplay games get no icons and no
game name — the Recent panel falls back to the appid. Plan:
`docs/plans/completed/2026-08-11-uplay-emulator-support.md`.

## Config generator (Add game dialog)

The tray menu's **Add game…** item opens `AddGameForm` — a wizard (despite the legacy class name) that is a self-contained replacement for the unmaintained `generate_emu_config`. It walks through pages (game folder → AppID, shown only if not auto-detected → API key, shown only if not already saved → hidden-achievements/Firecrawl-key, shown only if the fetched schema has hidden achievements and no Firecrawl key is saved → ready/options → progress). Detection happens when leaving the folder page; the achievement schema is fetched when leaving the last input page (to validate the AppID/key and decide the hidden step) and reused by the generator via the `prefetchedSchema` ctor arg. It produces a GBE-compatible `steam_settings/` folder: locates the Steam DLL, resolves the AppID, fetches the achievement schema + icons from the Steam Web API, fills in hidden descriptions by scraping SteamDB through the Firecrawl API (`SteamDbScraper`; SteamDB is behind Cloudflare so a plain HttpClient can't reach it), downloads the GBE release (`SharpCompress` for 7z), backs up and replaces the DLL, and writes `steam_interfaces.txt` via GBE's bundled tool.

The engine lives under `src/GbeConfig/`. Modules keep parsing logic in pure static methods (unit-tested in `tests/GbeConfig/`) separate from the network/IO/subprocess work in `GbeConfigGenerator`, which is front-end agnostic — it reports progress through `IConfigProgress` (the dialog implements it to drive its checklist + log) and takes a `ConfigRequest`. The Steam Web API key and optional Firecrawl API key are stored as optional fields in the app's own `config.json` (via `SettingsData`/`AppConfig`).

After a run, `TrayApplicationContext.RegisterNewGame` ensures the game's folder is covered by `gamesPaths` (using `GamesPathPlanner`), rescans `GameCache`, and re-seeds the watcher so the game is tracked without a restart.

The original plan for this feature (written for a CLI; the front-end was later changed to the dialog) is at `docs/plans/completed/2026-06-18-gbe-config-generator.md`.

## Config files

`config/default.json` is the committed config that ships as `config.json` next to the exe. For local
builds/deploys, a gitignored `config/local.json` takes precedence (the csproj links whichever exists,
preferring local) — use it to keep your personal `steamWebApiKey` / `firecrawlApiKey` out of git. On
CI/release builds `local.json` doesn't exist, so `default.json` is used. `config/local.json` is
created from the committed defaults plus credential placeholders; fill them in after first checkout.

## Build & Test

```
dotnet build src/AchievementOverlay.csproj -c Release
dotnet test tests/AchievementOverlay.Tests.csproj
```
