---
layout: default
title: Privacy
nav_order: 6
---

# Privacy

Short version: watching for achievements is entirely local, and the only feature that reaches the network is the one that has to.

## What is not collected

There is no telemetry, no analytics, no crash reporting and no update check. Nothing about you, your games, your unlocks or your machine is sent anywhere as part of normal operation. Watching for unlocks and showing notifications transfers no information to other networked systems at all.

Everything the app knows lives in two files next to the executable — `config.json` and `overlay.log` — both readable and both yours to delete. The tray menu's **Open config/logs location** opens the folder. The log keeps every run rather than only the current one, so that a problem is still described after a restart; it rolls over to `overlay.log.1` past 1 MB, and deleting either file is safe.

The tray menu's **Report a problem…** collects some of that into one file for you to attach to an issue. It uploads nothing: the report is shown to you in full, saved only where you choose, and API keys are replaced with `xxxxxx` before you see it. It covers the one game you pick, and log lines about your other games are removed, so reporting a problem does not publish what else you have installed. [What it contains](troubleshooting#reporting-a-problem).

## What the app reads locally

| Path | Why |
|---|---|
| The folders listed in `gseSavesPaths` | To notice when the emulator records an unlock |
| The folders listed in `gamesPaths` | To find each game's `steam_appid.txt` and `steam_settings/` |
| `HKCU\...\Run` | Only when **Start with Windows** is toggled |

## What goes out, and when

Only the [Add game](usage/adding-a-game) wizard makes outbound requests, and only while it is running. Nothing in that list is contacted unless you open the wizard and complete it.

| Host | What is sent | Why |
|---|---|---|
| `api.steampowered.com` | Your Steam Web API key and the game's AppID | Fetch the achievement schema |
| Steam's content CDN | Nothing but the icon URLs the schema returned | Download achievement icons |
| `store.steampowered.com` | The game folder's name, as a search term | Guess the AppID when it cannot be detected on disk |
| `api.firecrawl.dev` | Your Firecrawl API key and the game's SteamDB stats URL | Fetch hidden-achievement descriptions, which Steam's own API redacts |
| `api.github.com` and GitHub's release CDN | Nothing but the request itself | Download the GBE release the wizard installs |

The Firecrawl step is the only one that involves a third party rather than the source itself: SteamDB sits behind Cloudflare, so the page is fetched through Firecrawl's hosted scraper. It is optional — leave the Firecrawl key blank and hidden descriptions stay as placeholders.

## Your API keys

The Steam Web API key and the optional Firecrawl key are stored **unencrypted** in `config.json`, because the app has nowhere better to put them and pretending otherwise would be worse than saying so. They are only needed while a game is being added. If keeping them on disk bothers you, revoke the Steam key after the game is added and paste a fresh one next time; the wizard will simply ask again.

## Reporting a problem

Log files are local and can contain your game folder paths. If you attach one to a [GitHub issue](https://github.com/AnotherSava/achievement-overlay/issues/new), skim it first and redact anything you would rather not publish.
