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
