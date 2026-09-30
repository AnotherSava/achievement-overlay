---
created: 2026-09-29 21:47:29
---

# Show achievement progress (issue #10): milestone popups, and wizard-written stat bindings

Plan for [issue #10](https://github.com/AnotherSava/achievement-overlay/issues/10). Collectible achievements (destroy 25 ships, burn 40 wood) are the ones a player grinds toward, and today the overlay says nothing until the last step. GBE already records the running count in the unlock file, so most of the popup work is on our side. The wizard work is what makes the count exist at all for a game set up through **Add game…**. This is written for whoever implements it. Once shipped, the settled parts move to `docs/`: `usage/settings` and `development/gbe-reference`.

## What GBE writes

Checked against gbe_fork `dev` (cae0b81a). gse_fork `dev` is the same in every region below.

A progress achievement is one whose `steam_settings/achievements.json` entry has a **progress block**:

```json
"progress": { "min_val": "0", "max_val": "25", "value": { "operation": "statvalue", "operand1": "FightersDestroyed" } }
```

`operand1` names a stat, and that stat must also be defined in `steam_settings/stats.json` (see **Writing format** below). With both in place, GBE writes into `%appdata%/GSE Saves/<appid>/achievements.json` at these points:

- **At load**, the `Steam_User_Stats` constructor (the `defined_achievements` loop in `steam_user_stats.cpp`) adds `progress = min_val` and `max_progress = max_val` to every entry with a progress block. They reach disk at the next save, so every progress achievement shows up as `progress: 0, max_progress: N` long before any real progress. An empty-string `min_val` skips this step, because `std::stoul("")` throws.
- **On every stat change** (`set_stat_internal`, int and float, in `steam_user_stats_stats.cpp`), GBE checks every trigger bound to that stat twice:
  - `should_unlock_ach`: when stat ≥ max, the achievement unlocks.
  - `should_indicate_progress`: when stat < max, GBE stores the new progress and rewrites the whole file.

  The progress write is gated by `stat_achievement_progress_functionality` (on by default). `save_only_higher_stat_achievement_progress` (also on by default) skips values that aren't above the stored one. A 0→25 achievement can therefore cause up to 24 file rewrites.
- **When the game calls `IndicateAchievementProgress`** (`steam_user_stats_achievements.cpp`), GBE writes progress even for achievements without a progress block. It does so only while the achievement is unearned and below max.
- **On `ResetAllStats(true)`**, progress returns to `min_val`, so progress can go down.

Values are always non-negative JSON integers (`uint32`), and float stats are truncated. On unlock, progress stays at the last value below max (typically 24/25), so `earned: true` with `progress < max_progress` is the normal final state. GBE never writes `earned: true` with `earned_time: 0`: `set_achievement_internal` stamps the current time. GBE's own progress popup is off by default (`disable_overlay_achievement_progress`).

## Part 1: progress popups

### Watcher (`AchievementWatcher`)

- Parse `progress` / `max_progress` on `AchievementUnlockState` with the existing `FlexibleInt64Converter`. An entry counts as a progress entry when `max_progress > 0`. A malformed progress value costs the progress only, never the unlock: it must not drop the whole entry in `ParseUnlockStates`.
- Keep the last-seen progress per `appid|name` in a second record next to `_seenAchievements`.
- **Keep `main`'s seeding rule and extend it.** For a folder that appears after `Start()`, record only unlocks older than `_startedAtUnix`, so a game's first-ever unlock still pops. Record current progress values at seed time so a restart does not replay them.
- Raise a progress event only when an unearned entry's progress rises above its recorded value. When an entry is earned in the same write, raise only the unlock, and update the progress record along with it.
- **A decrease updates the record.** That covers a reset: the next rise is compared against the new low value, not the old high one.
- **The first sighting of a key counts as a rise from `min`.** A key that appears mid-session (GBE's load-time write reaching disk, or a first `IndicateAchievementProgress` call) is compared against 0 rather than recorded silently. The load-time `0 / N` write itself is not a rise, so it shows nothing.
- The progress record is keyed per game, not per folder, the same as `_seenAchievements`. A game with unlock files under two GSE Saves paths is therefore judged across both. `WarnAboutGamesInSeveralPaths` already covers that case and needs no change.

### Popup frequency: at most X per achievement

A setting caps the number of progress popups one achievement may show. The popups sit at milestones spread evenly over its range: with X = 4 and max 100, they come at 25, 50 and 75, and the unlock popup is the fourth.

- A popup fires when progress crosses a milestone it hadn't crossed before.
- Several milestones crossed in one write give one popup, showing the current value.
- The record of crossed milestones follows the same seeding and reset rules as the progress record.
- X = 0 turns progress popups off, so no separate on/off toggle is needed. The default for X hasn't been chosen.

**When a newer progress item for the same achievement arrives while one is still queued, it replaces the queued one** instead of adding a second. The queue then never shows a stale "5 / 25" before "20 / 25", and a real unlock never waits behind a backlog of progress popups.

### Queue and popup

- `NotificationQueue` gets a progress item kind. It resolves text through the same `AchievementMetadata.Resolve` path as unlocks, so text, language and schema-versus-inline precedence can't diverge between the two. A cache miss must not trigger a full `GameCache` rescan for every progress event: use `LookupScanningOnce` or equivalent.
- Progress popups get their own sound setting next to the unlock sound, with three options: **Off**, **Same as unlock**, and **Custom file**. The default is Off. The master switch `soundEnabled` still silences both. There is no per-game override for this sound: GBE's overlay only has `overlay_achievement_notification.wav` and `overlay_friend_notification.wav`, so no game ships a progress sound.
- The popup shows the name, the description, "17 / 25", and a bar under the description. Beyond the bar, it needs one more thing that sets it apart from an unlock popup (a title prefix, or the gray icon), since the two otherwise look the same.
- The bar colours come from `PopupPalette`, derived against the background with a contrast floor as `IconRing` is, not hard-coded. Hard-coded colours can disappear against a custom background.
- A separate setting lists in-progress achievements (current "N / M" and bar) in the Recent panel, next to the other recent-achievements settings. Its default hasn't been chosen.

## Part 2: the Add game wizard writes progress bindings

### Why the wizard needs it

Today `AchievementSchemaWriter.Build` writes every achievement from `GetSchemaForGame` (name, text, hidden flag, icons) with no progress block, and `GbeConfigGenerator` writes no `stats.json`. With no stats defined and `allow_unknown_stats` at its default (off), `set_stat_internal` returns failure for every stat before it reaches the trigger code. That has three effects on a wizard-configured game:

- It gets no progress at all.
- An achievement the game unlocks only by pushing its stat to the threshold, with no `SetAchievement` call, never unlocks.
- Every stat value the game saves is rejected.

This hasn't been reproduced on a real game yet. Before calling it a live bug, run one wizard-configured game with GBE's debug log on and look for failing `SetStat` calls.

### Sources

None of the wizard's current sources links a stat to an achievement:

- `GetSchemaForGame` lists achievements and stats separately. Checked on 1176710 and 427520: no `min_val`, and nothing ties `FightersDestroyed` to `Fighters25`.
- The SteamDB stats page has the same two separate tables. Its `--ach-progress:55.2%` is the global unlock rate, not a binding.

These sources can fill the gap:

1. **`IPlayerService/GetGameAchievements/v1` (detection).** Needs no key and no login:

   ```
   GET https://api.steampowered.com/IPlayerService/GetGameAchievements/v1/?appid=<id>&language=english
   ```

   - **What it returns:** a progress achievement carries `progress_type: 1` with `min_progress_int` / `max_progress_int`. The others have `progress_type: 0` and no min/max. It does not name the bound stat.
   - **`language` is required.** Without it the reply is `{"response":{}}`, so an empty reply means "unknown", never "no progress".
   - **Checked against the Steam cache:**
     - Space Crew (1176710): 11 of 11.
     - Factorio (427520): 21 of 21.
     - Through the Ages (758370): 88 of 89. The extra cached one binds `feat_internet` to a stat the schema never defines, and Steam's own profile page draws no bar for it either.
     - Three games without progress blocks (292030, 1063580, 238960): 0.
2. **Nemirtingas/games-infos-datas (binding).** Fetch `https://emulator.servegame.com/games-infos-data/steam/<appid>/achievements_db.json` (about 50 KB) and `stats_db.json` from the same folder.
   - **What it has:** progress entries carry `"stats_thresholds": [{"stat_name": "FightersDestroyed", "min_val": 0, "max_val": 25}]`. `stats_db.json` gives each stat's `name`, `type` (`int`/`float`/`avgrate`), `default` (as a number), and more.
   - **Fetch it per game at run time.** The GitHub repo is 1.6 GB with over 100,000 files and has no licence, so bundling it would mean redistributing it. It has also been frozen since 2026-05-15; its last commit points the README at the site above.
   - **Treat it as best-effort.** The site was updating daily when checked (`steam_metadata.json` last modified 2026-09-30) and returns 404 for an unknown appid. It runs on a personal dynamic-DNS host, so any failure, any 404, or any disagreement with source 1 on count or max means "unknown".
   - **Fallback:** GitHub raw (`raw.githubusercontent.com/Nemirtingas/games-infos-datas/main/steam/<appid>/...`), which is stale.
3. **The local Steam cache (binding, offline).**
   - **Location:** `<SteamPath>/appcache/stats/UserGameStatsSchema_<appid>.bin`, with `SteamPath` from the registry value `HKCU\Software\Valve\Steam\SteamPath`.
   - **Format:** binary KeyValues, with type bytes 0x00 subtree, 0x01 string, 0x02 int32, 0x03 float and 0x08 end.
   - **Layout:**
     - Achievements are at `<appid>/stats/<statId>/bits/<n>`, with `name`, `display{...}`, `bit`, and an optional `progress` block in exactly GBE's shape.
     - Stats are at `<appid>/stats/<statId>`, with `name`, `type`, `min`, `max` and `Default`.
   - **Values:** `min_val` / `max_val` can be strings, ints, or `""`.
   - **Availability:** the file exists only for games this machine's Steam client has run at some point, and it survives uninstall. A repack that never ran under Steam has none. This machine had 39 such files, 9 of them with progress blocks.
4. **Steam CM `ClientGetUserStats` (out of scope for now).** This is what `generate_emu_config` used (gbe_fork_tools, `generate_emu_config_old/generate_emu_config.py`, functions `get_stats_schema` and `generate_achievement_stats`). Its response carries the same blob as source 3, for any game. It needs a real Steam login with Steam Guard, and an anonymous login got no reply. Adopting it means adding SteamKit2 and handling the user's Steam password, which is a separate decision.

Steam Community profile achievement pages (`/profiles/<id>/stats/<appid>/?tab=achievements`) also show "0 / 25" bars anonymously for any profile. They add nothing over source 1 and would need HTML scraping.

### Resolution logic

1. Call source 1.
   - No progress achievements: write the config as today, and stop.
   - Empty or failed reply: treat it as unknown, write as today, and log it.
2. If it finds progress achievements, take the bindings from source 3 when the cache file exists, otherwise from source 2.
3. Accept a binding source only if its set of progress achievements and their max values agree with source 1. Otherwise treat it as unavailable. Source 1 is Steam's current schema, and the cache can be older.
4. With no usable binding, write as today, and have the wizard log say that the game has N progress achievements whose counters won't be tracked. That count is also what would justify offering the credential route (source 4) later.

### Writing format

- **The progress block** in `achievements.json` is `{"min_val": "<min>", "max_val": "<max>", "value": {"operation": "statvalue", "operand1": "<stat name>"}}`. Write the values as strings, as `generate_emu_config` does. Write an empty `min_val` as `"0"`: otherwise `std::stoul("")` throws at load and the initial `progress` / `max_progress` never appear.
- **`stats.json`** is a new file with one entry per stat: `{"name", "type", "default", "global"}`.
  - Write `default` and `global` as strings (`"0"`), not as the numbers `stats_db.json` holds. `parse_stats` in `settings_parser.cpp` reads both with `value(..., std::string)`, which throws on a JSON number, and the stat is then skipped.
  - `type` must be `int`, `float` or `avgrate`, or the stat is skipped.
- Write every stat the source defines, not only the ones bound to progress, so that the game's other `SetStat` calls stop failing.

### Hidden descriptions from the same endpoint

`GetGameAchievements` returns `localized_desc` for hidden achievements, in the requested language: all 26 hidden ones in The Witcher 3 had text, and all 31 of AC Odyssey's in both `english` and `russian`. `GetSchemaForGame` and SteamDB can't do that; SteamDB is English-only. It could replace the SteamDB/Firecrawl scrape (`SteamDbScraper`) for hidden descriptions, remove the need for a Firecrawl key, and lift the localisation ceiling that the memo "Fetch achievement text in several languages in the Add game wizard" runs into.

## Report a problem carries stats.json

Add the game's `steam_settings/stats.json` to the report (`DiagnosticReport.Collect`, next to `Schema`), read from the same folder as the schema and recorded as missing when absent. Once progress depends on stats, a missing or malformed `stats.json` is the first thing a progress bug report needs, and today it has to be requested by hand. The file holds stat names, types and defaults, so it needs no redaction beyond the existing path collapsing. `docs/pages/troubleshooting.md` lists the report's parts and gets the new line.

## Tests

- **Watcher:**
  - The first-ever unlock in a folder that appears after `Start()` still raises an event. Existing `AchievementWatcherTests` cases guard this; an early prototype broke eight of them.
  - A progress rise raises an event; an unchanged value and a first-seed value do not.
  - Milestone crossings respect X, and several milestones crossed in one write give one event.
  - A decrease resets the record.
  - An unlock and a progress change in the same write give only the unlock.
- **Queue:** a newer progress item replaces the queued one for the same achievement, and unlock items are untouched.
- **Palette:** the bar colours clear the contrast floor on the default background and on a light one.
- **Wizard** (the pure parts, in `tests/GbeConfig/`):
  - Parse a `GetGameAchievements` reply.
  - Parse the binary KeyValues cache blob.
  - Convert Nemirtingas data into progress blocks and `stats.json`, with string values and an empty `min` written as `"0"`.
  - A source that disagrees with source 1 is rejected.

## Open with the requester

A reply drafted in the session that wrote this plan asks which game this is and what writes their file: their example has `earned: true` with `earned_time: 0`, which GBE doesn't write. It asks for a **Report a problem…** file for that game, which carries the unlock file and `steam_settings/achievements.json`, plus `steam_settings/stats.json`, which the report does not include. The reply had not been posted when this plan was written.
