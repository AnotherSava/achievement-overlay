# CODEX/RUNE emulator support

Tracks [issue #11](https://github.com/AnotherSava/achievement-overlay/issues/11): "[Feature request]
CODEX/RUNE achievements.ini support", filed 2026-10-07 by `ant-sh`.

## Why

Some games run the CODEX or RUNE Steam emulator, not GBE. The emulator
records unlocks in an `achievements.ini` the app never reads, so such a game shows nothing. The
reporter bridges the gap with a PowerShell watcher that converts each changed `achievements.ini`
into a GSE Saves `achievements.json`; the request is to make that bridge unnecessary.

Two things stand between a CODEX/RUNE game and a popup, and this plan covers both:

- **No schema.** The unlock file names achievements and nothing else, so the game needs a
  `steam_settings/achievements.json` like any GBE game. The Add game wizard is the tool that writes
  one, but today it also replaces the game's `steam_api64.dll` with GBE, which removes the emulator
  the request is about.
- **No reader.** The watcher, the Recent panel, the tracking confirmation and the diagnostic report
  all read `achievements.json` and nothing else.

The anadius LSX emu (EA games) writes the same INI format, but it is out of scope for now.

## What the emulator writes

From the local test bed, cross-checked against the parsers in xan105's Achievement Watcher, Hydra and
BATorrent. Everything about the test bed's own games — names, folders, hashes, and how each one is
used below — is in `.claude/memory/codex-rune-local-test-bed.secret.md`, which transcrypt stores
encrypted:

- **Location.** `%PUBLIC%\Documents\Steam\CODEX\<appid>\` or `...\RUNE\<appid>\`, created on the
  game's first launch, alongside `leaderboards.ini`, an optional `stats.ini` and cloud save folders.
  The path is fixed: `steam_emu.ini` has no key to move it. Older CODEX releases also used
  `%APPDATA%\Steam\CODEX`; nothing in the test bed does.
- **The file.** ASCII, CRLF, no BOM. One section per earned achievement, named with the Steam API
  name, plus a `[SteamAchievements]` index that no parser uses:

  ```ini
  [Achievement01]
  Achieved=1
  CurProgress=0
  MaxProgress=0
  UnlockTime=1700000000

  [SteamAchievements]
  00000=Achievement01
  Count=1
  ```

- **Order.** Sections are sorted by name, not by unlock order, so the emulator rewrites the whole file
  rather than appending. Whether it rewrites in place, through a temp file and a rename, or by
  delete-and-create is **not measured**; no source documents it.
- **Quirks other parsers handle.** CODEX's Gears 5 and Gears Tactics write unlocked achievements with
  no `Achieved` key and `CurProgress` equal to a non-zero `MaxProgress`. `UnlockTime` can be 0.
  `Achieved=0` entries can carry progress.
- **The game folder.** A `steam_emu.ini` (`[Settings] AppId=…`) sits beside a `steam_api64.dll` of
  about 400 KB, with `*.rne` (RUNE) or `*.cdx` (CODEX) payloads next to it. The loader carries
  Valve's version info and no readable marker of its own.

Several games in the test bed have an `achievements.ini` that was never rewritten after creation,
although `stats.ini`, leaderboards or save files show the games were played under the emulator. The cause is not identified. The consequence for this feature is that
reading RUNE's file only helps where RUNE records unlocks, which is why D1 keeps a way to replace it.

## Decisions

### D1. What the wizard does with a CODEX/RUNE game

Options:

1. **Keep the emulator, write only the schema.** `steam_settings/` gets `achievements.json`, the icons
   and `steam_appid.txt`; nothing is downloaded and the DLL is untouched.
2. **Replace it with GBE**, which is what the wizard does today.
3. **Keep by default, with a choice to replace.**

**Recommendation: 3.** Keeping the emulator is the request. Replacing it is still the remedy for a
game RUNE does not record, which the test bed has several of, and games in it switched from RUNE or
CODEX to GBE with the wizard have working GSE Saves folders.
Removing the replace path would leave such a game with no fix. The cost is one checkbox on the Ready
page, shown only when the emulator is detected.

### D2. How the wizard tells a CODEX/RUNE game apart

**Recommendation:** the emulator is active when a `steam_emu.ini` sits beside the DLL the wizard
selected **and** that DLL is not GBE. GBE is recognised by the strings `GSE Saves` or
`Goldberg SteamEmu Saves` in the DLL.

The second half is needed because the emulator's files stay behind after a replacement: games in the
test bed that were switched to GBE still have `steam_emu.ini` and their `.rne`/`.cdx` beside a GBE DLL. A positive marker
on the GBE side is used because the loader has none: it carries Valve's version info, and size alone
cannot separate it from Valve's own DLL.

### D3. Where the AppID comes from

**Recommendation:** `[Settings] AppId` of the `steam_emu.ini` beside the selected DLL, ahead of the
generic resolver. The resolver today takes the first `*.ini` or `*.cfg` anywhere under the game folder
that matches an `AppId=` pattern, so a second ini can win, and a game in the test bed has a second
`steam_emu.ini` in a subfolder. The emulator reads only the file beside it, so that file is the one that
names the game.

### D4. How the app finds CODEX/RUNE unlock files

Options:

1. **The existing `gseSavesPaths` list holds them**, and every reader accepts either file name in any
   folder on it.
2. **A separate setting** for CODEX/RUNE folders.
3. **Fixed folders watched whenever they exist**, with no setting.

**Recommendation: 1.** The folders get everything `gseSavesPaths` already has: the Settings card with
its game count, the startup existence check, the folder-change rebuild of the watcher, the
several-paths warning and the diagnostic report's config section. A user can also remove one. A
second setting would duplicate all of that for a list of two fixed paths, and fixed folders would be
invisible in Settings and in a report. The setting's key stays `gseSavesPaths` so existing configs
keep working; only the Settings label changes, from "GSE Saves folders" to "Save folders".

### D5. How the CODEX/RUNE folder gets onto the list

Options:

1. **Add both to the shipped defaults.**
2. **The wizard adds the right one** when it sets up a CODEX/RUNE game, the way `RegisterNewGame`
   already adds a games folder.

**Recommendation: 2.** Defaults reach new installs only: an existing `config.json` already holds its
own `gseSavesPaths`, so the reporter would not get them. Every user without such games would also see
two "folder not found" cards in Settings. The wizard picks `RUNE` when `*.rne` files sit beside the DLL
and `CODEX` when `*.cdx` files do, which matches where each game in the test bed saved, and adds both when
neither is present. If the folder does not exist yet because the game has never been launched, the
wizard creates it, so the watcher can watch it before the first launch. A user whose CODEX/RUNE game
already has a schema adds the folder in Settings; the usage page says so.

### D6. Which INI quirks are read

**Recommendation:**

- An entry is earned when `Achieved=1`, or when `Achieved` is absent and `CurProgress` is at least a
  non-zero `MaxProgress`. That is the Gears quirk; a named writer produces it and the check is one
  expression.
- `UnlockTime=0` gets no special case. It behaves as `earned_time: 0` already does, which the realism
  audit of 2026-09-27 left as it is.
- `[SteamAchievements]` and `[Steam*]` sections are skipped.
- `CurProgress` and `MaxProgress` are not read further. Showing progress is issue #10, and its memo
  can name this file as a second source.

### D7. A game with unlock files in two save folders

Adding CODEX/RUNE folders makes this more common: a game played under RUNE and then switched to GBE
has a file in each. The watcher keeps one record of announced unlocks per game, so if both files hold
the same achievement at different times, it is announced again once per session. That limit is
documented, and a design for it is parked in `.claude/memos/done/2026-09-26-record-unlocks-per-folder-with-a-shared-unlock-so-a-game-in.md`.

**Recommendation:** keep the design parked, and narrow the startup warning to games whose files in two
folders *both* hold an earned achievement. The repeat needs that, and every overlap in the test bed
has an empty RUNE/CODEX file, so the warning as it stands would fire at every start about a problem
none of them can have. Revisit the
parked design if a switched game is reported repeating.

## Out of scope

- The anadius LSX emu.
- `%APPDATA%\Steam\CODEX` as a default or wizard-added folder; a user who has it can add it in
  Settings.
- `stats.ini` and progress.
- Diagnosing why RUNE recorded nothing for some games in the test bed.

## Implementation

### Step 1: the wizard

1. **Detection** (`src/GbeConfig/`). A new pure `SceneEmulatorDetector` takes the selected DLL's
   path and returns null or a result holding the `steam_emu.ini` path, its AppId, and the save
   folders to add (from D5). The GBE check reads the DLL's bytes for both marker strings, ASCII and
   UTF-16. Unit tests cover RUNE active, CODEX active, the emulator's files left beside a GBE DLL, no
   `steam_emu.ini`, and a `steam_emu.ini` with no `AppId`.
2. **AppID.** `AddGameForm.DetectAndAdvanceAsync` runs the detector right after `DllLocator`
   (since replaced by `InstallTarget.Choose`, after which the detector is skipped for a GBE
   `steamclient` the wizard keeps), and an AppId from it skips `AppIdResolver` and counts as
   detected, not guessed. The ini is read with `IniFile.Parse`.
3. **Request.** `ConfigRequest` gains `KeepEmulator`. The Ready page shows, only when the emulator is
   detected:
   - a "Steam emulator" row in the summary naming it (RUNE, CODEX, or CODEX/RUNE);
   - a "Replace it with GBE" checkbox, off by default;
   - the backup checkbox, its help and the GBE folder section, shown only when that box is ticked, and
     then worded for the emulator's library rather than Steam's.
   With the box off, help text says the overlay reads the emulator's own unlock file and only adds the
   achievement list and icons, and the progress page drops its line about downloading GBE.
4. **Generator.** With `KeepEmulator`, `GbeConfigGenerator.RunAsync` skips the DRM check (it asks
   whether the game will load the GBE DLL about to be installed, and none is), `GbeBinaryManager`, the
   backup, `InterfaceGenerator`, the DLL copy and `configs.overlay.ini`, and keeps the schema, hidden
   descriptions, icons, `achievements.json` and `steam_appid.txt`. The progress checklist omits the
   skipped steps rather than marking them skipped, since they never applied. The existing
   `steam_settings` overwrite prompt is unchanged.
5. **Registration.** `RegisterNewGame` passes the detector's save folders to `AddSavesFolders`, which
   creates any that are missing, adds them to `gseSavesPaths` through `AppConfig.UpdateConfigValue`
   (skipping one already listed, via `AppConfig.FindSameFolder`), and rebuilds the watcher the way
   `ApplySettings` does on a folder change, or when it created a folder already listed. The games-folder logic is unchanged: `GameCache` already finds a game by the
   `steam_appid.txt` the wizard writes inside `steam_settings/`, so a CODEX/RUNE game needs nothing
   new there.
6. **Replace mode.** This runs today's path, except for `steam_interfaces.txt`. GBE's
   `generate_interfaces`, run on two RUNE loaders from the test bed, exits 0 and writes an empty file,
   which `InterfaceGenerator` counted as success. On Valve's own DLL it lists every interface the
   `[Interfaces]` section of `steam_emu.ini` does, plus three networking ones. So replace mode writes
   the file from that section, and `InterfaceGenerator` now reports an empty file as no output.

Step 1 does not ship without step 2. The wizard's help text says the overlay reads the emulator's
unlock file, and before step 2 the tracking confirmation counts a game's earned achievements from
`achievements.json` alone, so a CODEX/RUNE game with unlocks already recorded gets a false
"tracking configured" popup.

### Step 2: reading achievements.ini

1. **One reader.** A new `UnlockFile` type owns "the unlock file in an appid folder":
   - it finds `achievements.json` or `achievements.ini`, with the JSON one first if a folder somehow
     holds both;
   - it reads it and returns the parsed states.

   `AchievementWatcher` (seeding, processing, the several-paths warning), `AchievementHistory.GetRecent`,
   `TrayApplicationContext.ReadUnlockStates` and `DiagnosticReport.FindUnlockFile` all call it, so
   none of them repeats a file name.
2. **Parser.** `AchievementMetadata.ParseIniUnlockStates` maps each section to an
   `AchievementUnlockState` per D6, converting entries one at a time so that one bad value costs one
   achievement, as `ParseUnlockStates` does.
   - `IniFile` needs a way to enumerate sections, and its doc comment, written for GBE's overlay
     files, says what the class is now shared by.
   - Its section names are case-insensitive. Steam API names are case-sensitive, but two achievements
     in one game differing only by case is not a case worth code; the unit tests record the choice.
   - Inline text never comes from this format, so `DisplayName` and `Description` stay null and
     resolution goes through the schema as for GBE.
3. **Watcher.** `AchievementWatcher.Start` adds both of `UnlockFile.Names` to the `Filters` of its
   one `FileSystemWatcher` per path, so a path holding both kinds needs no second watcher. Rename events are already subscribed, which covers a temp-file
   writer. `ExtractAppId` is unchanged; the parent folder is the appid in both layouts.
4. **Warning** (D7). `WarnAboutGamesInSeveralPaths` counts a folder only when its unlock file holds
   at least one earned achievement.
5. **Settings and the report.**
   - The Folders card label becomes "Save folders", with a description naming GBE and CODEX/RUNE.
     `DescribeSavesFolder` already counts appid folders, which holds for both layouts.
   - The diagnostic report's unlock section records the file name and, for an INI, its text, which
     carries no credentials.
   - `tools/ReplayReport` dispatches on the file name, so a report from a RUNE user replays.
6. **Tests.**
   - Parser: files shaped like the test bed's, with neutral appids, names and times, including
     `[SteamAchievements]` with `Count=0`; the Gears entry; a non-numeric `UnlockTime`.
   - Watcher, driven through file writes in a temp root:
     - a sorted whole-file rewrite;
     - temp-file-plus-rename;
     - delete-and-create;
     - backlog seeding of a folder that appears after `Start()`.
   - Each test is checked by breaking the subject on purpose: dropping the INI branch must fail the
     watcher tests, not only the parser's.
7. **Docs.**
   - `docs/pages/usage/other-emulators.md` gains a CODEX/RUNE section: what the wizard does, the
     replace option, and adding the folder by hand.
   - `docs/pages/usage/adding-a-game.md` and the settings page get the new checkbox and the renamed
     card.
   - `troubleshooting.md` covers a RUNE game that records nothing: replace it with GBE.
   - `CLAUDE.md` gets a section beside "Self-describing unlock files" describing `UnlockFile` and
     the detector.

## Testing in practice

Every check except the live write runs on the development machine without playing a game; the games,
folders and expected values are in `.claude/memory/codex-rune-local-test-bed.secret.md`.

- **Step 1, keep mode:** run the wizard on an installed game with RUNE active. The AppId must come from
  the `steam_emu.ini` beside the DLL, `steam_settings/` must be written, the DLL's hash must not change,
  the save folder must be added, and the game must be tracked after the rescan.
- **Step 1, replace mode:** run the wizard on a copy of a RUNE game's DLL folder in `tmp/` and compare
  `steam_interfaces.txt` with the `[Interfaces]` list (implementation step 1.6). The installed game is
  not touched.
- **Step 2, seeding:** start the app with the RUNE and CODEX folders listed. Recorded unlocks must seed
  with no popups and appear in the Recent panel with names and icons, and the several-paths warning
  must stay quiet for the empty overlaps.
- **Step 2, unlocks:** copy a RUNE save folder into a scratch save root in `tmp/`, remove one section,
  and write it back in each of the three ways the watcher tests use. Each write must raise one popup
  with the game's text and icon.
- **The live write:** one real unlock under RUNE confirms the write pattern the watcher tests assume.
  That needs either a session in the test bed or the reporter's first unlock on a pre-release build,
  and until one happens the write pattern stays unverified.
