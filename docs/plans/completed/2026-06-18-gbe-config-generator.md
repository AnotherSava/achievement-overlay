# GBE config generator: a C# replacement for `generate_emu_config`

## Background

`gbe_fork_tools/generate_emu_config.exe` (the standard GBE config generator) is broken in 2026 and the entire Python ecosystem it depends on is unmaintained. This plan adds a C# replacement to achievement-overlay so configuration becomes self-contained — one repo, one binary, no Python venv.

### Why the existing tool fails

- `Detanup01/gbe_fork_tools` depends on `ValvePython/steam` (steam-py) → last upstream commit **May 2023**.
- Steam dropped legacy CM TCP support since then; only WebSocket-based CM clients work today.
- `steam-py` and well-known forks fail in different ways:
  - **Stock `steam-py`**: WebAuth flow raises `KeyError: 'refresh_token'` during 2FA polling.
  - **`steam-next` (`fabieu/steam-next`, current upstream-of-record)**: WebAuth works, but CM connect still tries raw TCP on `:27017`; Steam closes the socket immediately. Auth ends with `EResult.NoConnection` (48).
  - **`detiam/steam_websocket`** (the pin used by `gbe_fork_tools` source): added a WebSocket backend, but `WebsocketConnection._reader_loop` calls `gselect([self.socket], …)` with `self.socket = None` after a partial connect → `TypeError: argument must be an int, or have a fileno() method`.
- `generate_emu_config.exe` is a frozen PyInstaller bundle of the above stack, so the brokenness is shipped in the binary.

### What works

- `Gobot1234/steam.py` (PyPI name **`steamio`**, async/`asyncio`) successfully authenticates and reaches the CM (verified 2026-06-18 against AppID 1601580 Frostpunk 2 — login + `fetch_app` + `app.achievements()` all working). Caveat: requires `aiohttp<3.14` (PR #621 in that repo) and has a small bug in `FetchedAppMovie.__init__` that monkey-patches around (`movie["mp4"]["max"]` assumes mp4 exists).
- For the **achievement-tracking use case** specifically, none of the CM machinery is actually needed. Everything required is reachable over plain HTTPS. That's the whole reason the manual fallback in `~/.claude/skills/track-achievements/SKILL.md` Appendix works.

## Scope

Add a **CLI mode** to `AchievementOverlay.exe` (or a sibling exe in the same project — see "Packaging" below) that, given a game directory and optionally an AppID, produces a GBE-compatible `steam_settings/` folder ready to drop next to `steam_api64.dll`.

**In scope** (what tracking actually needs):
1. Detect `steam_api64.dll` (or `steam_api.dll`) under the game directory; handle deep UE nesting.
2. Resolve AppID — explicit arg, then any local `steam_appid.txt`, then Steam store search by game name as last resort.
3. Detect Denuvo/cracked state and warn appropriately.
4. Back up the original Steam DLL adjacent to where it was found.
5. Fetch achievement schema via `ISteamUserStats/GetSchemaForGame` (Web API).
6. Download achievement icons (locked + unlocked) to `achievement_images/`.
7. Patch hidden-achievement placeholder descriptions from a SteamDB scrape.
8. Generate `steam_interfaces.txt` by invoking GBE's bundled `generate_interfaces_x64.exe` against the original DLL.
9. Write `achievements.json`, `steam_appid.txt`, `configs.overlay.ini` with `enable_experimental_overlay=0` (GBE overlay disabled — this app replaces it).
10. Replace the loaded DLL with GBE's `release/regular/x64/steam_api64.dll`.
11. (Optional) Add the game's Playnite GUID to SuccessStory's `ForcedSteamAppIds` mapping.

**Out of scope** (requires CM access, not worth the complexity):
- DLC list, depot map, supported languages
- Controller config / inventory / cloud-save dirs
- Multi-language achievement schemas (Achievement Watcher format)
- Anything `generate_emu_config -shots -thumbs -vid -aw -cdx` produces

If a game ever needs the full CM-derived config, fall back to a Python `steamio` script — but that should be a separate tool, not part of achievement-overlay.

## Data sources

| Need | Endpoint / Source | Auth |
|---|---|---|
| Achievement schema (display name, description, hidden flag, icon URLs) | `https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key=<API_KEY>&appid=<APPID>` | Steam Web API key |
| AppID lookup by name | `https://store.steampowered.com/search/?term=<name>` (scrape `data-ds-appid`) | None |
| App display name / DRM info (if needed) | `https://store.steampowered.com/api/appdetails?appids=<APPID>` | None — public, but unreliable for 3rd-party DRM field |
| Hidden achievement descriptions | `https://steamdb.info/app/<APPID>/stats/` (HTML scrape) | Cloudflare-protected — see "SteamDB scraping" below |
| Achievement icons | URLs returned by `GetSchemaForGame` (`icon`, `icongray`) | None |
| Steam interface list (`steam_interfaces.txt`) | Bundled `generate_interfaces_x64.exe` against the **original** DLL | None |
| GBE binaries (regular DLL + `generate_interfaces_x64.exe`) | `https://github.com/Detanup01/gbe_fork/releases/latest` | None |

API key storage: read from `%APPDATA%/AchievementOverlay/config.json` (already the app's config home) under a new `steamWebApiKey` field. First-run prompts the user once and writes it.

## Output layout

For each configured game (target dir is wherever `steam_api64.dll` lives, **not** the game root):

```
<target>/steam_api64.dll               ← GBE regular x64 (overwrites)
<target>/steam_api64.dll.original      ← backup of original
<target>/steam_settings/
    steam_appid.txt
    achievements.json
    steam_interfaces.txt
    configs.overlay.ini
    achievement_images/
        <achievement_api_name>.jpg     ← unlocked icon
        <achievement_api_name>_gray.jpg ← locked icon
```

This is the minimum GBE accepts. No `configs.main.ini` / `configs.app.ini` needed — defaults are fine.

## Implementation sketch

### Project structure

Two viable shapes — pick when implementing:

**A. CLI subcommand on existing exe.**
`AchievementOverlay.exe config <game_dir> [--appid <id>] [--api-key <key>]` — parse args in `Program.cs`, dispatch to a new `GbeConfigGenerator` class. Skip WPF startup when running in CLI mode. Smallest change, single binary.

**B. Sibling exe in same solution.**
New `AchievementOverlay.ConfigGen.csproj` (`OutputType=Exe`) referencing shared types from the main project via `InternalsVisibleTo` (already set up for the test project). Cleaner separation — the WPF/WinForms host doesn't get a CLI mode bolted on. Slightly larger refactor.

Recommend **A** for v1, can split out later if it grows.

### New code (in `src/`)

- `GbeConfig/AppIdResolver.cs` — find AppID via `steam_appid.txt` → `*.ini` strings → Steam store search.
- `GbeConfig/DllLocator.cs` — find `steam_api64.dll` / `steam_api.dll` under a game dir; handle multiple matches.
- `GbeConfig/DrmDetector.cs` — file size heuristic + `Denuvo` string search + (optional) SteamDB lookup.
- `GbeConfig/SteamWebApi.cs` — `HttpClient` wrapper around `GetSchemaForGame`, `appdetails`, returns typed records.
- `GbeConfig/AchievementSchemaWriter.cs` — convert Web API response → GBE `achievements.json` format (see existing example in `post_build/steam_settings.EXAMPLE/`).
- `GbeConfig/IconDownloader.cs` — parallel HTTP downloads of `icon` + `icongray` URLs.
- `GbeConfig/SteamDbScraper.cs` — fetch the stats page and parse hidden descriptions. Needs Cloudflare bypass — see below.
- `GbeConfig/InterfaceGenerator.cs` — invoke `generate_interfaces_x64.exe` as subprocess; capture `steam_interfaces.txt`.
- `GbeConfig/GbeBinaryManager.cs` — locate or download GBE release binaries; cache under `%LOCALAPPDATA%/AchievementOverlay/gbe-cache/<version>/`.
- `GbeConfig/PlanniteIntegration.cs` — (optional, behind `--playnite` flag) edit `%appdata%/Playnite/ExtensionsData/cebe6d32-…/config.json` `ForcedSteamAppIds`.
- `GbeConfig/GbeConfigGenerator.cs` — orchestrator that runs the steps in order.

`Program.cs` change: detect first-arg `config` and route to `GbeConfigGenerator.RunAsync(args)`, exiting before WPF startup.

### `achievements.json` schema (target output)

The schema GBE expects is a JSON array; each element has these fields (from observed real configs):

```json
{
  "name": "ACHIEVEMENT_API_NAME",
  "displayName": "Display Name",
  "description": "Description text",
  "hidden": "0",
  "icon": "achievement_images/ACHIEVEMENT_API_NAME.jpg",
  "icon_gray": "achievement_images/ACHIEVEMENT_API_NAME_gray.jpg"
}
```

Note `hidden` is a string `"0"`/`"1"`, not a bool — GBE quirk. The icons in the JSON are relative paths into the same `steam_settings/` folder.

### Hidden-description handling

`GetSchemaForGame` redacts the description for `hidden=1` achievements to `"No description available"`. SteamDB exposes the real text. The skill's REFERENCE.md documents the scrape format:

> The page lists each achievement as `<DisplayName>` followed by `_Hidden achievement:_ <description>` and the API name on its own line.

Match by API name (most reliable — display names can be ambiguous), patch the description, leave `hidden=1` so GBE still treats it as hidden in the UI.

### SteamDB scraping

SteamDB is behind Cloudflare. Plain `HttpClient.GetAsync` returns 403 with a JS challenge page. Options:

1. **Headless browser** (Playwright `.NET`, ~150 MB dependency) — heavy.
2. **`FlareSolverr`** (proxy that solves Cloudflare) — extra runtime dep.
3. **User-supplied cookie** — first run prompts the user to copy `cf_clearance` from their browser; cache it; refresh on 403. Lightest implementation.
4. **Fall back to a separate manual step** — if scraping fails, log placeholder achievements and tell the user how to fill them in.

Pick **3** with fallback to **4**. Don't add a browser runtime.

### Denuvo handling

Mirror what the skill does:
1. Check exe file size — anything >200 MB is suspicious.
2. `grep` the exe for the literal `Denuvo` string.
3. If size suspicious but string absent, log a warning and continue — modern Denuvo doesn't always embed the string. Don't try to authoritatively detect from SteamDB; that requires CF bypass and the answer doesn't change what we do.

If Denuvo is detected and **no crack indicator** is present (`.rzr`, `.rne`, `.cdx`, `.bak` adjacent to exe, etc.), **error out**: tell the user the game won't load `steam_api64.dll` and bail before touching files.

### GBE binary management

Don't bundle GBE in our release — license + size. On first run, download from `https://api.github.com/repos/Detanup01/gbe_fork/releases/latest`, extract `emu-win-release.7z` to `%LOCALAPPDATA%/AchievementOverlay/gbe-cache/<tag>/`. Use `SharpCompress` for 7z extraction (NuGet, no native deps).

Watch out for Windows Defender — current GBE releases sometimes trigger detection. Document this in the README and provide a manual-install fallback path (point at an existing extracted `C:/Programs/gbe-release/`).

## CLI surface (v1)

```
AchievementOverlay.exe config <game_dir> [options]

Options:
  --appid <id>           Skip auto-detection, use this AppID
  --api-key <key>        Steam Web API key (or env STEAM_WEB_API_KEY, or config.json)
  --gbe-path <dir>       Use this GBE release dir instead of downloading
  --playnite             Wire into SuccessStory's ForcedSteamAppIds
  --no-backup            Don't write steam_api64.dll.original (default: backup)
  --dry-run              Show what would be done, change nothing
  --force                Overwrite an existing steam_settings/
  -v, --verbose          Debug-level logging
```

Exit codes: `0` success, `1` user error (bad args, missing API key, Denuvo no-crack), `2` network/IO failure, `3` partial success (e.g. hidden-desc scrape failed but config was generated).

## Migration / interaction with the skill

`~/.claude/skills/track-achievements/SKILL.md` should be updated to:
1. Prefer `AchievementOverlay.exe config <dir>` when the binary is available.
2. Keep the existing manual-fallback path as a documented secondary route.
3. Note that `generate_emu_config.exe` is deprecated due to the upstream stack being unmaintained.

The skill's `fetch_achievements.py` can stay as a Python reference implementation but the C# tool becomes the default.

## Validation

For each implemented step, sanity-check against a real game:
- **Frostpunk 2** (AppID 1601580, RUNE crack, ~80 achievements) — primary test case, all the edge cases hit it.
- **A simple AAA without Denuvo** for the no-crack happy path.
- **A UE5 game with deeply nested `steam_api64.dll`** (e.g. anything under `Engine/Binaries/ThirdParty/Steamworks/Steamv157/Win64/`) to exercise the DLL locator.
- **A game with hidden achievements** (most modern releases) to exercise the SteamDB scrape + patch.

Compare the generated `steam_settings/` against one produced by the (working historical) `generate_emu_config` for the same game — `diff -r` should show only the optional metadata files we're intentionally skipping.

## Open questions

1. Distribution: bundle the GBE-download step into the main exe, or expect the user to run a one-time `AchievementOverlay.exe config setup` first?
2. SteamDB cookie storage: per-user (`%APPDATA%`) or per-session env var?
3. Should we offer Linux support (`steam_api.so`, no `.exe` to invoke)? Probably no for v1 — overlay itself is Windows-only.
4. When the game's `steam_settings/` already exists and looks complete, should we no-op or refresh icons / re-scrape hidden descs?
