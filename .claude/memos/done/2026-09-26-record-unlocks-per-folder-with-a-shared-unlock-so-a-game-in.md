---
created: 2026-09-26 21:11:39
---

# Record unlocks per folder with a shared Unlock so a game in two GSE Saves paths stops repeating

Parked 2026-09-26: the user chose to document the limit rather than build this — see CLAUDE.md (the paragraph after RegisterNewGame) and troubleshooting.md, "The same achievement appears again".

## The defect

`_seenAchievements` in AchievementWatcher is keyed appid|name, per game, so a game with a folder under two GSE Saves paths has each folder's earned times judged against the other's. Reproduced: P holds A@1000, Q holds A@2000; alternating writes raised A,B,A,C where B,C was expected. A stale folder costs one repeat per session too: Start seeds P first (TryAdd), so Q's next write re-announces A. Keying the record by folder alone ends it but gives a junction or sync copy two popups per unlock, where the per-game record gives one.

## The panel

A 7-agent design workflow produced three designs — an owner folder on the per-game record; per-folder sightings pointing at one shared Unlock; per-folder records suppressed when another folder holds the same time. Two of three judges picked the shared-Unlock design (8.5 and 8, against 7 and 6.5 for owner-folder and 6.5 and 5.5 for suppression); the third preferred suppression. The spec below was prototyped against the Release DLL: the 37 watcher tests passed, 600 random single-folder plans matched today's watcher exactly, 600 lockstep-mirror plans matched a single folder, and every scenario S1-S11 came out right.

## Trade-offs the spec chose

1. S2: a second install that earns an achievement the other folder already holds at a different time gets a popup. The alternative is to treat any unlock another folder holds as already known, which silences S1: someone moving from the old Goldberg path to GSE Saves would never see achievements they had already earned under the old build. When Q writes the time, the two cases look identical. Chosen: notify.

2. Untimed ambiguity: two folders that each gain the same achievement without an earned_time while the app runs give one popup. The alternative gives two popups for every untimed mirror, sync copy or junction. Chosen: one popup, which is also what today's per-game record gives.

3. An exact-time copy counts even when it was only seeded (recorded without being seen to arrive). That is what stops a copy migrated into an existing folder from replaying every old unlock. It costs two narrow misses:
   (a) A settings save adds a path that already holds an identical copy of an unlock a watched path wrote less than 100 ms earlier. The popup is lost; today loses it too for a first earn, but raises it for a re-earn.
   (b) Under a lagging mirror, a copy that the second folder first shows, or first gets a time for, only after its source reset or changed that time still counts. The source returning to exactly that time is then silent: 2 of 600 adversarial plans, against today's 0 misses but 180 plans of repeated popups (108 with this design).
   Chosen: count seeded copies.

4. A code-shape choice, not a scenario. SeedExistingAchievements becomes `internal`, which follows the ProcessFileAsync precedent, so three tests change one argument. The alternative that follows your no-test-only-surface rule more strictly is `private`, with those three tests rewritten to seed through Start, and through Retarget dropping and re-adding the path, keeping their assertions. Chosen: internal.

On its point 4: prefer SeedExistingAchievements private, with the three tests seeding through Start or Retarget, per the no-test-only-surface rule.

## Scenario outcomes

- PROBE: Setup: P has A@1000 and Q has A@2000 at Start, then writes alternate between P and Q, adding B, C, D and E. Result: B, C, D, E, in both gseSavesPaths orders (R3 is per folder; P's A is never compared with 2000). Today gives B,A@2000,C,A@1000,D,... with P first, and A@1000,B,A@2000,C,... with Q first. Right.
- S1: Setup: P is stale with A@1000; Q earns A@2000, then B. Result: A@2000 once, then B once (R3: no folder shows 2000), whether Q's folder exists at Start or appears after it with live times. Today gives the same. Variants the new rules also fix: P untimed and seeded, Q timed gives A@2000 (today: none, because the time was taken as a fill); P and Q both untimed give A and B (today: only B, since a seeded untimed P is not evidence under R4); P@1000 with Q writing no time first, then 2000, gives A@none and B (today: A@2000, A@1000, B). Right.
- S2: Setup: P has A@1000; Q earns A@2000, P earns B, Q earns C, P earns D. Result: A@2000, B, C, D. P's writes never raise A again. Q's A@2000 notifies, because it is a genuine unlock in that install and S1 cannot be told apart from S2 when it is written. Today gives A@2000, A@1000, B, A@2000, C, and so on. Right; the notify decision is listed under tradeoffs.
- S3: Setup: Q earns A@3000 and a mirror copies the file into P. Result: one popup, whichever folder's pass runs first. The second folder shares the first's Unlock (R4, equal times). Untimed copies also give one popup (the unlock arrived while watching, so it is not Seeded). A mirrored re-earn at 4000 adds exactly one more (R2c withdraws the old unlock; the copy shares the new one). Today gives the same. Right.
- S4: Setup: the mirror copies Q's reset of A, then Q's re-earn. Result: one popup for the re-earn, with the same time and with no time written, for either processing order and either path order. Q's reset withdraws the shared Unlock; the copy of the re-earn then shares Q's new one. Lagging mirror, where P gets the reset only after Q has re-earned: one popup; today gives 2. Coalesced mirror, where P never sees the reset: one popup in both path orders; today gives 1. Right.
- S5: Setup (seeded): P lists A as earned 0 and Q holds A earned 1 at 2000; then P earns B and Q earns C. Result: B, C. R1 touches only P's own, empty record. Live variant, where Q earns A@2000 after Start: A, B, C. Today gives B,A,C and A,B,A,C, because P's 0 deleted the per-game key. Right.
- S6: Single folder: unchanged. ProcessFile_EarnedAgainAfterUnearned_WithTheSameTime and _WithNoTimeWritten both pass, and 600 random single-folder plans match today exactly. Two-folder variant: P and Q both hold A@1000 (or both untimed) at Start, and P resets and re-earns A at the same time. Result: one popup in both path orders. The two seeded sightings share an Unlock, and P's reset withdraws it; in the untimed case, Q's seeded untimed sighting is not evidence. Today gives the same. Right.
- S7: All four FolderAppearingAfterStart and UnlockAfterFolderAppeared tests pass, so first-sight backlog seeding still happens per folder. Two-folder case: P has A@1000; Q appears after Start with backlog A@2000 or A@500, plus a live Y; then P adds Z. Result: Y, Z. R5 seeds Q's own record; today gives A,Y,A,Z. Migration copy of P's file into an existing, already-seeded Q: nothing raised, because seeded equal times count as evidence; today gives the same. Right.
- S8: The existing Retarget_* tests pass. Setup: P has A@1000 and an unread B at now; Retarget adds Q with A@2000 and B@1600. Result: B@now, then only C from Q's next write and D from P's next write. Each folder is compared with its own seeded record. Today gives B, A@2000, B@1600, C, A@1000, B, D. Right. Known limit: if the added Q already holds an identical copy of P's unread unlock (Retarget inside the 100 ms debounce), the seeded copy counts as evidence and no popup is shown. For a first earn today misses it too; for a re-earn today raises it.
- S9: Setup: both folders hold A with no time, or P untimed and Q A@2000; each folder is rewritten. Result: B, C, and A never. Each folder compares its own null with itself. A reset and re-earn with no time in one of the two untimed folders gives one popup. Today gives the same. Right.
- S10: Single folder: ProcessFile_TimeWrittenAfterTheUnlockFirstAppearedWithoutOne_RaisesOnce passes, with one popup carrying no time (R2b). Two-folder case: Q has A@700 at Start; P earns A untimed, then @3000; both folders are rewritten. Result: one A, with no time (today: A@3000, A@700, A@3000). Copy races, one popup each: Q's untimed A is announced and the copy that reaches P already carries the time (a live untimed sighting matches); or P's copy of the untimed version is read after Q's time arrived (symmetric). Right.
- S11: Memory holds one Sighting per earned achievement per folder, removed on unearned and replaced on change, plus one Unlock per distinct unlock, referenced only by sightings. It does not grow with writes or over time. Measured in isolation with 60 games x 150 seeded unlocks: one path, today 1.37 MiB vs new 1.98 MiB; the same games in two paths, today 1.32 MiB vs new 3.64 MiB. Time: UnlockShownElsewhere scans only one game's folders (usually 1 or 2), per candidate and per seeded entry. Unchanged gap: records for a path Retarget drops are kept. Right.
- RESET-IN-OTHER-INSTALL: Setup: P has A@1000; Q earns A@2000 and then resets A (GBE form, or Uplay earned 0); then P writes. Result: A@2000, B; P's copy never replays. Both path orders. Today gives A@2000, A@1000, B. The owner-folder design also failed this.
- MS-STALE-FOLDER: Setup: P holds A in milliseconds (1700000000000); Q earns A@2000. Result: A@2000, B; times are never ordered across folders. Today gives an extra A@1700000000000. Ordering by time would have lost the popup.
- FLIP-BACK: Setup: P and Q both hold A@1000; Q changes to 2000, then back to 1000. Result: two popups, like a single folder, because R2c withdraws the shared unlock. Today also gives two.
- UNTIMED-TWO-INSTALLS: Setup: two folders each gain the same untimed achievement while the app runs. Result: one popup, which is indistinguishable from an untimed mirror. Today also gives one. This is a deliberate choice.
- LAGGING-MIRROR-RANDOM: 600 adversarial random plans, where copies lag behind the source and may skip states. Extra popups: 108 plans vs today's 180. An achievement gets fewer popups than the source alone in 2 plans vs today's 0. Both of those are a copy first shown, or first given its time, only after its source reset or changed that time, followed by the source returning to exactly that time. Accepted limit.

## Spec

SCOPE
Only src/AchievementWatcher.cs changes in production, plus tests and docs. Unchanged: TrayApplicationContext, DiagnosticReport, and the public events and their args. Also unchanged: Start, Retarget, Stop, Watch, GetExistingAppIdFolders, EarnedBeforeStart, HasFileChanged, the debounce, _passGates, _observedAppIds, _lastModTimes, _pendingChanges and _seededFolders, including the _seededFolders comment. Nothing is added for tests.

DATA MODEL
- Remove `_seenAchievements`.
- Add `_sightings`, typed `ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, Sighting>>>`. It has three levels, and each level uses its own comparer:
  - Outer: appid, with the default ordinal comparer, like `_passGates`.
  - Middle: the game folder's PathKey, with `StringComparer.OrdinalIgnoreCase`, like `_seededFolders`.
  - Inner: the achievement name, ordinal, as today's key was.
- Add `private readonly record struct Sighting(long? EarnedTime, Unlock Unlock)`: one folder's record of one achievement it marks earned.
- Add `private sealed class Unlock`: one unlock, shared by every folder showing it.
  - `public required bool Seeded { get; init; }`
  - `public bool Withdrawn`, backed by `private volatile bool _withdrawn`. It is written by passes and read by seeds, which take no gate.
  - Use no primary constructor: .editorconfig sets csharp_style_prefer_primary_constructors = false.
- `public void SeedExistingAchievements(string appId, ...)` becomes `internal void SeedExistingAchievements(string gameFolder, Dictionary<string, AchievementUnlockState> states)`. Inside it, `folder = PathKey(gameFolder)` and `appId = Path.GetFileName(folder)`. Its only callers outside the class are tests, which InternalsVisibleTo already covers.

RULES
F is the PathKey of the directory holding achievements.json. The record is F's own. Each rule is marked with the scenarios that depend on it.
- R1, unearned entry: `record.TryRemove(name, out var s)`, and if an entry was removed, set `s.Unlock.Withdrawn = true`. Other folders' records are never touched. An achievement absent from the file changes nothing. (S5, S4, S6)
- R2, earned entry with a previous sighting in F:
  - a. t is null, or t equals the previous time: do nothing.
  - b. The previous time is null and t is written (the time arriving): write `(t, joined ?? prev.Unlock)`, where `joined = prev.Unlock.Seeded ? UnlockShownElsewhere(t) : null`. Never raise. It is seeded-only because R4 never matches a seeded unlock with no time, so no other folder can share it and re-pointing it leaves nothing behind. A live one was matched when it arrived and may be shared. (S10, and the lagging-mirror join)
  - c. Both times written and different (earned again): set `prev.Unlock.Withdrawn = true`, then `shown = UnlockShownElsewhere(t)`, write `(t, shown ?? new Unlock { Seeded = false })`, and raise only if shown is null. (S6, the flip-back case, a mirrored re-earn)
- R3, earned entry with no sighting in F: `shown = UnlockShownElsewhere(t)`, then `record.TryAdd(name, (t, shown ?? new Unlock { Seeded = false }))`. If TryAdd fails, a seed of F raced the pass; raise nothing. Otherwise raise only if shown is null.
- R4, `UnlockShownElsewhere(appId, F, name, t)`: returns the Unlock of the first other folder G of the same appid (G != F, OrdinalIgnoreCase) whose sighting of name is not Withdrawn and meets one of these:
  - both times are written and equal;
  - at least one time is unwritten, and the unlock is not Seeded.
  Otherwise it returns null. Written times are never ordered or compared across folders except for equality, so a folder writing milliseconds cannot hide or cause anything.
- R5, seeding F: covers SeedExistingAchievementsFromDirectory (Start, and a path Retarget adds) and the first-sight EarnedBeforeStart backlog. For each earned entry: `record.TryAdd(name, (t, UnlockShownElsewhere(t) ?? new Unlock { Seeded = true }))`. A seed never overwrites and never raises.
- R6, gates: unchanged. Passes run one at a time per appid, so R4 inside a pass reads settled records. Seeds stay ungated; the worst a race can do is decide whether one seeded entry is linked to another folder's unlock.
- INVARIANT: with one folder, R4 always returns null, so R1 to R3 are exactly today's rules. The 600-plan differential confirmed this.

CODE (as prototyped)

Field, replacing `_seenAchievements` and its comment:
```
    // What each game folder's unlock file last said about each achievement it marks earned: by appid,
    // then by the folder's PathKey, then by achievement name. A folder's file is diffed only against the
    // folder's own record, because one game can have a folder under two GSE Saves paths (an older
    // emulator build writes to another one), and judging one folder's time against the other's announces
    // the same unlock again on every write to either. Grouping by appid is what lets a folder ask whether
    // another folder of the game already shows an unlock; see UnlockShownElsewhere.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, Sighting>>> _sightings = new();
```

ProcessFileExclusiveAsync. Keep the first-sight comment. Replace from the `_seededFolders.TryAdd` line to the start of the "New achievement unlocked" log line:
```
        var folder = PathKey(Path.GetDirectoryName(filePath)!);
        if (_seededFolders.TryAdd(folder, 0))
        { ...unchanged, except: SeedExistingAchievements(folder, backlog); }

        // Diff against this folder's own record to find new unlocks
        var record = RecordOf(appId, folder);
        foreach (var (achName, state) in states)
        {
            // An entry the file marks unearned is forgotten, so earning it again after a reset notifies
            // like any new unlock. The unlock it showed is withdrawn, so a copy of it still sitting in
            // another folder cannot pass the next unlock off as that copy. Only this folder's record is
            // touched: another folder listing the achievement unearned is an install that never earned
            // it, not a reset of this one. An achievement absent from the file is not a reset.
            if (!state.Earned)
            {
                if (record.TryRemove(achName, out var forgotten))
                    forgotten.Unlock.Withdrawn = true;
                continue;
            }

            var earnedTime = state.EarnedTime;

            // The first sight of an unlock raises; after that only a changed written time can. A
            // different written time is the achievement earned again, and withdraws the unlock it
            // replaces as a reset does. A time arriving for an unlock first seen without one is the same
            // unlock — already announced, or seeded as earned before the watcher started — so the time is
            // recorded and nothing is raised. A time the file stops writing says nothing new about the
            // unlock either, so the time already known is kept. A first sight or a changed time can still
            // be an unlock another folder of the game already shows, and is then that unlock's copy:
            // recorded as sharing it, and not raised again.
            Unlock? shown;
            if (record.TryGetValue(achName, out var previous))
            {
                if (earnedTime == null || previous.EarnedTime == earnedTime)
                    continue;
                if (previous.EarnedTime == null)
                {
                    // A seed without a time could not be told apart from another install's, so nothing
                    // shares it; with the time, it can join the unlock another folder shows at that time.
                    // One that arrived while watched was matched when it arrived, and may be shared.
                    var joined = previous.Unlock.Seeded ? UnlockShownElsewhere(appId, folder, achName, earnedTime) : null;
                    record[achName] = new Sighting(earnedTime, joined ?? previous.Unlock);
                    continue;
                }
                previous.Unlock.Withdrawn = true;
                shown = UnlockShownElsewhere(appId, folder, achName, earnedTime);
                record[achName] = new Sighting(earnedTime, shown ?? new Unlock { Seeded = false });
            }
            else
            {
                shown = UnlockShownElsewhere(appId, folder, achName, earnedTime);
                // A seed of this same folder can record it between the two calls — Retarget adding back a
                // path it had dropped — and the first to record an unlock decides whether it is new.
                if (!record.TryAdd(achName, new Sighting(earnedTime, shown ?? new Unlock { Seeded = false })))
                    continue;
            }

            if (shown != null)
            {
                Logger.Info($"Unlock already shown from another folder of the game: appid={appId}, name={achName}, time={earnedTime?.ToString(CultureInfo.InvariantCulture) ?? "not written"}");
                continue;
            }
            // unchanged from here: the "New achievement unlocked" log line and NewAchievement?.Invoke
```

The log line uses the appid= spelling and contains no path, so DiagnosticReport.KeepLinesForGame keeps it in that game's report.

SeedExistingAchievementsFromDirectory: `SeedExistingAchievements(appId, states)` becomes `SeedExistingAchievements(dir, states)`. Nothing else changes there.

Replace SeedExistingAchievements, and add the new members at the end of the class:
```
    /// <summary>
    /// Records a game folder's already-earned achievements in that folder's record, so they don't fire
    /// as new. Records only achievements the folder's record does not hold yet, so a seed can never
    /// overwrite (and thereby swallow) an unlock a concurrent pass over the same file has just recorded.
    /// </summary>
    internal void SeedExistingAchievements(string gameFolder, Dictionary<string, AchievementUnlockState> states)
    {
        var folder = PathKey(gameFolder);
        var appId = Path.GetFileName(folder);
        var record = RecordOf(appId, folder);
        foreach (var (achName, state) in states)
        {
            if (state.Earned)
                record.TryAdd(achName, new Sighting(state.EarnedTime, UnlockShownElsewhere(appId, folder, achName, state.EarnedTime) ?? new Unlock { Seeded = true }));
        }
    }

    /// <summary>One game folder's record, created empty on first use.</summary>
    private ConcurrentDictionary<string, Sighting> RecordOf(string appId, string folder) => _sightings.GetOrAdd(appId, _ => new(StringComparer.OrdinalIgnoreCase)).GetOrAdd(folder, _ => new());

    /// <summary>
    /// The unlock another folder of the game already shows for this achievement, or null. Two written
    /// times are the same unlock when they are equal: a mirror, a junction, a migrated copy. Where either
    /// side wrote no time, only an unlock seen to arrive while the watcher ran counts, since its copy can
    /// reach the other folder with or without the time, while one recorded before that could as well be
    /// another install's. A withdrawn unlock counts for nothing: a folder that showed it has moved on.
    /// Passes over one game run one at a time, so a pass reads the other folders' records settled; a seed
    /// takes no gate, and a race can only decide whether that one seeded entry shares another's unlock.
    /// </summary>
    private Unlock? UnlockShownElsewhere(string appId, string folder, string achName, long? earnedTime)
    {
        if (!_sightings.TryGetValue(appId, out var folders))
            return null;

        foreach (var (otherFolder, record) in folders)
        {
            if (string.Equals(otherFolder, folder, StringComparison.OrdinalIgnoreCase) || !record.TryGetValue(achName, out var sighting) || sighting.Unlock.Withdrawn)
                continue;
            if (sighting.EarnedTime is { } shownTime && earnedTime is { } seenTime ? shownTime == seenTime : !sighting.Unlock.Seeded)
                return sighting.Unlock;
        }
        return null;
    }

    /// <summary>One folder's word on one achievement it marks earned.</summary>
    /// <param name="EarnedTime">The earned_time the file wrote; null when it wrote none.</param>
    /// <param name="Unlock">The unlock it shows, shared by every folder of the game showing the same one.</param>
    private readonly record struct Sighting(long? EarnedTime, Unlock Unlock);

    /// <summary>
    /// One unlock of one achievement of one game, shared by every folder that shows it, so a copy of it
    /// in a second folder does not announce it a second time.
    /// </summary>
    private sealed class Unlock
    {
        // Set by a pass and read by a seed on another thread, which takes no gate.
        private volatile bool _withdrawn;

        /// <summary>First recorded by a seed, rather than seen to arrive while the watcher ran.</summary>
        public required bool Seeded { get; init; }

        /// <summary>
        /// A folder that showed it has stopped showing it — marked the achievement unearned, or wrote a
        /// different time for it — so the copies left in other folders are out of date, not evidence.
        /// </summary>
        public bool Withdrawn { get => _withdrawn; set => _withdrawn = value; }
    }
```

COMMENTS TO UPDATE
- The `_seenAchievements` comment is replaced by the `_sightings` comment above.
- "Diff against cached state" becomes "Diff against this folder's own record".
- The unearned comment and the first-sight/changed-time comment are rewritten as above.
- The TryAdd race comment now names a seed of the same folder, since a different GSE Saves folder no longer shares the record.
- The SeedExistingAchievements summary is rewritten.
- The Seeded-log wording "for newly seen appid" can stay.

KNOWN LIMITS (see tradeoffs; do not pin them in tests)
- A path added by Retarget while it holds an identical copy of an unlock a kept path wrote within the debounce is lost. For a first earn, today loses it too; for a re-earn, today raises it.
- Two folders both gaining the same untimed achievement while the app runs give one popup, as today.
- Under an adversarial lagging mirror, a copy that is first shown, or first gets its time, only after its source reset or changed that time still counts. The source returning to exactly that time is then silent. This happened in 2 of 600 random plans, where today misses none but repeats popups in 180 plans against 108 with this design.

UNCHANGED GAP
Records for a path that Retarget drops are kept, as today. Pruning `_sightings[appId][folder]` for folders under dropped paths is possible now, but it is a separate change.

## Tests to add

- Existing tests, argument change only: SeedExistingAchievements_PreventsNotification, ProcessFile_NewUnlockAfterSeeding_RaisesEvent and SeedExistingAchievements_DoesNotOverwriteObservedUnlock pass Path.Combine(_tempDir, '12345') instead of '12345'; no assertion changes. The third one fails if the argument is left as '12345': the seed then lands in a different folder, cwd/12345, whose equal time withholds the re-earn. All 37 existing tests passed on the prototype.
- Fixture additions:
- `_otherDir = _tempDir + '_other'`, a second GSE Saves root created in the constructor and deleted in Dispose.
- A quiet-watcher helper: `new AchievementWatcher(paths, debounceDelay: TimeSpan.FromMinutes(1))` subscribed to _events, so the FileSystemWatcher never runs a pass. Lines 447 and 506 already build this inline and can share it.
- A writer helper for `<root>/4242/achievements.json` that calls WriteFile and then sets LastWriteTimeUtc from a strictly increasing fixture clock, so HasFileChanged never skips an identical-length mirror copy.
- Assertions compare _events projected to 'Name@time'. Each test calls ProcessFileAsync directly after each write. For a folder that exists at Start, literal times such as 1000 are live; for a folder appearing after Start, use now+5.
- ProcessFile_OneGameInTwoFolders_NeverRaisesTheOtherFoldersUnlock, a Theory over the order of gseSavesPaths. Setup: P {A@1000} and Q {A@2000} before Start; then write P {A,B@2100}, Q {A,C@2200}, P {A,B,D@2300}, Q {A,C,E@2400}. Assert: exactly B, C, D, E. Fails today.
- ProcessFile_NewTimeInASecondFolder_RaisesOnce (S1). Setup: P {A@1000} and an empty Q/4242 at Start; then Q {A@2000}, then Q {A@2000,B@2100}. Assert: A@2000, B@2100. A second case has Q's folder appear after Start with now-based times, with the same assertion. Passes today; it guards against suppressing any unlock another folder holds.
- ProcessFile_UnlockInEachInstall_RaisesEachOnce (S2). Setup: P {A@1000} and an empty Q at Start; then Q {A@2000}, P {A@1000,B@2100}, Q {A@2000,C@2200}, P {+D@2300}. Assert: A@2000, B@2100, C@2200, D@2300. Fails today.
- ProcessFile_UnlockMirroredIntoASecondFolder_RaisesOnce (S3), a Theory over which folder is processed first and over timed or untimed. Setup: empty P and Q at Start; identical files written to both, then both processed. Then, in the timed case, both files are rewritten with A@4000. Assert: A@3000 then A@4000, or a single A@none. Passes today; it pins the per-game dedupe that per-folder records would otherwise lose.
- ProcessFile_MirroredResetAndReEarn_RaisesOnce (S4), a Theory over same time or no time, processing order, and path order. Setup: P and Q {A@1000} (or untimed) at Start; both files reset to earned false, both processed; both re-earned, both processed. Assert: one popup. Passes today.
- ProcessFile_LaggingMirrorResetAndReEarn_RaisesOnce, a Theory over same time or no time. Setup: P and Q {A@1000} at Start; Q reset, Q re-earned, then P reset, then P's re-earn copy, each processed in turn. Assert: one popup. Fails today with 2.
- ProcessFile_CoalescedMirrorResetAndReEarn_RaisesOnce, a Theory over path order and same time or no time. Setup: P and Q {A@1000} at Start; Q reset, Q re-earned at the same time; P only receives the final identical file. Assert: one popup. Passes today; without the withdrawal in R1 it gives 0.
- ProcessFile_UnearnedInOneFolder_DoesNotForgetAnotherFoldersUnlock (S5, Uplay numeric form). Setup: P {A: earned 0} and Q {A: earned 1, earned_time 2000} at Start; then P {A:0, B:1@2100}, then Q {A:1@2000, C:1@2200}. Assert: B, C. A live variant starts with Q empty and has Q earn A@2000 first; assert A, B, C. Both fail today.
- ProcessFile_ResetInOneInstall_DoesNotReplayTheOther, a Theory over the GBE and Uplay reset forms and over path order. Setup: P {A@1000} and an empty Q at Start; Q {A@2000}; Q resets A; then P {A@1000,B@2100}. Assert: A@2000, B@2100. Fails today, which gives an extra A@1000; the owner-folder design also fails it.
- ProcessFile_ResetAndReEarnInOneOfTwoFoldersHoldingTheSameTime_Raises (S6, two folders), a Theory over path order and timed or untimed. Setup: P and Q {A@1000} (or untimed) at Start; P resets, then re-earns at the same time. Assert: one popup. Passes today; it pins that seeds sharing an equal time share an Unlock, without which it gives 0.
- ProcessFile_FolderAppearingAfterStart_ForAGameHeldElsewhere_SeedsItsOwnBacklog (S7), a Theory over backlog times 2000 and 500. Setup: P {A@1000} at Start; Q/4242 appears with {A@backlog, Y@now+5}; then P {A@1000, Z@now+6}. Assert: Y, Z. Fails today.
- ProcessFile_OldUnlocksCopiedIntoAnExistingFolder_AreNotReplayed. Setup: P {A@1000,B@1001} and Q {} at Start; Q receives P's content. Assert: no events. Passes today; it pins that seeded copies count as evidence.
- Retarget_AddedPathHoldingTheSameGameWithOtherTimes_RaisesOnlyNewUnlocks (S8). Setup: P {A@1000} and Start([P]); P {A@1000, B@now+5} left unread; Q/4242 {A@2000, B@1600}; Retarget([P,Q]); process P; then Q {+C}; then P {+D}. Assert: B, C, D. Fails today.
- ProcessFile_TimeArrivingInOneFolder_DoesNotRaiseTheOther (S10, two folders). Setup: Q {A@700} and an empty P at Start; P {A: earned true}; P {A@3000}; rewrite Q; rewrite P. Assert: one event with a null EarnedTime. Fails today, which gives A@3000, A@700, A@3000.
- ProcessFile_TimeFilledInOnOneSideOfACopy_RaisesOnce, a Theory over two cases. (a) Q {A untimed} processed; Q and P then both get {A@3000}, and P is processed first. (b) Q {A untimed} processed; P gets the untimed copy; Q {A@3000} processed; then P processed, then P {A@3000}. Assert: one event with a null time. Passes today; it pins both halves of the unwritten-time rule in R4.
- ProcessFile_UntimedUnlockHeldByAStaleFolder_StillRaises, a Theory over two cases. (a) P {A untimed} at Start and Q gets {A@2000}; assert A@2000. (b) P {A untimed} and Q gets {A untimed}, then {A, B untimed}; assert A and B. A third case, P {A@1000} while Q writes {A untimed} then {A@2000}, asserts a single A with no time. All fail today; they pin that a Seeded unlock is not evidence when a time is unwritten.
- ProcessFile_SeededUntimedCopiesJoinWhenTheirTimeArrives. Setup: P and Q {A untimed} at Start; Q {A@1000}; P {A@1000}; Q resets; Q {A@1000}; P {A@1000}. Assert: exactly one A@1000. Passes today; without the join in R2b it gives 0.
- ProcessFile_MillisecondTimesInAStaleFolder_DoNotHideANewUnlock. Setup: P {A@1700000000000} and an empty Q at Start; Q {A@2000}; P {A@1700000000000, B@2100}. Assert: A@2000, B@2100. Fails today, which gives an extra A; ordering times across folders would lose the popup instead.
- ProcessFile_TimeRestoredInOneOfTwoFolders_RaisesLikeASingleFolder. Setup: P and Q {A@1000} at Start; Q {A@2000}; Q {A@1000}. Assert: A@2000, A@1000. Passes today; without the withdrawal in R2c it gives only A@2000.
- ProcessFile_UntimedUnlockInBothFolders_NeverRaises (S9), a Theory over Q untimed or Q@2000. Setup: P {A untimed}, Q {A untimed or @2000} at Start; P {A, B@2100}; Q {A, C@2200}; P rewritten. Assert: B, C. Passes today.
- Do not add tests asserting the known limits (the S8 mirror swallow, one popup for two untimed installs, the lagging-copy return): a test that pins a miss locks it in. The prototype confirmed each outcome described.

## Doc changes

CLAUDE.md: add a section after "Tracking-configured notification" (or after "Self-describing unlock files"):

## One game under two GSE Saves paths

A game can have a folder under two of the `gseSavesPaths` — the troubleshooting page has users of older emulator builds add `%appdata%\Goldberg SteamEmu Saves` beside `GSE Saves`, and a sync tool or a directory junction does the same. So `AchievementWatcher` records unlocks per game folder, not per game (`_sightings`: appid, then folder, then achievement), and diffs each folder's file only against that folder's own record. Every rule about one file — first sight raises, a changed written time is earned again, a time filled in or dropped is not, unearned forgets — is therefore exactly what a single-folder game gets. A per-game record would compare one folder's time with another's and announce the same unlock again on every write to either.

The other folders of a game can only withhold a popup, never cause one. A folder that newly shows an unlock another folder already shows shares that folder's `Unlock` and raises nothing: equal written times are one unlock, and where either side wrote no time, only an unlock that arrived while the watcher ran counts, since one seeded without a time could as well be another install's. That keeps a mirror, a junction or a migrated copy to one popup. Written times are compared across folders only for equality, never ordered, because a writer using milliseconds would otherwise win every comparison. A folder that marks the achievement unearned, or writes a different time for it, withdraws the unlock, so the copies it leaves elsewhere stop counting. An unearned entry touches only its own folder's record: the Uplay emulator lists every achievement it has not earned, and an install that never earned one is not a reset of another.

The accepted limits all involve a copy with an identical time or none. Two installs that each gain the same untimed achievement in one session give one popup, as a per-game record did. A path that Retarget adds while it holds a copy of an unlock that a kept path has written but not yet read swallows that popup. A copy that another folder first shows, or first gets a time for, only after its source reset or changed that time still counts, so the source returning to exactly that time is silent.

Also in CLAUDE.md, in the "Consequences elsewhere" paragraph of "Self-describing unlock files": nothing needs to change. It still holds that seeding is unconditional and per folder. CLAUDE.md currently carries another session's uncommitted edit (in the language-fallback paragraph), so stage only this section.

docs/pages/troubleshooting.md, in the Goldberg SteamEmu Saves bullet: change "Add that folder to `gseSavesPaths`." to "Add that folder to `gseSavesPaths`; a game with a folder in both still gets one popup per unlock."

docs/pages/development/gbe-reference.md, "Where unlocks are stored": change "either point `gseSavesPaths` at that folder too or move the game onto a current GBE build." to "either point `gseSavesPaths` at that folder too — a game with a folder in both still gets one popup per unlock — or move the game onto a current GBE build."

Run the docs-style skill before finalising the CLAUDE.md section.
