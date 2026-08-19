# Per-game GBE overlay settings

Closes the memo of 2026-08-12, which tracks the closing comment on
[issue #5](https://github.com/AnotherSava/achievement-overlay/issues/5) by `ant-sh`: five optional
wishes, all of the form "read what the game's own GBE config already says".

## Goal

When a game's `steam_settings/` folder carries GBE overlay settings the user has already chosen, use
them for that game's popups instead of the app-wide values.

## What was asked, and what is being built

| # | Ask | Verdict |
|---|---|---|
| 1 | Unlock sound from `steam_settings/sounds/overlay_achievement_notification.wav` | **Build** |
| 2 | Display duration from `configs.overlay.ini` | **Build** |
| 3 | Position from `PosAchievement` | **Cut** — see "Why position is cut" |
| 4a | Font **file** from `Font_Override` + `steam_settings/fonts` | **Build** |
| 4b | Font and icon **sizes** from `Font_Size` / `Icon_Size` | **Drop** — see "Why sizes cannot be mirrored" |
| 5 | "at minimum expose a font size — the default reads as very small" | **Already shipped**, plus a defect fix |

## Where these files actually come from

This is the fact the whole feature rests on, so it was checked against real installs rather than
assumed.

- **Repacks and scene releases** bundle a pre-made `steam_settings/`. This is the dominant source.
- **Generators** (`generate_emu_config` and successors) write the file with a key or two.
- **The GBE release** ships `steam_settings.EXAMPLE/configs.overlay.EXAMPLE.ini` as a template to
  rename and edit.
- **This app's own Add game wizard** writes a two-line stub —
  `[overlay::general] enable_experimental_overlay=0` — and nothing else.
- **Not the emulator.** GBE writes back only `configs.user.ini` (account name, steamid, language,
  ip_country) through `save_global_ini_value`. The release README's "some default configurations are
  saved" means that file alone.

A survey of the four configured games on the development machine:

| Game | `configs.overlay.ini` |
|---|---|
| The Coffin of Andy and Leyley | Repack theme: 20 colour keys, rounding, margins, `Font_Override=poppins.ttf`, `Font_Size=22.0`, plus `fonts/poppins.ttf` and a 153 KB `sounds/overlay_achievement_notification.wav` |
| Red Dead Redemption | `PosAchievement=bot_right` |
| Persona 5 Royal | `PosAchievement=bot_right`, overlay enabled |
| Elden Ring | `enable_experimental_overlay=0` only |

Two conclusions follow, and they set the priorities below. A **sound file and a font file are what
actually exist on disk**; duration and position are theoretical. And the Coffin theme sits behind
`enable_experimental_overlay=0`, so it has never rendered once — which is the clearest possible
statement of what these files are.

**The framing, which belongs in the docs and in the card copy:** for a game the wizard configured,
nothing reads `configs.overlay.ini` at all — the wizard installs the *regular* GBE build, which has no
overlay code. This feature is not mirroring what the user sees in-game. It is treating that file as
*a standard place where the user has already written down what they want*. Anything stronger is a
fidelity claim the feature cannot make.

## Why position is cut

It was the largest piece and has the smallest payoff:

- Both real-world instances set `bot_right`, which is GBE's own default and where the popup already
  goes. Honouring it is a no-op on every game surveyed.
- A per-game key that can move the popup somewhere the user cannot choose app-wide is incoherent, so
  it drags in a global `notificationPosition` setting, its card, its diff line and its docs.
- Placement is hardcoded across `NotificationWindow.RightAlignedLeft` / `SizeAndPosition` / the slide
  animation, plus three stacking call sites in `RecentAchievementsDisplay` and `SettingsWindow`, all
  of which assume a bottom anchor and upward stacking.
- It exposes a coordinate mismatch that cannot be fixed here: GBE positions against `io.DisplaySize`,
  the game's swap-chain surface, while this app positions against the work area of the foreground
  window's monitor. A bottom-right toast hides that; `top_center` would not.

If it is ever revived, the sane order is: extract the placement maths behind a
`NotificationPlacement.Place(anchor, …)` with `BottomRight` behaviour-preserving first, then add the
five other anchors together with the stacking direction and the settings card, then read
`PosAchievement` into it as a one-line change.

## Why sizes cannot be mirrored

GBE's notification box is `0.25 × render width` with a 16 px font and a 64 px icon. This app's popup
is a fixed 322 DIU tree with `Width="230"` text columns multiplied by one `ScaleTransform`. The
geometries are not similar, so `Font_Size` can only be *reinterpreted* — and any reinterpretation
silently fights the user's own **Popup size**, the setting `NotificationScale` exists to keep
singular. Honouring `Icon_Size` separately would additionally break the invariant
`RenderedWidth == DesignWidth × scale`, which both the placement maths and the settings footer depend
on.

Colours, rounding, margins and the animation duration are dropped for a different reason: the popup's
`#DD1A1A2E` look is the app's identity, not an emulator setting. It sits over a game, not over
Windows, and it is deliberately not meant to match either.

## Ask 5 is already answered, but the default was still too small

Popup size shipped in `d3b19d5` on 2026-08-18. The build the reporter complained about had the width
hardcoded at 15% of the screen with no control at all.

One defect survives, and it is exactly the complaint: `MinFactor = 0.75` let the popup draw **below
its own design size**. At the default 15% on a 1920 px display that is 288 px against a 322 px design
width — a scale of 0.894, giving a 12.5 px title and a 10.7 px description. The floor rises to 1.0, so
the popup is never drawn smaller than it was designed.

Two independent voices support this: the reporter, and the Coffin repack author, who raised
`Font_Size` from GBE's 16 to 22.

The consequences are accepted deliberately:

- The percent slider's floor becomes `ceil(322 / displayWidth × 100)` — 17% on 1080p. A user sitting
  at the 15% default who opens Settings and saves has `scale` rewritten to `"17%"`. Nothing moves on
  screen that the floor change did not already move, and the file now states what is actually drawn.
- On a narrow or heavily scaled display the floor can exceed `MaxScreenPercent` (1920×1080 at 200%
  scaling is 960 logical px, where the smallest drawable popup is 34%). The slider maximum is
  therefore held at or above its minimum, or it would snap to nothing.

## Design

### Precedence

Per key, weakest first:

1. **App config** (`config.json`) — the baseline for every field of every popup.
2. **The game's own `steam_settings/`** — wins for the keys it defines.

There is deliberately **no global `GSE Saves/settings/` layer**. It is not what the issue asked for,
it is empty on the development machine, and it drags in GBE's `local_save_path` precedence inversion,
which exists only because a global layer exists.

Within the game's folder, GBE merges four filenames into one key space — `configs.app.ini`,
`configs.main.ini`, `configs.overlay.ini`, `configs.user.ini` — with **first definition winning**, in
that order. The *section* decides meaning, not the filename, so all four are read.

Rules:

- **Master switch off** (`useGameOverlaySettings: false`) → layer 2 is not consulted at all.
- **Game not in `GameCache`** (a self-describing Uplay game, or one outside `gamesPaths`) → there is
  no folder to read, so layer 1 stands. This is permanent, not a gap: nothing maps such a game's
  appid to any location but its GSE Saves folder, and no emulator writes overlay config there.
- **Key absent or unparseable** → the key does not participate; the app value stands.
- **Key out of range** → clamped. Duration is held to 1–60 s, wider than the settings slider's 1–30
  because an ini value is deliberate, but not wide enough to pin a topmost popup up for minutes.
- **Nothing suppresses a notification.** `Notification_Duration_Achievement=0` and
  `disable_achievement_notification=1` are *not* honoured, diverging from GBE on purpose. Someone who
  set those to quiet GBE's own overlay and then installed this app to get notifications back would
  meet silence, and "the app shows nothing for one game" is the worst possible bug report to earn
  from an optional nicety.
- **The sound master switch stays app-owned.** `soundEnabled: false` is silence whatever a game
  ships; only the *file* is overridable.
- **A game's sound that fails to play falls back to the built-in one.** An override must never leave
  the user worse off than no override. A path the *user* typed does not fall back — silence is the
  honest report that the file they chose is wrong.
- **The recent panel and the "Show me" preview never use layer 2.** The panel stacks entries from
  several games at once, so no single game's config can speak for the stack.

### Deliberate divergences from GBE's parser

GBE compares `[overlay::appearance]` keys against their stored spelling with `std::string::compare`,
which makes them **case-sensitive** — `font_size=22` is silently discarded, and if a local file and a
global one spell one key differently, both are lost. This app reads keys **case-insensitively**
everywhere. Under the framing above the file is a statement of preference, not a live mirror, so the
only reader that would disagree is one that never reads the file for a wizard-configured game.
Accepting more is the friendlier failure.

Everything else follows GBE: full-line `;` and `#` comments only (no trailing comments), no quote
stripping, duplicate key in one file means last wins, empty value reads as absent, key-only lines are
dropped, section names are case-insensitive, and a malformed number costs that one key rather than
the file. Numbers take their leading numeric prefix, matching `std::stof`, so `7.0s` reads as 7.

### Shape

```
GbeOverlaySettingsReader.Read(steamSettingsDir)   IO + caching, one owner
  → IniFile.Parse × 4, folded with WithFallback   pure
  → GameOverlayConfig.Parse                       pure: duration + raw font name
  → GameOverlaySettings                           absolute paths that exist on disk

NotificationAppearance.Resolve(settings, game)    pure: the whole precedence, one function
  → NotificationWindow / UnlockSoundPlayer
```

The reader caches per folder and re-parses when any of six timestamps moves — the four ini files plus
the `sounds/` and `fonts/` folders, so a wav or a ttf appearing is noticed too. It is **not** part of
`GameCache`, whose entries are replaced wholesale on every `ScanAll`; parsed state hung off it would
be discarded on every settings save.

`Resolve` is where the master switch and every clamp live, so all of it is unit-testable without a
window, and the two places that build a `NotificationAppearance` today collapse into one.

## Implementation

### 1. Readability floor — `src/NotificationScale.cs`, `src/SettingsWindow.xaml.cs`, `src/SettingsWindow.xaml`

`MinFactor` 0.75 → 1.0. `ApplyScaleSliderRange` holds the maximum at or above the minimum. The card
is renamed **Popup width** → **Popup size** and its description points at the complaint it answers.
Ships alone; independent of everything below.

### 2. INI reading — `src/GbeOverlay/IniFile.cs`, `src/GbeOverlay/GameOverlayConfig.cs`

Pure, no IO. `IniFile.Parse` / `Get` / `WithFallback`, and a projection to the two keys this app
reads: `Notification_Duration_Achievement` and `Font_Override`. Nothing speculative — the keys that
are dropped above are not parsed "for later".

### 3. The reader — `src/GbeOverlay/GameOverlaySettings.cs`, `src/GbeOverlay/GbeOverlaySettingsReader.cs`

Folds the four files, resolves `Font_Override` (absolute as given; relative against
`steam_settings/fonts`), probes `sounds/overlay_achievement_notification.wav`, and caches on the six
timestamps. Never throws — a disconnected drive must not reach `DispatchNext`.

### 4. Resolution — `src/NotificationAppearance.cs`

Reshaped from a positional record to init-only properties with a single
`Resolve(SettingsData, GameOverlaySettings?)`. `From(AppConfig)` and `From(SettingsData)` are
`Resolve(…, null)`, so the settings window stops hand-building the record.

### 5. Sound — `src/SoundPlayer.cs`, `src/NotificationQueue.cs`, `src/RecentAchievementsDisplay.cs`

`Play()` is deleted; the one entry point is `Play(enabled, path, fallBackToDefaultOnError)`. The
single-slot player cache becomes a small per-path one — per-game sounds make alternation normal, and
`Load()` is synchronous on the dispatcher.

### 6. Font file — `src/PopupFontLoader.cs`, `src/NotificationWindow.xaml.cs`

`new FontFamily(@"C:\…\poppins.ttf")` does not fail; WPF resolves the unknown *name* through fallback
and quietly draws Segoe UI. The working form needs the folder as a base URI and the font's own family
name, which is why this is a loader rather than one line at the call site.

### 7. Wiring — `src/NotificationQueue.cs`, `src/AppConfig.cs`, `src/SettingsDiff.cs`, `src/TrayApplicationContext.cs`, `src/SettingsWindow.xaml*`, `config/*.json`

The new `useGameOverlaySettings` key, its toggle card on the Notifications page, its diff line, and
the reader handed to `NotificationQueue`. No `ApplySettings` branch: the switch is read live per
popup, and the reader is keyed by game folder, so neither a `gseSavesPaths` change nor a rescan can
stale it.

## Tests

- `tests/NotificationScaleTests.cs` — the floor never draws below the design width, and still clamps
  at the top.
- `tests/GbeOverlay/IniFileTests.cs` — comment forms, trailing `#` is part of the value, quotes are
  kept, duplicate key, case-insensitive sections and keys, key without `=`, empty value, whitespace,
  and both directions of `WithFallback`.
- `tests/GbeOverlay/GameOverlayConfigTests.cs` — duration in seconds, numeric prefix, unparseable
  number leaves the key unset without costing the next one, zero and negative durations are ignored
  rather than suppressing, `Font_Override` is read from any of the four files.
- `tests/GbeOverlay/GbeOverlaySettingsReaderTests.cs` — temp-dir fixture: first definition wins across
  the four filenames, relative and absolute font paths, a missing font file leaves the key unset, the
  sound is found and a missing one is not, an empty folder reads as nothing, and an edited ini is
  picked up without a restart.
- `tests/NotificationAppearanceTests.cs` — the precedence itself, per field.
- `tests/SettingsDiffTests.cs` — the new key is reported.

## Follow-up: a game's folders are plural, and some are hidden (2026-08-19)

Testing the finished feature against the one real install that ships a font and a sound — The Coffin
of Andy and Leyley — showed it having no effect, from two separate defects rather than from the
design.

**Hidden folders were invisible.** `C:\Games\The Coffin of Andy and Leyley\steam_settings` carries the
`Hidden` attribute, and `EnumerationOptions.AttributesToSkip` defaults to `Hidden | System`, so the
scan never descended into it. The tell was in the log: one `Cached` line for the appid and no
`Skipped` or `Error processing` line — never enumerated, rather than enumerated and rejected. Repacks
hide these folders routinely, so a game whose *only* config folder was hidden went untracked
entirely, and the Add game wizard could not find its DLL or appid either. Every recursive scan now
shares `AppUtilities.RecursiveScan`, which skips System alone — enough to stay out of `$RECYCLE.BIN`
and `System Volume Information`, which are hidden *and* system.

**One game, several `steam_settings` folders.** Of the nine games on the development machine, three
have more than one: Coffin (root + `www/greenworks/lib/`), Atomfall (root + `bin/coldclient/`), and
Red Dead (two `steam_appid.txt` files naming one folder). The old code did `_cache[appId] = info` and
let the last enumerated win, silently and in filesystem order. `GameInfo.SettingsDirs` now holds all
of them, deepest first, grouped by appid **and** first-level game folder so an appid collision between
two installs cannot pool their settings. The reader folds them into a single key space, extending
GBE's first-definition-wins rule from four filenames to however many folders a game has, and probes
`sounds/` and `fonts/` in the same order.

Deepest-first is the rule because the emulator loads from beside its DLL, which is the nested copy in
every layout seen. It also matches what the old ordering happened to produce, so schema and icon
resolution did not move — they only stopped depending on enumeration order.

**Why folding is right rather than picking the authoritative folder.** The folder GBE actually reads
is the greenworks one, and it holds *less*: only `enable_experimental_overlay=0`. Being accurate about
which folder the emulator reads would have bought nothing. Under this feature's framing — a standard
place where the user has written down what they want, not a mirror of the emulator — the union across
a game's own folders is the thing being asked for. That is also why `DllLocator` was not used to pick
a winner.

Verified on the real install: the popup takes `poppins.ttf` and the unlock wav from the root folder
while the schema and icons still come from the greenworks one, with nothing moved on disk.

## Known limits

- A game outside `gamesPaths`, or one tracked only through a self-describing unlock file, can never
  have per-game settings. There is nowhere to read them from.
- `GameCache` keys on appid, so when two installs claim one appid the last one scanned still wins the
  cache entry and an unlock can be answered with the wrong game's settings. The grouping key keeps
  their folders from being *pooled*, which is the worse failure, but it cannot decide which install
  the unlock came from — nothing in the unlock file says. The log names the folders the values came
  from.
- Timestamp checks have the same one-tick granularity as `AppConfig.Reload`: two writes inside one
  filesystem tick and the second is missed until the next change.
- A font WPF cannot parse (`.ttc`, a corrupt file) yields no families and no exception, so the loader
  logs and falls back rather than failing loudly.
