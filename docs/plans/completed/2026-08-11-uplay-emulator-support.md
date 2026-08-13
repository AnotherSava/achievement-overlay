# Uplay emulator support (self-describing unlock files)

Tracks [issue #5](https://github.com/AnotherSava/achievement-overlay/issues/5) — "[Feature request] Uplay Steam mode support", filed 2026-08-06 by `ant-sh` (the same reporter as #3).

## Goal

Track achievements for a game whose `%appdata%\GSE Saves\<id>\achievements.json` is written by the
Goldberg Uplay R2 emulator rather than GBE, without requiring any `steam_settings/` folder,
`steam_appid.txt`, or entry in `gamesPaths`.

## What the reporter is running

The Goldberg uplay r2 emulator (Mr_Goldberg's v0.0.2, continued as binary-only builds by cs.rin.ru
user `demde`). Two INI keys in `uplay_r2.ini` / `upc_r2.ini` make it write into the GSE Saves layout:

- **`AchSavePath`** — an absolute output directory. `%APPDATA%` is not expanded by the emulator, so
  the path is typed out literally. The GSE Saves layout is therefore a *convention the user or setup
  script chose*, not something the emulator computes — and by that convention the folder is named
  with the **Steam** AppID so Steam-oriented trackers find it.
- **`AchKeyPrefix`** — the game passes only a numeric achievement id, so the emulator emits
  `<AchKeyPrefix>Ach_<id>` (e.g. `AFOP_Ach_7`), which happens to be the real Steam API name.

Detail lives in the `gbe-emulator` learning. **None of it is on this feature's critical path** — see
"Design rationale" below. It is recorded so a future reader knows what was and wasn't verified.

## The format, per the issue

```json
"AFOP_Ach_7": {
    "earned": 0,
    "description": "Complete the quest Becoming.",
    "displayName": "First Strike"
}
```

On unlock, `earned` becomes `1` and `earned_time` is added. Before any unlock the file is a
byte-identical copy of the `achievements_schema.json` sitting next to the emulator DLL.

Differences from GBE that matter:

| | GBE | Uplay R2 |
|---|---|---|
| `earned` | JSON bool | **JSON number `0`/`1`** |
| `earned_time` | always present | added on unlock only |
| per-entry text | none | **carries `displayName` + `description` inline** |
| icons | `steam_settings/achievement_images/` | none anywhere |
| discovery anchor | `steam_appid.txt` | none — the id appears only inside `AchSavePath` |

## Design rationale — why there is no game discovery

The unlock file the app already watches carries its own display text, so it is **self-describing**:
everything needed for a notification is in the file, and nothing about the Uplay game folder is
required. That matters because the game-folder facts (which ini, which key, where the id lives) are
forum-derived and unverified, while the file's shape is what the reporter pasted into the issue
himself. Building on the file alone means the feature does not rest on anything we cannot check.

The rejected alternative — teaching `GameCache` to discover Uplay games by scanning `gamesPaths` for
`achievements_schema.json` plus the emulator ini — buys exactly one thing the inline path can't
provide: a real game name. It costs a whole discovery subsystem staked on an unverified ini key, and
still yields no icons. Deferred; see "Follow-ups".

## Verified facts this plan relies on

Established by running code on .NET 10 against the repo's own types:

- `ParseUnlockStates` on `"earned": 0` throws `JsonException: The JSON value could not be converted
  to System.Boolean` and loses the **entire** dictionary, not just that entry.
- `JsonNumberHandling.AllowReadingFromString` does **not** fix it (it governs number↔string only).
  A converter is the only option.
- A property-scoped `[JsonConverter]` on `Earned` fixes it without touching any other `bool` in the
  app, and leaves the GBE payload parsing identically.
- Extra `description`/`displayName` keys on the unlock object are already skipped harmlessly
  (`UnmappedMemberHandling.Skip` is the default).
- `File.ReadAllText[Async]` already strips UTF-8/UTF-16 BOMs. **Do not** pin an encoding or switch to
  a bytes overload "to be safe" — that introduces a bug that does not exist today.

Details in the `dotnet-system-text-json-tolerant-parsing` learning.

## Assumptions that only the reporter's test can settle

Ranked by what breaks if wrong. The build handed to him is the acceptance test; a hand-made fixture
can only confirm our own beliefs about the format.

| | Assumption | If wrong |
|---|---|---|
| **U1** | An **unlocked** entry still carries `displayName`/`description` | **The design collapses** — inline resolution returns null for exactly the entries that produce popups. The issue only shows inline text on a *locked* entry; the unlocked example is a two-line fragment. |
| U2 | `earned_time` is a bare number, not quoted | Hedged: tolerant numeric parsing (step 3). |
| U3 | The emulator writes in place, not temp-file-plus-rename | Hedged: subscribe `Renamed` (step 5). Unhedged this is a total silent failure — no popup, no log line. |
| U4 | It closes the handle, so last-write-time updates | Hedged: key the change check on (time, length) (step 5). |
| U5 | `earned_time` values are stable across rewrites | Not hedgeable cheaply. If they churn, every rewrite replays every unlock. Ask. |
| U6 | The top level holds only achievement entries | Hedged: per-entry tolerance (step 3). |

## Implementation

### 1. Tolerant `earned` — `src/AchievementMetadata.cs`

Add `internal sealed class FlexibleBooleanConverter : JsonConverter<bool>`: `True`/`False` pass
through; `Number` → `TryGetInt64 ? n != 0 : GetDouble() != 0`; `String` → `bool.TryParse` then
`long.TryParse`; `Null` → **`false`** (a null must cost at most one popup, never the file); anything
else throws. `Write` emits a real boolean so GBE-shaped output still round-trips.

Apply property-scoped — `[JsonConverter(typeof(FlexibleBooleanConverter))]` on
`AchievementUnlockState.Earned` — never on the shared `JsonOptions`. Keep `Earned` typed `bool` so
all four consumers and the existing tests are untouched.

### 2. Inline text on the unlock state — `src/AchievementMetadata.cs`

Add `DisplayName` and `Description` as `JsonElement?` to `AchievementUnlockState`, mirroring
`AchievementDefinition`, so the existing `GetDisplayText` handles both plain strings and
per-language objects with no new code. Add a tolerant converter for `EarnedTime` (number or string,
missing → 0) — U2 insurance, ~10 lines.

### 3. Per-entry tolerance in `ParseUnlockStates`

Deserialize to `Dictionary<string, JsonElement>`, then convert each value inside its own
`try`/`catch (JsonException)`. Today one surprise value anywhere silently costs every notification in
the file, and three of the four call sites swallow the exception.

**Aggregate the failures into one log line** — `Skipped {n}/{total} unreadable entries in '{path}'
(first: {message})`. A per-entry warning turns one systematically-bad field into 120 synchronous
disk flushes per save (`Logger` uses `AutoFlush = true`).

A document-level syntax error must still throw `JsonException`, so `AchievementWatcher`'s existing
catch and `ProcessFile_InvalidJson_LogsErrorAndSkips` keep working.

### 4. Resolution chain — `src/AchievementMetadata.cs`, `src/NotificationQueue.cs`

> Superseded by the schema-first follow-up at the end of this document; the precedence below is what
> shipped in v1.6.0-rc1.

Split `Resolve` into `ResolveFromDefinitions` (today's body from `FindDefinition` down) and
`ResolveInline(state, name, language)` (null when the state is null or both texts are empty;
`IconPath` always null — never probe GSE Saves for images).

Order: **cached schema → inline → rescan**. Two corrections from review:

- Skip the rescan leg entirely when `LookupCached(appId) != null` — otherwise a known game with a
  stale schema parses its definitions file twice per dropped notification and double-logs the warning.
- When the file satisfies `IsSelfDescribing`, resolve **inline first** and log a warning. A
  self-describing file is by construction not GBE's, so a cached GBE schema under that id belongs to
  a different game — this is the appid-collision guard (Ubisoft and Steam id ranges fully overlap).

Add pure, unit-testable `HasInlineText(state)` and `IsSelfDescribing(states)`.

`NotificationQueue` passes the unlock state through (new non-required `UnlockState` on
`NewAchievementEventArgs`, so existing test initializers still compile) and keeps dropping on null,
with the skip log naming both causes.

### 5. Watcher — `src/AchievementWatcher.cs`

- **Subscribe `Renamed`** alongside `Changed`/`Created`. One line; `RenamedEventArgs` derives from
  `FileSystemEventArgs`. Guards U3, zero risk to the GBE path.
- **Key `HasFileChanged` on (last-write-time, length)** and raise its skip line to `Warn`. Guards U4.
- **Remove the seeding filter** — `Start()` loses `knownAppIds`, `ReseedKnownAppIds` becomes
  `ReseedAll`. Today unknown appids are never seeded, so the moment inline resolution works, every
  already-earned achievement fires at once. Behaviour-preserving for GBE (unknown appids were
  seeded-but-dropped either way).
- **Seed, don't notify, on first sight of an unseeded appid**: removing the filter does not cover a
  folder that *appears* mid-session (the documented Uplay migration moves save folders in by hand).
  On the first `ProcessFileAsync` for an appid absent from the `Start()` seed set, seed any entry
  whose `earned_time` predates the watcher's start rather than raising it.

### 6. Setup confirmation for Uplay games — `src/AchievementWatcher.cs`, `src/TrayApplicationContext.cs`

Rename `GameFolderCreated` → `GameFolderObserved` and also raise it from `ProcessFileAsync`, because
the emulator creates the folder before the file exists and the folder-creation gate fails.
Three constraints from review:

- Raise it **after** the read+parse succeeds, not at the top — `ReadFileWithRetryAsync` exists
  precisely because the writer still holds the handle at that moment.
- **Carry the parsed states in the event args** so the handler never re-reads a file that is being
  written.
- The once-per-appid-per-session guard must be a `ConcurrentDictionary`, not a `HashSet` —
  `ProcessFileAsync` is reached from fire-and-forget tasks where an exception is unobserved.

Gate on `game != null || IsSelfDescribing(states)`, so a stray GBE-shaped folder for an unconfigured
game still does not fire.

### 7. Recent panel — `src/AchievementHistory.cs`

Use `LookupCached` (never `Lookup` — a rescan per unknown appid inside `GetRecent` is a regression),
load definitions **once per game** rather than once per achievement, then per entry
`ResolveFromDefinitions(...) ?? ResolveInline(...)`, skipping when both the game and the resolution
are null. Gate the Gearhead row on the same predicate, so it doesn't outlive an uninstalled game as a
bare-appid row.

Game name: `gameInfo?.GameName ?? appId`. **No `gameNames` config key in v1** — the reporter never
asked for one, and one answer from him either kills it or specifies it properly.

### 8. Startup — `src/TrayApplicationContext.cs`, `src/AppConfig.cs`

A Uplay-only user has no Steam game roots, and today cannot start the app at all: `Validate` hard-
fails on empty `gamesPaths`, and the constructor exits when the cache is empty.

- Make `SettingsData.GamesPaths` nullable and validate **absent** rather than empty, so a typo'd key
  still produces the loud "missing" dialog while an explicit `"gamesPaths": ""` is accepted.
- Replace the empty-cache fatal exit with a warning when `gseSavesPaths` is valid. Exiting is wrong
  precisely for the user who has installed the overlay but not yet run the game — the one case the
  directory watcher exists to handle.

### 9. Add game wizard — `src/AddGameForm.cs`

String only: say the wizard configures Steam games and that other emulators are tracked
automatically once they write to GSE Saves. It stays Steam-only — the generator replaces the game's
DLL.

## Tests

New/extended in `tests/AchievementMetadataTests.cs` (the issue's Avatar payload verbatim; mixed
bool+number in one file; string forms; null → false; absent `earned_time`; a GBE regression case; one
malformed entry skipped while siblings parse; `HasInlineText`/`IsSelfDescribing`; `ResolveInline`
with text / without / description-only / multi-language), `NotificationQueueTests` (inline resolves;
schema still wins over inline, icon intact), `AchievementHistoryTests` (Uplay-shaped unknown appid is
included; appid fallback name), `AchievementWatcherTests` (both `earned` forms; event carries the
state; no replay storm for an appid that appears mid-session), `AppConfigTests` (empty vs absent
`gamesPaths`).

Keep unchanged — verified they stay green rather than needing rewrites:
`NotificationQueueTests.ResolveMetadata_UnknownGame_ReturnsNull`, `Enqueue_UnknownGame_SkipsNotification`
(their args carry no unlock state), and `AchievementHistoryTests.GetRecent_OnlyTrackedGames_IgnoresUnknownAppIds`
(its fixture is GBE-shaped with no inline text).

## Validation

A hand-made fixture in the exact shape from the issue proves the parser matches *our belief* about
the format — it cannot detect U1, U3, U4 or U5 being wrong. So: build the branch, hand the reporter a
build, and ask him to confirm popups carry the right name and description. That answer is the real
acceptance test.

## Follow-up: schema-first precedence (2026-08-13)

The reporter confirmed the popups work, then asked for the one thing v1 ruled out: **icons**. His
argument settles the design question v1 hedged on. He configures the emulator's `AchKeyPrefix` so it
emits the game's *real Steam achievement names*, and names the save folder with the *Steam* AppID —
deliberately, so Steam-oriented trackers line up. So a Uplay game with a GBE `steam_settings/` beside
it is not a special case at all: it is a Steam game whose unlock file happens to be written by an
odd emulator. He also pointed out that inline text is not guaranteed — the legitimate Ubisoft client
writes only `ach_id`/`earned`/`earned_time` — which kills any design that treats the inline text as
the primary source.

**Change:** `ResolvePreferringInline` becomes `ResolvePreferringSchema` — the schema leads where it
defines that achievement name, inline text is the fallback. `ResolveFromDefinitions` and
`ResolveInline` (v1's split) fold into it, since neither had another caller.

The appid collision that motivated inline-first (Ubisoft and Steam id ranges overlap) is handled
better by the name match itself: a schema cached under a colliding id defines *other* achievements, so
`FindDefinition` misses and the inline text is used. Per-achievement rather than per-file, so a game
whose emulator names only partly match the schema resolves each achievement from whichever source has
it.

Per *field*, too — review caught this and it is not hypothetical. Steam redacts hidden achievements'
descriptions, so the Add game wizard run without a Firecrawl key writes `"description": ""` into a
schema that still carries a real `displayName`. Choosing a source wholesale would then blank a
description the unlock file did carry — a regression against rc1 for exactly the games this follow-up
is for. Each field takes the schema's text when it has any, the inline text otherwise; the icon can
only ever come from the schema.

`Resolve`'s lookup policy now splits by what is at stake:

| unlock file | lookup | why |
|---|---|---|
| no inline text | `Lookup` (rescan on every miss) | without a schema there is no notification at all |
| inline text | `LookupScanningOnce` (one rescan per appid) | the notification already works; the schema only upgrades it, so a config dropped in after startup is picked up without paying a library walk per unlock |

Everything else from v1 stands. The `earned: 0/1` tolerance the reporter offered as an alternative
("just accept it for all Steam games") was already implemented that way — universally, not gated on
detecting a Uplay install — so his second option needed no work.

## Follow-ups (deliberately not in v1)

- ~~**Game name and icons.**~~ Answered by the schema-first follow-up above: both come from a
  `steam_settings/` config the user places beside the game. The rejected alternative — discovering
  Uplay games by scanning `gamesPaths` for `achievements_schema.json` and matching a GSE Saves folder
  by achievement-name-set intersection — is still the only route for a game with *no* Steam config,
  and still not worth its cost.
- **A metadata-only mode for the Add game… wizard** — generate `steam_settings/` (schema + icons +
  `steam_appid.txt`) into a folder without locating or replacing a Steam DLL, so a non-Steam emulator
  user can produce the config from the app instead of copying one in. Considered for this follow-up
  and deferred: the reporter already has the config his emulator was configured against.
- **`gameNames` config map**, pending the reporter's preference.
- **Shared Gearhead strings.** The title and description are written verbatim in both
  `TrayApplicationContext` and `AchievementHistory`; consolidate while step 6/7 touch both.
- **`ExtractGameName` degenerate cases** — `"."` when `gameDir == basePath`, a bare drive letter when
  outside it.
- **`gamesPaths` stops being the whitelist.** Any self-describing folder becomes eligible; today
  `gamesPaths` is the only thing deciding which of a shared GSE Saves tree's folders produce popups.
  Document it; add an opt-out only if someone asks.
