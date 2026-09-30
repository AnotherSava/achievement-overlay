---
created: 2026-09-29 23:06:12
---

# Show an achievement list and completion dashboard (issue #8): a per-game list first, completion figures second, global rarity third

Plan for [issue #8](https://github.com/AnotherSava/achievement-overlay/issues/8). mohsinous asks for two things: a list per game showing which achievements are unlocked and which are locked, and a dashboard of games with their completion percentage. ant-sh replied against feature creep. His point is that a small download is the app's advantage over the alternatives, and he is fine with new features "as long as app stays let's say within 150 Mb". mohsinous then thanked him as if he were the maintainer, and ant-sh clarified "I am not Oleg, it's up to him". So the maintainer has not said anything on the issue yet, and the reply drafted with this plan (see **Open with the requester**) is the first word from him.

Every fact either feature needs is already on disk, and the app already turns most of it into display text for the Recent panel. So the work is mostly a new window plus two schema fields the app does not read yet. This is written for whoever implements it. Once shipped, the settled parts move to `docs/`: a new `usage/` page for the window, a bullet in `usage/tray-menu`, and the data rules in `development/gbe-reference`.

## Size, for ant-sh's concern

Measured on the v1.11.0 release assets and the files inside them:

- The self-contained zip is 72,714,582 bytes, and its exe is 175,778,181 bytes (175.8 MB) unpacked. That is already above ant-sh's 150 MB if he meant size on disk; he did not say whether he meant the download or the install. Almost all of it is the .NET runtime plus the WindowsDesktop framework (WPF and WinForms), bundled because neither publish trims. The app's own code is about 0.4 MB of it: `AchievementOverlay.dll` is 376,320 bytes, and the workflow does not set `EnableCompressionInSingleFile`, so the bundle stores it uncompressed.
- The framework-dependent zip is 273,965 bytes, 541,697 bytes unpacked. That figure measures an incomplete package: its `AchievementOverlay.deps.json` declares SharpCompress 0.50.4, but the "Package zips" step in `.github/workflows/build.yml` lists the framework-dependent files by name and leaves out `SharpCompress.dll` (2,461,696 bytes). With it, the framework-dependent build would be about 3.0 MB unpacked (computed, not measured on a fixed build). See the separate defect below; re-measure after the fix before quoting a framework-dependent size.
- Across every release since v1.4.0 the self-contained zip has stayed between 72.4 and 72.7 MB. The only real step was v1.3.0 to v1.4.0 (+3.06 MB), which added SharpCompress for the wizard's 7z download. Everything from v1.7.0 through v1.11.0 together added 26 KB.
- The two most recent windows each added 13–18 KB to the framework-dependent zip: v1.6.1 to v1.7.0 (`SettingsWindow` plus per-game overlay settings) +17.5 KB, and v1.9.1 to v1.10.0 (`DiagnosticReportWindow` plus `DiagnosticReport`) +13.1 KB. Their compiled XAML is 11,149 and 5,107 bytes.
- The app idles at about 23 MB working set (deployed build, measured after 46 minutes up). Every dialog is built on open and discarded on close (`OpenSettingsDialog`, `OpenDiagnosticReport`), so a closed dialog holds nothing. What WPF keeps cached after a first open is UNMEASURED.

What this feature needs is already inside every build: WPF's `ListBox`, `ProgressBar` and virtualizing panels ship in `Microsoft.WindowsDesktop.App`, and the icons are the ones each game's `steam_settings` already holds. **It adds no package and bundles no image.** The expected cost is in the range of the last two windows, tens of KB against either download. That is an estimate; building it is the only measurement. What would change it is a charting or grid package, or bundled artwork, and the plan uses neither.

The footprint rule for this feature is therefore: no new dependency, nothing read in the background while the window is closed, and no network call in Parts 1 and 2 (Part 3's rarity fetch is the one exception).

## What the app already has

### What GBE writes

Checked against gbe_fork `dev` (`dll/steam_user_stats.cpp`, `dll/steam_user_stats_achievements.cpp`).

- The `Steam_User_Stats` constructor loads the schema (`load_achievements_db`), then the GSE Saves file (`load_achievements`), then for every defined achievement runs `emplace("earned", false)` and `emplace("earned_time", 0)`. Achievements with a progress block also get `progress` and `max_progress` (issue #10's fields).
- GBE's `save_achievements` writes the whole object. Its callers are unlock, clear, `IndicateAchievementProgress`, and a stats store that needs a disk write.
- Consequences:
  - The unlock file does not exist until the first save. A game folder can exist with no unlock file: appid 1687950 on this machine has one.
  - Once written, the file lists **every** schema achievement, locked ones as `earned: false, earned_time: 0`.
  - GBE's `emplace` never removes, so a name dropped from the schema stays in the file. A schema change reaches the file only at the next save.
  - GBE running with no schema writes only the achievements the game touched. That is inferred from the code, not observed.
- Whether a stats store with no unlock can write the file first is UNMEASURED.

Measured on the maintainer's machine (one games root, GSE Saves at the default `%appdata%\GSE Saves`):

| appid | Game | Unlock entries | Earned | Schema entries | Names | Hidden |
|---|---|---|---|---|---|---|
| 1245620 | Elden Ring | 42 | 10 | 42 | identical | 36 |
| 1601580 | Frostpunk 2 | 79 | 13 | 79 | identical | 23 |
| 1966410 | Aphelion | 30 | 27 | 30 | identical | 15 |
| 2668510 | Red Dead Redemption | 51 | 17 | 51 | identical | 20 |
| 524220 | Nier Automata | 47 | 27 | 47 | identical | 30 |

Three more games (2378900, 2624870, 801800) have a schema and no GSE Saves folder: configured, never run. All 11 local schemas matched Steam's own achievement count exactly (`IPlayerService/GetGameAchievements`, 0 missing and 0 extra). Whether third-party schemas are complete in general is UNMEASURED.

### What self-describing writers write

The Goldberg Uplay R2 emulator (issue #5) writes the unlock file pre-populated: every entry with `earned: 0` and inline `displayName`/`description`, with `earned: 1` and `earned_time` added on unlock. The one real sample, ant-sh's AC Odyssey file from issue #7 (`tmp/replay/ant-sh-812140.json`), has 93 entries, 10 earned, named `1`..`93` against the schema's `001`..`093`. So for such a game the unlock file alone gives both the total and the earned count. Whether every Uplay emulator configuration pre-populates is UNVERIFIED: one sample plus the reporter's description. ant-sh also said the legitimate Ubisoft client writes only the achievement id, `earned` and `earned_time`; whether it lists locked entries is UNVERIFIED.

### What the schema carries that the app ignores

- **Gray icons.** Every local schema carries a locked icon, under two spellings: `icongray` in 10 files, and `icon_gray` in Red Dead Redemption (hash-named files under `img/`). GBE reads both, `icon_gray` first with `icongray` as the "old format" fallback (`load_ach_icon`, `get_achievement_icon_name`). Every referenced gray file exists on disk (42/42, 51/51, 47/47, 30/30, 79/79). The wizard writes `icongray` (`AchievementSchemaWriter.SchemaEntry.IconGray`) and downloads `<name>_gray.jpg` (`IconDownloader`). The app's `AchievementDefinition` parses only `name`, `displayName`, `description` and `icon`, and `ResolveIconPath` resolves only `icon`.
- **Hidden.** Every local schema carries `hidden`, as a string in 8 files and an int in 3 (Atomfall `bin/coldclient`, LiS Reunion `coldclient`, Red Dead Redemption). GBE normalises it to a string. The app's `AchievementDefinition` does not parse it. Self-describing unlock files carry no hidden flag.
- **No rarity and no progress.** No local schema has a percent, rarity or progress key.

### What the code already does

- The resolver `AchievementMetadata.ResolvePreferringSchema(state, definitions, metadataDir, name, language)` is the single place display text is chosen: schema first per field, inline text as filler, the selected language outranking both, and `FindDefinition` with its leading-zero fold. Its `state` parameter is nullable and it never checks `Earned`, so it already resolves a locked achievement, and one with no unlock entry at all when called with the definition's own name.
- **`FindDefinition`'s refusal rule:** an exact name match wins wherever it sits. Otherwise, when one unlock name folds onto two *differently spelled* schema entries, it returns null and logs a `WarnOnce`. Several unlock names folding onto the *same* definition (`1` and `01` against `001`) each match it; nothing refuses that case.
- The Recent panel's `AchievementHistory.GetRecent` is nearly a list builder. It walks every GSE Saves folder, parses with `ParseUnlockStates`, qualifies a game when `GameCache.LookupCached` knows it or the file `IsSelfDescribing`, loads definitions once through `GameCache.LoadDefinitions`, then skips `!state.Earned` and resolves the rest. For an unconfigured game an entry that resolves to nothing is dropped; for a configured game it is kept under its raw API name (`AchievementName = resolved?.DisplayName ?? achName`). It never iterates the schema, so **locked achievements with no unlock entry are enumerated nowhere in the app**.
- Unlock files are read three ways today:
  - `GetRecent` reads every GSE Saves path, so a game present under two paths contributes twice.
  - `TrayApplicationContext.ReadUnlockStates` returns the first copy that parses. An unreadable copy is logged at Warn, sets `unreadable`, and falls through to the next path; the method returns null only when no copy parsed. (When a later copy parses it returns those states with `unreadable` still set; its callers `CountEarnedAchievements` and the others use the states first.)
  - `AchievementWatcher` has its own reads.
- Nothing computes a total or a percentage. The only earned count is the private `TrayApplicationContext.CountEarnedAchievements` (0 for no file, null for an unreadable one), which feeds `TrackingConfirmation.ShouldNotify`.
- The report's `OpenDiagnosticReport` builds a **superset** of the games a list needs: every `GameCache.GetAll()` entry, plus every all-digits folder from `AchievementWatcher.GetExistingAppIdFolders()` looked up with `LookupCached` (which may be null). It checks neither that `achievements.json` exists nor `IsSelfDescribing`, because a report is about exactly the odd folders a list leaves out. The `DiagnosticGameChoice` it builds is a plain `{AppId, Game}` with a `ToString()`.
- The schema loader `GameCache.LoadDefinitions` returns null both for a missing schema and for an unreadable or malformed one (it logs the latter at Warn). Callers cannot tell the two apart.
- The popup's `NotificationWindow.LoadIcon` (decode at render size, frozen) and `CreateDefaultIcon` (the goldenrod star fallback) are both private.

## Part 0: shared pieces, before any window

Each of these has a consumer in Part 1, so none ships alone.

### One per-game builder

Add a pure function, working name `GameAchievements.Build(definitions, metadataDir, states, language)`, returning one row per achievement and a summary. It calls `FindDefinition` and `ResolvePreferringSchema` and restates neither. The Recent panel's `GetRecent` becomes a caller of it, filtered to earned rows, so the Recent panel, the list and the dashboard cannot disagree about an achievement's text, icon or earned state.

Row fields: API name, resolved display name and description, colour icon path, gray icon path, earned, earned time, hidden, and whether the row matched a schema definition. Progress and max progress are **not** pre-added: a field no step reads does not get one. Whichever of #8 and #10 ships second adds them together with the list's bar (see Integration with issue #10).

### Resolution logic

1. Map each unlock entry to a definition with `FindDefinition`. One unlock name folding onto two differently spelled definitions matches nothing, as in the popup. Several unlock names may map to one definition; which state that definition takes is an open decision (recommendation: earned beats unearned, the earliest `earned_time` among earned copies wins, and the definition counts once).
2. For each definition, take its matched state, or none.
3. Resolve text with `ResolvePreferringSchema(state, definitions, metadataDir, name, language)`, where `name` is the unlock entry's name when there is one (so a folded match resolves as it does in a popup) and the definition's name otherwise. The language is `AppConfig.Language`, the same as the popups, with no switch in the window.
4. An unlock entry matching no definition is an **extra**: a stale name GBE kept after a schema change, a self-describing entry, or a refused fold. Extras keep today's Recent-panel behaviour. For a configured game an extra is listed even when it resolves to nothing, under its raw API name and marked as not in the schema. For an unconfigured self-describing game an entry is listed only if it resolves (`ResolvePreferringSchema` returns null when there is neither a definition nor inline text), as `GetRecent` does now.
5. A refused fold is the one case where the list and the popup differ in effect. In the popup it costs only text and icon. In the list the earned unlock matches no definition, so both colliding definitions render as locked while the unlock appears as an extra. This needs two differently spelled schema entries folding onto one form, which no measured schema has; under the realism standard the plan accepts it as described rather than adding handling.
6. Summary:
   - **Schema present:** total is the definition count, earned is the count of earned definitions. Whether earned extras count toward the total is an open decision below.
   - **Schema present, no unlock file:** 0 earned of N. This is the configured-but-never-run case.
   - **Schema unreadable or malformed:** total unknown, shown as unknown, never replaced by the unlock file's count and never treated as "no schema". This needs `LoadDefinitions` (or a sibling the builder calls) to report unreadable separately from missing, which it does not today.
   - **No schema, self-describing file:** total and earned both come from the unlock file, and the summary says so, since that total is whatever the emulator wrote.
   - **No schema, plain GBE file:** the game is skipped, as `GetRecent` skips it today. Its rows could only be raw API names.
   - **Unreadable unlock file:** earned is unknown, never 0, following `CountEarnedAchievements`. The row set is the schema's, with every earned state unknown.

### One unlock-file reader and one game set

- Pick one rule for a game whose folder is under two GSE Saves paths and use it everywhere this feature reads. `ReadUnlockStates` takes the first copy that parses, falling through an unreadable one; `GetRecent` reads every copy. The watcher's `WarnAboutGamesInSeveralPaths` already warns about the setup. Moving `ReadUnlockStates` out of `TrayApplicationContext` into the builder's module and routing `GetRecent` through it would give the Recent panel the first-readable-copy rule too, which is a visible change for anyone with that setup. Which rule wins hasn't been chosen.
- Share the raw candidate enumeration, not a finished game set. The report and the list need different sets: the report wants every configured game plus every appid folder; the list wants only games that qualify under `GetRecent`'s rule (`LookupCached != null || IsSelfDescribing(states)`), which needs the unlock file read and parsed. So extract the enumeration from `OpenDiagnosticReport` into one method returning candidates, and give the list its own qualification step on top of it.

### Schema fields

- **`AchievementDefinition.IconGray`**, reading `icon_gray` first and `icongray` second, the same order as GBE. Resolved by a gray variant of `ResolveIconPath` that shares its search (the verbatim path, then `achievement_images/<name>`, each tried with the `.jpg`/`.png`/`.bmp`/`.ico` variants in `TryResolve`) rather than copying it. When a schema has no gray icon, the fallback hasn't been chosen: runtime desaturation of the colour icon (`FormatConvertedBitmap` to a gray format; whether it keeps alpha is UNVERIFIED) or the colour icon at reduced opacity.
- **`AchievementDefinition.Hidden`**. A converter in the style of `FlexibleBooleanConverter` would not be enough on its own: that converter throws `JsonException` on a value it cannot read, `ParseDefinitions` deserialises the whole list in one `JsonSerializer.Deserialize` call, and `LoadDefinitions` then drops the whole schema. A converter returning `false` instead would silently unmask a hidden achievement. So `Hidden` is a `bool?` whose converter accepts `"0"`/`"1"`, `0`/`1` and booleans, reads anything else as null (unknown) and logs it once; the list treats unknown as hidden. The alternative is to make `ParseDefinitions` convert entries one at a time as `ParseUnlockStates` does, which costs the malformed entry rather than the flag. This is the consumer the memo "Parse the schema's hidden field, and ship it with a consumer" asks for. That memo's other consumers (the `GameCache` "Schema:" line saying how many blank descriptions are hidden, and the report's per-part description) can ship in the same change.

### Which steam_settings copy supplies the schema

The cache's `GameInfo.MetadataPath` is the `achievements.json` in the deepest `steam_settings` folder only. Atomfall's deepest copy has 25 blank descriptions where the root copy has all 54 filled, so a list, which shows every description at once, puts that defect in front of the user for the whole game. The memo "Fold the achievement schema across all of a game's steam_settings folders" is the fix, folding across `GameInfo.SettingsDirs` with first-definition-wins as `GbeOverlaySettingsReader` does. It should ship before or with Part 1. It also decides where the total comes from when two copies disagree on the achievement set; the local copies measured so far agree.

## Part 1: the per-game achievement list

### Window

A new WPF window, working name `AchievementsWindow`, built the way `SettingsWindow` and `DiagnosticReportWindow` are: `ThemeMode="System"`, `DialogStyles.xaml`, and `DialogChrome.ApplyThemeBrushes`, `LoadWindowIcon` and `ClampToScreen` in the constructor. Any new colour (a locked-row tint, a bar track) goes into `ApplyThemeBrushes`, not the window's XAML. It follows the Windows theme, not `PopupPalette`, which belongs to the popup over the game. The one borrowed piece is the fallback icon, which moves out of `NotificationWindow` with `LoadIcon` into a shared helper; its ring colour must read against `CardBackground`.

Two panels side by side, the list and the dashboard in one window:

- **Left, the games.** One row per game: its name and a summary chip (earned/total and a percentage, from the builder's summary). Above the rows, a search box (matches the game name), a sort selector, and filter toggle buttons.
  - Sort: **last unlock** (the default), **name**, **completion %**. Nothing else.
  - Filters are pressed/unpressed toggle buttons, not a dropdown. The first one: **exclude games with nothing unlocked** (which also hides never-run games). Filters are optional for the first version; if cut, the toolbar keeps the search box and the sort selector.
  - Selecting a row shows that game in the right panel.
- **Right, the selected game's achievements.** A header plus the rows described below, with the same kind of toggle buttons: **exclude locked** and **exclude hidden**.

The right panel follows Steam's personal achievement page, which is the design users know:

- A header: "16 of 19 (84%)" and a bar, from the builder's summary. For a self-describing game without a schema, the header says the total is the emulator's. For an unreadable unlock file it says the count is unknown; for an unreadable schema it says the total is unknown.
- Unlocked rows, newest first, each with colour icon, name, description and unlock date.
- Locked rows in schema order, each with gray icon, name and description, no date.
- Hidden locked rows. The **exclude hidden** toggle removes them; with it pressed, one line still says "N hidden achievements remaining", as Steam does, so the count in the header adds up. Whether the toggle starts pressed hasn't been chosen. The toggle covers only schema-backed games: a self-describing game with no schema has no hidden flag anywhere, so its locked rows are all shown, because nothing says to hide them.
- Extras below the locked rows, marked as not in the schema.

Every row renders the same way whatever wrote the file; only the header wording differs by source.

### Scale

The row list is the pane's own scroller: `ItemsSource` with a `DataTemplate`, not inside an outer `ScrollViewer` or `StackPanel`, with `VirtualizationMode="Recycling"` and `ScrollUnit="Pixel"`. An outer scroller gives the list infinite height and realises every row. This is the app's first data-bound control; every existing window sets properties from code-behind. Icons decode at row size, freeze, and are cached by path for the window's lifetime. Whether the Fluent `ListBox` template keeps virtualization in .NET 10 is UNVERIFIED, and the largest achievement count among real users' games is UNMEASURED (Steam allows about 5000).

### Opening it

- **A single left-click on the tray icon** toggles it. Left-click is unused today (no `DoubleClick` or `MouseClick` handler on the `NotifyIcon`), and opening the main window is what Windows users expect it to do. Three states, not two:
  - closed: open it;
  - open but not in front (behind the game or another window): bring it to the front — hiding a window the user clicked to see would read as broken;
  - open and in front just before the click: close it.

  "In front just before the click" cannot be read at click time: clicking the tray moves focus to the taskbar before the handler runs, so the window is never active by then. The window records when it last lost activation (`Deactivated`), and a click within about 200 ms of that counts as in front. The threshold is a guess to tune on the real taskbar.
- **A tray menu item** as well, the way **Settings…** opens, so there is a keyboard route and something to name in the docs. Its name and position haven't been chosen; "Achievements…" directly under "Show recent achievements" is the obvious slot.
- Read on open, not in the background: the window builds its rows when it opens, as `AvailableLanguages` already walks every schema and unlock file when Settings opens. Nothing is added to the idle app.
- Lifetime: every existing *dialog* is modal. `SettingsWindow`, `DiagnosticReportWindow` and `AddGameForm` run through `ShowDialog` in `OpenSettingsDialog`, `OpenDiagnosticReport` and `OpenAddGameDialog`, and `ExitApplication`'s close list and `CompletePendingExit`'s null checks cover them. The popups (`NotificationQueue`) and the Recent panel (`RecentAchievementsDisplay.Show`) are already modeless WPF windows outside that bookkeeping, but hold no state worth closing cleanly. **This window is modeless** (`Show()`), because the tray-click toggle and the tray menu keep working while it is open, which means Settings or Add game can be opened on top of it; a modal window would run a nested loop that every other open path has to respect. Leaving it open beside a game is not the reason: most players run the game full screen on one display. That makes it the first modeless window that needs its own Exit design: `ExitApplication` must close it, and the comment there explains the nested-dispatcher problem modal dialogs avoid. A second **Achievements…** click activates the open window rather than opening another.
- **It updates live.** `AchievementWatcher.NewAchievement`, marshalled to the dispatcher, rebuilds the affected game's summary on the left and, when that game is selected, its rows on the right, keeping the selection, the search text and the toggles. A rescan of `GameCache` (Add game…, a Settings change) rebuilds the game list. Once #10 ships, progress events do the same.
- It must not block a modal dialog opened while it is up, and a modal dialog must not block it; how WPF and the WinForms-owned dialogs interact here is UNMEASURED.
- A second hotkey would need its own id, config key, Settings card and `WarnIfShortcutUnavailable` path. It is not part of Part 1 unless the requester asks.

### Which games appear

The candidates from the shared enumeration, filtered by `GetRecent`'s qualification rule: configured games (including never-run ones at 0 of N) and self-describing games without a schema (named by appid, no icons). A plain GBE file with no schema is left out, as in the Recent panel. The synthetic "Gearhead" (tracking configured) and "Achievement Connoisseur" entries are Recent-panel constructs; they appear in no list and count toward no total.

## Part 2: the completion dashboard

The dashboard is the window's left panel (see **Window**): every game with its summary, searchable, sortable and filterable. This answers the request with no second window. Parts 1 and 2 can still ship separately: Part 1 alone is the two panels with plain name rows on the left, and Part 2 adds the summary chips, the sort selector and the filter toggles.

- The game list needs an `ItemTemplate` over data. The report window's rail is literal `ListBoxItem`s, so the `NavItem` style is reused and the rows are not.
- Opening it reads every game's schema and unlock file. For about 100 games that is the same order of work `AvailableLanguages` does, but it is UNMEASURED. The per-game rows can be built lazily on selection; only the summaries are needed up front.
- Search, sort and the toggles are view state, not settings: nothing is written to `config.json`. **Closing discards the window, not the view state.** The second click closes rather than hides, so the decoded icons are freed and the idle app holds nothing extra; the selected game, the search text, the sort and the toggles are kept in memory for the session, so reopening looks the same as a hide would have. They do not survive a restart.
- A summary across all games (games completed, total achievements earned) is further than the request goes and is not planned.

## Part 3: global rarity

Each achievement's global unlock percentage needs Steam data; no local schema carries it. Checked 2026-09-29, with no key:

- `ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid=<id>` returns `{name, percent}` pairs, with `percent` a string, and answered HTTP 200 without a key for appid 1245620. An unknown appid returns HTTP 403.
- `IPlayerService/GetGameAchievements/v1/?appid=<id>&language=<lang>` returns the same `player_percent_unlocked` together with text, icon hashes, `hidden` as a bool, and `progress_type` with `min_progress_int`/`max_progress_int`. Without `language`, or for an unknown appid, it returns `{"response":{}}`. It is undocumented by Valve.

Part 3 is planned whatever the requester answers: a rarity figure beside each achievement is worth having for everyone, and the data is one keyless call per game. Which of two routes fetches it hasn't been chosen:

1. **Fetch at Add game time** and store it beside the schema. Keeps the runtime offline outside the wizard, which already calls Steam. It covers only wizard-configured games, the figures go stale, and writing an extra key into GBE's `achievements.json` assumes GBE ignores unknown keys, which is UNVERIFIED for every build. A sidecar file in `steam_settings` avoids that.
2. **Fetch when the window opens**, cached per appid. Covers every game. The app already calls the network from the Add game wizard (`SteamWebApi`, `IconDownloader`, `GbeBinaryManager`, `AppIdResolver`, `SteamDbScraper`), but this would be the first call made outside that user-initiated setup flow, just by opening a window. That is a change of stance, and the reply to the issue says so plainly rather than slipping it in.

Everything Part 3 could add beyond a percentage (rarity tiers, playtime, library art, an in-game list overlay) stays out of scope. Other tools already do those, at Windows download sizes (latest releases as of 2026-09-29) of 127–179 MB: [Achievement Watcher Next](https://github.com/Shirowwww/Achievement-Watcher-Next) v3.10.9 (setup 127.5 MB, portable 173.4 MB), Hydra v4.1.5 (178.8 MB setup), and Playnite 10.62 (151.9 MB installer) with the SuccessStory extension (4.4 MB). Matching them is what ant-sh is warning against. The only lightweight GBE tool, Achievement Watchdog, has a terminal viewer and a README asking for a GUI one. A small list that works offline, with rarity as the one networked extra, is the gap this app fills.

The same endpoint returning hidden descriptions is recorded in the issue #10 plan; it concerns the wizard, not this feature.

## Integration with issue #10

Issue #10 adds `progress` and `max_progress` to `AchievementUnlockState` through `FlexibleInt64Converter`. Neither feature pre-adds fields for the other. Whichever ships second adds progress and max progress to the builder's row in the same change as the list's bar:

- A locked row with `max_progress > 0` shows "17 / 25" and a Fluent `ProgressBar` under its description.
- An earned row shows no bar, since GBE leaves progress at the last value below max on unlock (typically 24/25), which would read as unfinished.
- Hidden locked rows show no bar. Steam reportedly does the same; that is UNVERIFIED first-hand.
- The popup's bar colours come from `PopupPalette`; the list's come from `DialogChrome`, because the two surfaces follow different themes.

No local unlock file carries progress yet, so the bar can only be tested against fixtures until #10's wizard work writes progress blocks.

## Docs and screenshots

- A new page under `docs/pages/usage/` and a bullet in `tray-menu.md`, which mirrors the menu order.
- Both `docs/pages/usage.md` and `docs/pages/installation.md` say the app has no main window. This window is the first thing close to one, so both need rewording.
- If the feature list gains a bullet, README and `docs/index.md` change word for word together.
- A `screenshots.json` entry and a `capture/<id>.sh`/`.ps1` pair for the window, driving the rail through `Select-NavPage`. The `tray-menu` shot goes stale when the menu gains an item.

## Tests

- **Builder** (pure, no window):
  - Schema plus a GBE file listing every achievement gives N rows, earned and locked, with totals matching the table's counts for a fixture shaped like Elden Ring.
  - Schema plus no unlock file gives 0 of N.
  - An unreadable unlock file gives an unknown earned count, never 0.
  - An unreadable schema gives an unknown total, never the unlock file's count, and is not treated as self-describing or skipped.
  - A self-describing file with no schema gives rows and totals from the file, with no row masked as hidden; one shaped like ant-sh's AC Odyssey file with its `001` schema matches all 93 by folding.
  - A stale earned unlock entry absent from a configured game's schema is an extra listed under its API name, and follows whatever rule is chosen for totals.
  - One unlock name folding onto two differently spelled definitions matches nothing: both definitions stay locked and the entry is an extra.
  - Two unlock names (`1` and `01`) folding onto one definition give that definition the chosen state and count it once.
  - Text for a locked row with no unlock entry equals what `ResolvePreferringSchema` returns for the definition's name, including a language only the schema carries.
  - `GetRecent` returns the same entries before and after being routed through the builder, apart from the chosen two-paths rule.
- **Schema fields:** `hidden` as `"1"`, `1`, `true`, and a malformed value (read as unknown, logged, definition and schema kept, list masks it); `icon_gray` preferred over `icongray`; each alone.
- **Tray click:** the choice between open, bring to front and close is a pure function of whether the window exists, when it last lost activation, and the click time, and is tested as one: closed → open; deactivated long before the click → bring to front; deactivated within the threshold → close.
- **Progress** (in whichever change adds the fields): a locked row with progress carries it; an earned row does not show it.

## Open with the requester

A reply drafted in the session that wrote this plan introduces the maintainer, thanks mohsinous, states the plan and the size figures for ant-sh (download and unpacked), describes the two-panel window opened by a tray click, says GBE is supported first and other emulators on request, says each achievement will show its global rarity from Steam, and ends by asking for feedback and questions rather than a list of specific ones. Posted 2026-09-30 as [this comment](https://github.com/AnotherSava/achievement-overlay/issues/8#issuecomment-5906048679). Check the issue for answers before starting.

## Separate defect found during research

The framework-dependent release zip omits `SharpCompress.dll`, which its `deps.json` declares; the file list is in the "Package zips" step of `.github/workflows/build.yml`. The Add game wizard's 7z extract (`GbeBinaryManager`, `SharpCompress.Archives.SevenZip`) therefore probably fails in that build; the runtime failure is UNVERIFIED. It is independent of #8 and belongs in its own commit.
