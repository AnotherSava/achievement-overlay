---
layout: default
title: Development
nav_order: 5
has_children: true
has_toc: false
---

# Development

## Setup

**Prerequisites:** Windows 10 or later, and the [.NET 10 SDK](https://dotnet.microsoft.com/download). The app targets `net10.0-windows` and uses both WPF and WinForms, so it does not build on macOS or Linux.

```
git clone https://github.com/AnotherSava/achievement-overlay.git
cd achievement-overlay
dotnet build src/AchievementOverlay.csproj
```

The executable lands in `src/bin/Debug/net10.0-windows/`, with `config.json` copied in beside it.

## Commands

| Command | What it does |
|---|---|
| `dotnet build src/AchievementOverlay.csproj` | Build the app |
| `dotnet build src/AchievementOverlay.csproj -c Release` | Release build |
| `dotnet test tests/AchievementOverlay.Tests.csproj` | Run the xUnit suite |
| `bash .claude/commit-checks.sh` | The gate: a clean-slate Release build with `-warnaserror`, then the tests |

Prefer the last one before pushing. It builds the **test** project, which pulls in `src` through its project reference, so warnings in test code are seen at all — and it passes `--no-incremental`, because MSBuild skips analysis for unchanged projects and a cached build reports no warnings even when the code still has them.

## Configuration files

`config/default.json` is the committed config that ships as `config.json` next to the executable. For local builds a gitignored `config/local.json` takes precedence — the csproj links whichever exists, preferring local — so a personal `steamWebApiKey` and `firecrawlApiKey` stay out of git. On CI and release builds `local.json` does not exist, so `default.json` is used.

## Project structure

```
src/
  Program.cs                  entry point
  TrayApplicationContext.cs   tray icon and menu; wires everything below together
  AppConfig.cs                config.json load, update and env-var expansion
  AchievementWatcher.cs       FileSystemWatcher over the GSE Saves folders
  GameCache.cs                the scan of gamesPaths: appid -> game folder and schema
  AchievementMetadata.cs      schema parsing, and choosing an achievement's display text
  NotificationQueue.cs        which popup shows next, and how they stack
  NotificationWindow.xaml     the popup itself
  NotificationPlacement.cs    where a popup rests and which way a stack grows
  PopupPalette.cs             text colours derived from the chosen background
  RecentAchievementsDisplay.cs  the panel behind the global shortcut
  SettingsWindow.xaml         the four-page settings window
  SettingsDiff.cs             what the window changed, keyed by property name
  AddGameForm.cs              the Add game wizard
  GbeConfig/                  the config generator engine behind the wizard
  GbeOverlay/                 reading a game's own configs.overlay.ini
tests/                        xUnit tests, mirroring src/ file for file
config/default.json           the config.json shipped next to the exe
docs/                         this documentation site
```

## Design notes

**Nothing touches the game.** The app never hooks, injects into, or reads memory from a running process. It watches files, which is the whole reason it works with titles that reject an in-game overlay. Any change that would require reaching into a game is out of scope.

**Anything positional or colour-related lives in a pure module.** Three separate code paths draw a popup — the unlock notification, the recent achievements panel, and the settings window's **Show me** preview — and they must agree about where an edge is, which way a stack grows and what colour the text is. `NotificationPlacement` and `PopupPalette` are pure functions those three all call, which is why they can be unit-tested with no window and no dispatcher, and why the three cannot drift apart.

**The settings window writes nothing.** It returns the values that changed, computed by `SettingsDiff`; the tray context persists them in one file write and re-wires whatever binds a changed value at startup. The diff is what tells the host to re-register the hotkey, rescan the game cache, or rebuild the watcher.

**Test-only code does not go in `src/`.** Tests exercise the public surface and real behaviour; `InternalsVisibleTo` gives them access to internals rather than production code exposing hooks it does not otherwise need.

## Releases

Pushing a `v*` tag builds and publishes both archives — self-contained and framework-dependent — through the `build` workflow in `.github/workflows/`. Pushes and pull requests against `main` run the build and tests only.

## Going deeper

The [GBE reference](development/gbe-reference) documents the emulator itself: where it stores unlocks, how its overlay config files are parsed, the achievement schema format, and why hidden achievement descriptions come back blank. It is the background behind most of what `GbeConfig/` and `GbeOverlay/` do.
