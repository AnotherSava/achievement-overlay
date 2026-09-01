---
layout: default
title: Adding a game
parent: Usage
nav_order: 4
---

# Adding a game

For the overlay to show anything, each game needs a `steam_settings/achievements.json` next to its `steam_api64.dll`. The app can generate this for you — right-click the tray icon and choose **Add game…** to launch a short wizard. It is a self-contained replacement for the unmaintained `generate_emu_config` tool.

## What the wizard asks

It only asks for what it cannot work out on its own, so a typical run is two or three pages.

1. **Game folder** — pick the game's install folder. The wizard finds the Steam DLL below it (even nested deep in an Unreal Engine layout) and tries to detect the AppID.
2. **Steam AppID** — shown only if the AppID could not be detected from the game folder. If a Steam store search guessed one, it is pre-filled for you to verify.
3. **Steam Web API key** — shown only the first time; it is saved to [`config.json`](configuration) and reused afterwards. Required for the achievement schema; get one at [steamcommunity.com/dev/apikey](https://steamcommunity.com/dev/apikey). The key is stored unencrypted — if that is a concern, revoke it right after the game is added. It is only needed while adding a game, and you will just enter a fresh one next time.
4. **Hidden achievements** — shown only if the game actually has hidden achievements and you have not already saved a Firecrawl API key. Steam blanks out the descriptions of secret achievements; the real text lives on SteamDB, behind Cloudflare, so the tool fetches it through [Firecrawl](https://firecrawl.dev), a hosted scraper. Paste a free Firecrawl API key, or leave it blank to skip — those descriptions then stay as placeholders.
5. **Ready** — review the summary and options (back up the original DLL, and an **Advanced** section for the GBE release folder), then click **Add game**.

## What it then does

It fetches the achievement icons from Steam, downloads the matching GBE release, backs up and replaces the Steam DLL, and writes a `steam_settings/` folder with GBE's own overlay disabled — this app replaces it. The final page shows live progress.

When it finishes, the game's folder is added to `gamesPaths` if it is not covered already, and the overlay starts tracking it immediately. No restart.

If a configuration already exists for the game, the wizard shows its location and asks before overwriting it.

## Two things that can stop it

- **Denuvo games** do not load `steam_api64.dll` at all. If Denuvo is detected with no crack present, the tool stops before changing anything.
- **Windows Defender** sometimes raises a false positive on current GBE releases as they download. The wizard offers to add the needed Defender exclusions (with a UAC prompt) and retries automatically. Alternatively, point the Advanced **GBE release folder** at an already-extracted release and uncheck "Download the latest GBE release".

## Doing it by hand

The [GBE reference](../development/gbe-reference) covers what the wizard is doing underneath — where unlocks are stored, the achievement schema format, why the overlay disables GBE's own, and why hidden achievement descriptions arrive blank. None of it is required reading for normal use.
