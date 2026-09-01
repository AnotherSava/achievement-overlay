# CLAUDE.md

## What This Is

C# WPF app — Steam-like achievement overlay notifications for GBE-configured games. Monitors `%appdata%/GSE Saves/` for `achievements.json` changes via `FileSystemWatcher` and displays transparent popup notifications over the game window. No game process interaction — purely filesystem-based.

Stack: .NET 10, WinForms tray + WPF notification and settings windows.

## Tracking-configured notification

A synthetic "Gearhead" achievement fires the first time the app sees a game's `%appdata%/GSE Saves/<appid>/` folder appear (GBE creates it on first run) — confirming tracking is live before any real unlock. `AchievementWatcher` raises `GameFolderObserved` from two places — a second `FileSystemWatcher` watching for bare folder creation (the `achievements.json` filter doesn't fire on that), and the first successful read of a folder's `achievements.json`, which carries the parsed states on the event so the handler needn't re-read a file the emulator may still hold open. The folder-creation raise is deliberately unguarded, since at that moment there is usually no file yet to judge the game by. `TrayApplicationContext.NotifyTrackingConfiguredForExistingFolders` sweeps already-existing folders at startup and again after **Add game…** (a game configured mid-session may already have a folder from an earlier run, so its creation event has been and gone). `TrayApplicationContext.TryNotifyTrackingConfigured` gates it (game known in `GameCache`, not already shown, and a *known* earned count of zero — an unlock file that exists but can't be read counts as unknown, never as zero), shows it via `NotificationQueue.EnqueueSynthetic` (bypasses the schema lookup), and persists `appid → fire-time` in the `trackingConfigured` map in `config.json` (once per game). It also appears in the Recent panel, timestamped by fire-time. Bundled icon at `src/Assets/tracking_configured.jpg`; embedded-resource extraction shared via `src/EmbeddedAssets.cs`.

## Self-describing unlock files (non-GBE emulators)

GBE's GSE Saves `achievements.json` holds only `earned`/`earned_time`, so display text comes from the
game's `steam_settings/achievements.json` via `GameCache`. Other emulators — the Goldberg Uplay R2
one, per [issue #5](https://github.com/AnotherSava/achievement-overlay/issues/5) — write the same
file with a numeric `earned` and the `displayName`/`description` inlined per entry. Such a file is
*self-describing*: `AchievementMetadata.HasInlineText`/`IsSelfDescribing` detect it, and the game is
tracked with no `steam_appid.txt`, no `steam_settings/`, and no `gamesPaths` entry.

`AchievementMetadata.ResolvePreferringSchema` is the single place the source is chosen — the game's
schema leads where it defines that achievement name, inline text is the fallback, and the choice is
made **per field**: a schema can name an achievement and still leave a field blank (Steam redacts
hidden achievements' descriptions, and the Add game wizard writes them empty when no Firecrawl key
fills them in), so choosing wholesale would discard text the unlock file did carry. Schema-first is what
gives such a game icons and localised text: the reporter's Uplay emulator is configured to emit the
game's real Steam achievement names, so a game that also has a `steam_settings/` config under
`gamesPaths` is just a Steam game with an unusual writer. Matching on the achievement name is the
appid-collision guard that inline-first used to be — a schema cached under a colliding Ubisoft id
defines other achievements, so it doesn't match and the inline text stands. Both the popup path
(`Resolve`) and the Recent panel call it, so the two can't disagree about an achievement's text;
`Resolve` adds the cache lookup: a full rescan on a miss when there is no inline text (no schema means
no notification at all), and `GameCache.LookupScanningOnce` — one rescan per appid — when there is,
since then the schema only upgrades a notification that already works. Tolerance
lives in `FlexibleBooleanConverter`/`FlexibleInt64Converter` (property-scoped, so the shared
`JsonOptions` is untouched) and in `ParseUnlockStates`, which converts entries individually so one
bad value costs one achievement rather than the whole file.

Consequences elsewhere: `AchievementWatcher` seeds unconditionally (an appid that becomes resolvable
later would otherwise replay its backlog), seeds rather than notifies for a folder that appears after
`Start()`, subscribes `Renamed`, and raises `GameFolderObserved` from the file path as well as the
folder path. An empty `GameCache` is a warning, not a fatal error. A self-describing game with no
`steam_settings/` of its own gets no icons and no game name — the Recent panel falls back to the
appid. Plan: `docs/plans/completed/2026-08-11-uplay-emulator-support.md` (its final section covers the
schema-first follow-up).

## Config generator (Add game dialog)

The tray menu's **Add game…** item opens `AddGameForm` — a wizard (despite the legacy class name) that is a self-contained replacement for the unmaintained `generate_emu_config`. It walks through pages (game folder → AppID, shown only if not auto-detected → API key, shown only if not already saved → hidden-achievements/Firecrawl-key, shown only if the fetched schema has hidden achievements and no Firecrawl key is saved → ready/options → progress). Detection happens when leaving the folder page; the achievement schema is fetched when leaving the last input page (to validate the AppID/key and decide the hidden step) and reused by the generator via the `prefetchedSchema` ctor arg. It produces a GBE-compatible `steam_settings/` folder: locates the Steam DLL, resolves the AppID, fetches the achievement schema + icons from the Steam Web API, fills in hidden descriptions by scraping SteamDB through the Firecrawl API (`SteamDbScraper`; SteamDB is behind Cloudflare so a plain HttpClient can't reach it), downloads the GBE release (`SharpCompress` for 7z), backs up and replaces the DLL, and writes `steam_interfaces.txt` via GBE's bundled tool.

The engine lives under `src/GbeConfig/`. Modules keep parsing logic in pure static methods (unit-tested in `tests/GbeConfig/`) separate from the network/IO/subprocess work in `GbeConfigGenerator`, which is front-end agnostic — it reports progress through `IConfigProgress` (the dialog implements it to drive its checklist + log) and takes a `ConfigRequest`. The Steam Web API key and optional Firecrawl API key are stored as optional fields in the app's own `config.json` (via `SettingsData`/`AppConfig`).

After a run, `TrayApplicationContext.RegisterNewGame` ensures the game's folder is covered by `gamesPaths` (using `GamesPathPlanner`), rescans `GameCache`, and re-seeds the watcher so the game is tracked without a restart.

The original plan for this feature (written for a CLI; the front-end was later changed to the dialog) is at `docs/plans/completed/2026-06-18-gbe-config-generator.md`.

## Settings window

The tray menu's **Settings…** opens `SettingsWindow` — four pages behind a nav rail (General,
Notifications, Folders, Advanced), one card per setting with its explanation visible rather than
hidden in a tooltip. Built to the `5a`–`5d` frames of the Claude Design project *Windows app settings
dialog redesign*.

**WPF, not WinForms.** The design needs a toggle switch, sliders and OS dark mode, none of which
WinForms has; the app already hosts WPF for the notification itself, so this adds no dependency.
`ThemeMode="System"` (WPF's Fluent theme) supplies the control styles, the light/dark switch and the
system accent — only the chrome the theme has no key for (page, card and nav colours) is defined in
`ApplyThemeBrushes`, matched to the mode read from the registry. The popup still does **not** follow
the Windows theme or accent — it sits over a game, not over Windows, so the two are not meant to
match. `#DD1A1A2E` is now its default rather than its only colour: only an explicit choice moves it,
and the text colours are derived from that choice rather than configured beside it.

The window writes nothing itself. It returns a `SettingsResult` holding the values that changed —
`SettingsDiff.Compute` diffs the collected `SettingsData` against the snapshot the window opened
against, keyed by property name — and `TrayApplicationContext.ApplySettings` persists them through
`AppConfig.UpdateConfigValues` (one file write, not one per field) and re-wires whatever binds a
changed value at startup. That diff is load-bearing rather than an optimisation: it's how the host
knows to re-register the hotkey, rescan `GameCache`, or rebuild `AchievementWatcher` over new
`gseSavesPaths` (it binds its paths at construction, and `Start()` re-seeds from disk so the new
paths' backlog is recorded rather than replayed). Values read live on every use — sound, duration,
language, font, scale, recent count — need nothing beyond the write.

**Pause notifications** is deliberately absent: a momentary tray toggle, not a setting. The
`trackingConfigured` map stays out too — app-managed state, and `SettingsDiff` never compares it, so
a save can't drop a game's setup confirmation.

Page-specific notes:

- **Achievement text** (the `language` key) is a combo box listing what the installed games actually
  provide. `AchievementMetadata.CollectLanguages` reads the keys off a multi-language
  `displayName`/`description` object, and `TrayApplicationContext.AvailableLanguages` unions **both**
  sources of display text: every game's schema, and every GSE Saves unlock file — a self-describing
  game has no schema, so its unlock file is the only record of the languages it can show. Steam's
  `token` key is excluded; it sits in the same object but is a localisation token, and picking it
  would put `NEW_ACHIEVEMENT_1_0_NAME` on screen as the achievement name. It stays **editable on
  purpose**: the list cannot be proven complete (a fresh install offers only english), and
  select-only would leave a legitimate language reachable only by hand-editing `config.json`.
- **Popup size** (the `scale` key) is deliberately *one* setting rather than a size plus a separate
  text size. They overlap — scaling already enlarges the text — and a large popup with small text is a
  combination neither value alone would explain. It is also the answer to "the text is too small",
  which is why the card says so; see the floor note under **Per-game overlay settings**.

  There is also **no "automatic" mode**, because automatic was only ever *15% of the display's width*.
  Expressed in a unit the user picks, that is just the default value, so the mode disappears and the
  number on screen means something: `NotificationScale` is `"15%"` (a share of the display) or
  `"384px"` (absolute), written self-describing so the unit survives the round trip. The earlier
  design drew this as Automatic/Fixed with a percentage of the 322 px *design* width — an abstract
  number ("119%") that told the user nothing.

  `NotificationScale.WidthOn` gives the requested width; `NotificationWindow.ComputeScale` clamps it
  to the readable range and is the single pure calculation behind both what the footer reports and
  what is drawn, so the two cannot disagree. Switching unit in the window carries the current width
  across rather than the bare number, so the popup doesn't jump (15% of 2560 → 384 px).
- **Font** applies to the whole popup by being set on the window, which every `TextBlock` inherits —
  one assignment, no per-element drift. An unknown family is not an error: WPF falls back (a *file*
  is a different matter — see `PopupFontLoader` under **Per-game overlay settings**). The picker
  is a shortlist rather than every installed family, because achievement text is localised and a
  picker of everything invites one with no Cyrillic or CJK coverage.
- **Shortcut** captures keystrokes rather than accepting typed text, so its value always round-trips
  through `GlobalHotkey.ParseHotkeyString`; note `Keys.ToString()` can return an alias (`OemQuestion`
  → `Oem2`), which parses back fine.

  Capturing needs two defences, because **a global hotkey beats the focused window** — that is the
  whole point of `RegisterHotKey` — so a combination already spoken for never reaches the field, and
  the shortcuts most worth reassigning are exactly the unreachable ones:
  - *Ours*: `OpenSettingsDialog` suspends the hotkey for the window's lifetime and rebuilds it from
    config in a `finally`, so a cancel re-registers the unchanged value. `ApplySettings` therefore
    does **not** touch the hotkey.
  - *Everyone else's*: `LowLevelKeyboardHook` (`WH_KEYBOARD_LL`), installed only while the field has
    focus. It runs ahead of every hotkey owner and returns 1 to swallow the keystroke, so another
    app's hotkey — or an Explorer "Shortcut key" on a `.lnk`, registered the same way — does not
    fire. It is *system-wide*, so `OnHookedKeyDown` bails out unless this window is active with the
    field focused, and both blur and close uninstall it.

  Both paths funnel into `TryCaptureShortcut` so they cannot disagree. The hook reports WinForms
  `Keys` (physical, e.g. `LControlKey`); the WPF fallback converts through
  `KeyInterop.VirtualKeyFromKey`, so both spellings are filtered.
- **Sound** is a radio pair (Built-in / Custom file) rather than a path box that means "built-in" when
  empty. Choosing Built-in writes `soundPath: ""`, so config still says plainly which is in use.
- **Folder cards** show a live status line — how many games were found, or that a drive isn't
  connected — which is the check that used to run only on OK. Entries round-trip **raw** (no
  expansion), and a freshly picked folder is packed back through
  `AppConfig.CollapseEnvironmentVariables`, the inverse of `ExpandEnvironmentVariables`. Without that,
  editing the default `%appdata%\GSE Saves` would pin it to one machine's user profile, which matters
  because this config is used from more than one. Collapsing takes the *deepest* matching folder
  (`%localappdata%` over `%userprofile%`) and only on a separator boundary, so `C:\Users\Bobby` never
  collapses against `C:\Users\Bob`. Existing entries are left exactly as written.
- **Popup position** carries GBE's own six spellings, so the value matches what a
  `configs.overlay.ini` says. Six rather than four because the one argument for cutting the centre
  pair — that centring exposes the work-area-versus-game-window mismatch — turns out to apply to the
  corners equally.

  The control is a picture, and two earlier versions of it were not. It is one frame at display
  proportions holding six radio cells; each cell draws a rectangle at the popup's own 322 × 95
  proportion, aligned to the corner or edge that cell means. What it went through, because each step
  looked reasonable in the markup and wrong on screen: bordered cells each holding a *centred* bar
  said nothing about position — every cell looked identical and the highlight did all the work; and
  the resting borders made the frame read as six buttons rather than as one screen. Every marker is
  the same size, so the frame stays honest about what a popup looks like; the chosen one differs by
  colour and full opacity against the others' half strength, and the cell fill appears only on hover
  and keyboard focus.
- **Popup background** is one `#AARRGGBB` value behind two controls, because WinForms' `ColorDialog`
  cannot carry alpha (its result is a 24-bit COLORREF) and WPF ships no picker at all. The opacity
  slider's unit is the **alpha byte**, not a percentage: 93 of the 154 alphas in range don't survive a
  whole-percent round trip — the shipped `0xDD` is one of them (221 → 87% → 222) — so a percent slider
  would rewrite the colour on every no-edit save and make `SettingsDiff` report a phantom change.
  There is no free-text hex field, which is why neither card needs a `FindProblem` entry.
- **Show me** needs no special handling for the window it covers: the notification is topmost, so it
  draws over the window, and the window can be moved. A width or position change clears the previews
  first — different widths can't stack into a tidy column, and a position change would orphan the
  stack in the old corner. A colour change does not: two colours side by side is a comparison.

Validation blocks only the two entries that would fail silently — a GSE Saves list where nothing
exists, and a missing custom sound file — and switches to the page that needs fixing before saying so.

The folder picker and browse button are shared with `AddGameForm` via `src/DialogControls.cs`;
`PickFolder` takes a nullable owner so the WPF window, which has no `IWin32Window`, uses the same one.
## Popup position and background colour

Both are app settings, both read live per popup, and neither has a per-game override. The placement
maths lives in one pure module — `NotificationPlacement` — which the unlock popup, the recent panel
and the settings preview all call, so the three cannot disagree about where an edge is or which way a
stack grows. The slide distance is one **signed** number, so flipping the animation without also
flipping the resting position is not expressible. `PopupPalette` derives every text colour from the
fill at the WCAG crossover luminance 0.179129, and returns exactly today's four colours on
`#DD1A1A2E`, so the default look is preserved by construction rather than by promise.

A game's own `PosAchievement` and colour keys stay unread, and the reasoning is not the old one: a
position is not additive the way a sound, a duration or a font is, so a game's ini must not move a
popup the user has just placed deliberately — and the recent panel, app-owned by construction, would
then review that unlock in a different corner from the one it appeared in.

Plan, with the measured contrast figures and the limits that are accepted rather than engineered
around: `docs/plans/2026-08-30-popup-position-and-background.md`.

## Per-game overlay settings

A game that arrived with someone else's `steam_settings/` — a repack, a scene release, an old config
generator — may already state an unlock sound, a display duration and a font. Those three are honoured
for that game's popups when `useGameOverlaySettings` is on. Everything else GBE's
`configs.overlay.ini` can say is deliberately ignored; the reasoning, the survey of what real installs
actually contain, and why position was cut are in
`docs/plans/completed/2026-08-18-per-game-overlay-settings.md`.

**Nothing writes these files but a third party.** GBE writes back only `configs.user.ini`, and this
app's own wizard writes a two-line stub and installs the *regular* GBE build, which has no overlay
code and never reads the file. So the feature is not mirroring what the user sees in-game — it reads
that file as *a standard place where someone has already written down what they want*. Any wording
stronger than that is a fidelity claim it cannot make, which is why the settings card and
`docs/pages/development/gbe-reference.md` both say it plainly.

The chain is `GbeOverlaySettingsReader.Read` (the only IO and the only cache) → an `IniFile.Parse` per
config file per folder, folded with `WithFallback` → `GameOverlayConfig.Parse` → `GameOverlaySettings`
(absolute paths that exist) → `NotificationAppearance.Resolve`. Everything but the reader is pure, so the precedence is
unit-tested without a window, and `Resolve` is the single expression of it — the unlock popup, the
recent panel and the settings preview all go through it, so the three cannot disagree about a font or
a duration.

Load-bearing details:

- **A game usually has more than one `steam_settings` folder, and they hold different things.** A
  repack decorates the copy at the game root while the emulator reads a bare one beside its DLL
  (`bin/coldclient/`, `www/greenworks/lib/`). `GameInfo.SettingsDirs` holds all of them, deepest
  first, and the reader folds them into one key space with the same first-definition-wins rule GBE
  applies across its four filenames — so a sound or font living only in the copy GBE ignores is still
  used. `MetadataPath` stays the deepest folder, so schema and icon resolution are untouched.
  Grouping is keyed by appid **and** first-level game folder: two installs claiming one appid are two
  games, and pooling their folders would answer an unlock with a mixture of both.
- **The scan reads hidden folders** (`AppUtilities.RecursiveScan`). Repacks mark `steam_settings`
  hidden routinely, and `EnumerationOptions` skips `Hidden | System` by default — which made such a
  game untracked with nothing in the log to say why, since the folder was never enumerated rather
  than enumerated and rejected. System stays skipped, which is what keeps a scan out of
  `$RECYCLE.BIN` and `System Volume Information` (hidden *and* system). Every recursive scan in the
  app shares that one options property, the wizard's included.
- A game with no `steam_appid.txt` or outside `gamesPaths` (the self-describing Uplay case) can never
  have per-game settings: nothing maps its appid to a folder.
- The reader is **not** part of `GameCache`, whose entries are replaced wholesale on every `ScanAll`.
  It caches against six timestamps per folder — the four ini files plus the `sounds/` and `fonts/`
  folders, since a folder's own timestamp moves when a file appears inside it, which is how a wav or
  a ttf added without an ini edit is noticed. The cache key is the whole folder list, so a game whose
  set of folders changes re-reads rather than serving the old answer.
- **Nothing suppresses a notification**, diverging from GBE on purpose. `Notification_Duration_Achievement=0`
  and `disable_achievement_notification=1` mean silence over there; honouring them would let a stale
  ini quietly stop this app from notifying for one game, which is the worst bug report an optional
  nicety could earn. Keys are also read case-insensitively, where GBE compares stored spellings and
  drops `font_size`.
- **The sound master switch stays app-owned**: `soundEnabled: false` is silence whatever a game ships,
  and only the *file* is overridable. A game-supplied wav that won't load falls back to the built-in
  sound (an override must never leave the user worse off); a path the user typed does not, because
  silence is the honest report that their file is wrong. That asymmetry is the whole reason
  `NotificationAppearance.SoundIsFromGame` exists.
- **The recent panel and the settings preview never use a game's settings** — the panel stacks entries
  from several games at once, so no one game can speak for the stack. That is what
  `NotificationAppearance.From` is.
- `PopupFontLoader` exists because `new FontFamily(@"C:\…\poppins.ttf")` compiles, throws nothing and
  silently draws a fallback family (measured: Arial). A file has to be addressed as a base URI plus
  the family name inside it, via `Fonts.GetFontFamilies(Uri, string)` — and by file name, not by
  folder, since the folder overload returns whatever else sits beside it.

`NotificationScale.MinFactor` is **1.0**, not a fraction: the popup is laid out at a size chosen to be
readable, so drawing below it is drawing text nobody sized. It binds at the default — 15% of a 1920 px
display is 288 px against a 322 px design width. The consequence is that the percent slider's floor
becomes `ceil(322 / displayWidth × 100)`, which can exceed `MaxScreenPercent` on a narrow or heavily
scaled display, so `ApplyScaleSliderRange` holds the maximum at or above the minimum.

## Config files

`config/default.json` is the committed config that ships as `config.json` next to the exe. For local
builds/deploys, a gitignored `config/local.json` takes precedence (the csproj links whichever exists,
preferring local) — use it to keep your personal `steamWebApiKey` / `firecrawlApiKey` out of git. On
CI/release builds `local.json` doesn't exist, so `default.json` is used. `config/local.json` is
created from the committed defaults plus credential placeholders; fill them in after first checkout.

Fill the keys in **`config/local.json`**, not in the running app. Deploy copies `local.json` over the
installed `config.json`, so a key typed into the Add game wizard (which writes the *installed* file)
survives only until the next deploy — an empty placeholder in `local.json` silently blanks it.

## Encrypted files

The `track-achievements` skill under `.claude/skills/` is the operational runbook for putting a game
on this machine under GBE tracking — the counterpart to the Add game wizard, for the cases a GUI can't
cover. It is stored encrypted: `.gitattributes` routes that directory through transcrypt, so the files
are plaintext in the working tree and ciphertext in git.

The whole directory is encrypted rather than individual `*.secret.*` files because Claude Code
discovers a skill by its literal `SKILL.md` name, which leaves no room for the marker in the filename.

A fresh clone reads those files as base64 and the skill won't register until the repo is unlocked with
transcrypt and the shared passphrase. Everything the runbook covers that is safe to publish lives in
`docs/pages/development/gbe-reference.md` and `docs/pages/usage/playnite.md` instead — put new
findings there by preference, and keep the encrypted files for what genuinely can't be public.

## Build & Test

```
dotnet build src/AchievementOverlay.csproj -c Release
dotnet test tests/AchievementOverlay.Tests.csproj
```

`.claude/commit-checks.sh` is the gate `/commit` runs before it will propose a commit, and it is
stricter than either line above on purpose. It builds the **test** project (which pulls in `src`
through the project reference, so warnings in test code are seen at all), with `-warnaserror` and
`--no-incremental` — MSBuild skips analysis for unchanged projects, so a cached build reports no
warnings even when the code still has them. A `CS8625` in a test reached a release that way once.

## Documentation site

User-facing documentation is a GitHub Pages site under `docs/`, built by Jekyll with the
`just-the-docs` remote theme. The sidebar is generated from each page's `nav_order` / `parent` /
`has_children` front matter — never write a manual nav strip — and internal links carry no `.md`
extension, because GitHub Pages serves `pages/usage/settings.html` at `pages/usage/settings`.

The README is an entry point, not a manual: tagline, hero screenshot, feature list, licence, and a
footer indexing every page. Its tagline, context paragraph and feature bullets must match
`docs/index.md` word for word, and no capability may appear in both the context paragraph and a
feature bullet. Anything longer than a paragraph belongs on a page instead.

Three things bite here:

- `.gitignore` ignores `docs/*` and re-includes the site by name, so a **new top-level file or
  folder under `docs/` is invisible to git until its `!` line exists**. Nothing warns you.
- `_includes/nav_footer_custom.html` is what removes the theme's footer attribution, and every
  obvious guess about it is wrong. Not `footer_custom.html` (that is a `site.footer_content` hook).
  Not an *empty* file — the theme guards on `!= ""` and ships a 0-byte copy, so empty prints the
  line. And nothing in it may contain `{%` or `{{`, even inside a comment: an include is parsed as
  Liquid, so quoting the theme's own guard there fails the Pages build outright.
- The site runs at the theme's own `$content-width` deliberately. Overriding it is not a width
  knob: `md` and `lg` in the theme's `$media-queries` are *derived* from it, so raising it also
  raises the window width at which the sidebar stops being a mobile header — and it can't be
  decoupled, because `.main`'s `lg` margin is computed from the same variable.
- `remote_theme` is **pinned** to a release tag. Unpinned it resolves to the theme's default-branch
  HEAD on every rebuild, so an upstream commit changes this site with nothing changing here — and
  the footer include above depends on the exact shape of two theme partials. Bump it deliberately,
  and check what the site relies on still exists at the new tag.

## Documentation screenshots

`docs/screenshots/screenshots.json` records, per shot, what it shows, how to reproduce it, and
whether tooling may replace it — `auto`, `confirm`, or `never`, which is the user's to set. The
global `/documentation` skill reads it; nothing in the app does.

`docs/screenshots/capture/<id>.sh` deploys the working tree, then drives the app through `<id>.ps1` —
so a shot documents the code under review, not whatever release is installed. Shared pieces live in
`capture/lib/`: `ui-automation.ps1` (tray icon and its menu) and `window-capture.ps1`.

The `window-capture.ps1` helper captures twice over known backdrops and solves `O = C*a + B*(1-a)` per
pixel, producing a PNG with real alpha. That is not decoration: the popup is translucent by design, so
a plain screen grab bakes the wallpaper into it and no crop can separate them. For an opaque window
pass `-CropToOpaque` — its rounded corners are the window's own dark border curving inward, which is
chrome that alpha recovery correctly preserves rather than removes.

Keep these scripts pure ASCII. PowerShell 5.1 reads a `.ps1` as ANSI without a BOM, so a UTF-8 dash
inside a string breaks the parse with a misleading "missing closing '}'".
