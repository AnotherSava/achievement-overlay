# Popup position and background colour

Answers [issue #6](https://github.com/AnotherSava/achievement-overlay/issues/6) by `mohsinous`: two
asks, "change notification position (for example Top-Right)" and "change the background color".

## Goal

Let the user choose which corner or edge of the display popups appear at, and what colour the panel
behind their text is, without either choice being able to produce an unreadable or misplaced popup.

## What was asked, and what is being built

| # | Ask | Verdict |
|---|---|---|
| 1 | Notification position, "for example Top-Right" | **Build** — six anchors, GBE's own spellings |
| 2 | Background colour | **Build** — one `#AARRGGBB` value, with the text colours derived from it |
| — | A game's own `PosAchievement` / `Notification_R/G/B/A` | **Still not read** — see "Why a game's own position stays unread" |

Both were cut in [the per-game overlay settings plan](completed/2026-08-18-per-game-overlay-settings.md),
so this document's first job is to say which of those reasons were about the per-game version and
which survive an app-level setting.

## Which of the 2026-08-18 objections survive

The earlier "Why position is cut" gave four reasons. Read against a setting the user picks, three of
them evaporate and one becomes a documented limitation.

| Objection | Status |
|---|---|
| "Both real-world instances set `bot_right` … honouring it is a no-op" | **Gone.** It was an argument about reading a file, not about offering a choice. Nobody who asks for top-right already has it written in an ini. |
| "A per-game key that can move the popup somewhere the user cannot choose app-wide is incoherent, so it drags in a global `notificationPosition` setting" | **Inverted.** That global setting is precisely what is being asked for, and the earlier text even names it. |
| "Placement is hardcoded across `RightAlignedLeft` / `SizeAndPosition` / the slide animation, plus three stacking call sites" | **Survives as the cost, not as a reason.** It is real and it is most of the work — hence the extraction below, in the order that plan prescribed. |
| "GBE positions against `io.DisplaySize` … this app against the work area of the foreground window's monitor" | **Survives, narrowed.** It is fatal to *mirroring* a game's key. It is a documented limitation for a setting the user picks. |

The colour paragraph of that plan said the `#DD1A1A2E` look "is the app's identity, not an emulator
setting … it sits over a game, not over Windows, and it is deliberately not meant to match either".
That still holds, and is why the popup will not follow the Windows theme or accent the way the
settings window does. It is an argument against the popup being recoloured *by something else*. It is
not an argument against the user recolouring it, and `#DD1A1A2E` stays the default.

## The coordinate space, stated plainly

Placement is measured against the work area of the display the foreground window is on, not against
the game's own window — `AppUtilities.GetForegroundWindowRect` returns `Screen.FromHandle(...).WorkingArea`.

This is already true today: a bottom-right popup over a windowed game sits at the *screen* corner, not
at the window's corner. So the mismatch is uniform across all six anchors rather than specific to the
centre ones, and it is not a reason to offer fewer. It is one sentence in the README and one in the
issue reply.

Fixing it is out of scope and stays out: it would mean choosing between the game's client rect and the
work area per anchor, and the DPI story in `GetForegroundWindowRect` would have to be settled first on
a mixed-DPI multi-monitor machine. A feature that moved the popup would then be blamed for a
pre-existing inaccuracy it did not introduce.

## Why six anchors

The `NotificationAnchor` type carries exactly the six GBE names, verified in the emulator source
rather than inferred: `top_left, top_center, top_right, bot_left, bot_center, bot_right` (`dll/dll/settings.h`),
parsed in `dll/settings_parser.cpp`, with `ach_earned_pos = bot_right` as GBE's own achievement
default. So today's behaviour, GBE's default and this setting's default are the same value, and a
user who has edited a `configs.overlay.ini` reads a vocabulary they already know.

Six rather than four because the earlier plan prescribed "the five other anchors", because GBE names
six, and because the only argument for cutting the centre pair — that centring exposes the coordinate
mismatch — turns out to apply equally to the corners.

Vertical-centre anchors (`mid_left`, `mid_right`) are not built: GBE has no spelling for them, nobody
asked, and a vertically centred stack has no obvious growth direction — it either grows both ways or
re-centres on every entry, and neither is right by inspection.

## Why the text colour is derived, not chosen

The popup has four foregrounds tuned for a dark fill, and one of them — the recent panel's dismiss
hint — is assigned in C# rather than in XAML. A background setting on its own therefore produces
white-on-white the first time somebody picks a light colour, and a XAML-only fix silently misses that
line.

The alternative, letting the user set the foregrounds too, is the mistake `NotificationScale`'s own
doc comment already names about a size plus a separate text size: two settings that overlap, where one
combination is a state neither value alone explains. An unreadable pair must not be reachable.

So the ink is computed. The honest limit, which belongs in the card copy and in the README: contrast
is worked out against the colour as chosen, ignoring its alpha, because what sits behind a translucent
popup is a game frame nobody can predict.

## Design

### Shape

```
NotificationAnchor          value + tolerant parse + type-level JsonConverter   pure
NotificationPlacement       every "bottom" and "right" expression, once         pure
PopupBackground             one #AARRGGBB + tolerant parse + JsonConverter      pure
PopupPalette                background → ink, title, description, game line, footer, icon   pure

NotificationAppearance.Resolve(settings, game)   both values taken straight off `app`
  → NotificationWindow / RecentAchievementsDisplay / the settings preview
```

Everything above the resolver is pure and unit-tested without a window, which is the same division
`NotificationScale` and `GameOverlayConfig` already use.

### Placement

A new `NotificationPlacement` module absorbs `MarginFraction` (0.02), `SlideDistanceFraction` (0.015)
and `StackGap` (6). Those three are `private const` on `NotificationWindow` today, which is exactly why
`RecentAchievementsDisplay` re-states two of them as bare literals — a copy that cannot be checked
against its original.

```
IsTop(anchor)
Margin(area)                = min(area.Width, area.Height) × 0.02
SlideOffset(anchor, area)   = IsTop ? −(area.Height × 0.015) : +(area.Height × 0.015)
LeftFor(anchor, area, w)    = left ? area.Left + Margin : centre ? area.Left + (area.Width − w)/2
                                                        : area.Right − w − Margin
FlushEdge(anchor, area)     = IsTop ? area.Top + Margin : area.Bottom − Margin
TopFor(anchor, edge, h)     = IsTop ? edge : edge − h
Advance(anchor, edge, h)    = IsTop ? edge + h + StackGap : edge − h − StackGap
EdgeOf(anchor, top, h)      = IsTop ? top : top + h
StackSlideOffset(anchor, h) = IsTop ? −(h + StackGap) : +(h + StackGap)
Place(anchor, area, w, h)   → Placement(Left, Top, SlideOffset)
```

The slide offset is **one signed number**, and both of its uses stay written as they are today:
`Top = flush − slide` and `SlideTransform.Y = slide`. A popup therefore rests one slide distance in
from its edge and travels outward-to-inward at every anchor, and flipping the animation without also
flipping the resting position becomes impossible rather than merely unlikely.

### Two resting rules, both preserved

The unlock popup rests at `Bottom − h − margin − slide`; the recent panel's footer rests at
`Bottom − h − margin`, one slide distance (~16 px at 1080p) closer to the edge. Both are expressible —
`Place` for the first, `TopFor(anchor, FlushEdge(anchor, area), h)` for the second.

This disagreement is a real inconsistency, and extracting one module is what makes it visible. It is
still preserved exactly, because a pixel-identical diff on both surfaces is the only cheap
verification available for a refactor that touches five expressions in three files. Unifying it moves
something every user sees, and that belongs in its own commit with its own diff.

### Stacking

The `NotificationWindow.SlotHeight` helper is deleted rather than generalised. It takes one height and so cannot
express direction — and the codebase already contains both readings of it:

- `SettingsWindow.OnShowMe` passes the **incoming** window's height, which is correct for a stack
  growing upward.
- `SettingsWindow.RestackPreviews` passes the **outgoing** one's, which is not: the second preview
  lands at `anchor − h₀ − gap` where it needs `preview₀.Top − gap − h₁`.

Unequal-height previews therefore overlap today. It has never shown because every preview is the same
"Connoisseur" popup. Walking a running edge with `TopFor` and `Advance` takes one height per step and
is direction-correct in both, so all four stacking call sites collapse onto the same two lines and the
ambiguity has nowhere to live.

### The animation

Two changes, both no-ops at a bottom anchor:

- The easing in `SlideTo` becomes direction-derived (`top > Top ? EaseIn : EaseOut`). Its comment
  justifies ease-in as gravity — "the way a dropped thing lands" — and at a top anchor a closing gap
  moves survivors *upward*, where acceleration reads as a glitch.
- Remove `RenderTransformOrigin="0.5,1"`. It is a no-op for a pure `TranslateTransform` and encodes
  "bottom" in a file that will no longer have one.

The slide stays **vertical** at every anchor, unlike GBE, which slides its corner notifications
horizontally. The window is `SizeToContent="WidthAndHeight"` with the transform on the window, so the
transform moves content inside an HWND sized exactly to it — a horizontal slide would clip the popup's
own text.

### Colour — the value

Config holds one self-describing string, `#AARRGGBB`, the same rule `NotificationScale` follows: a
stored value carries its own meaning. A separate `notificationOpacity` key would be two sources of one
truth needing reconciliation on every read.

Parsing is hand-written and never touches `ColorConverter`, which has no `TryParse`, returns null for
null, throws `FormatException` for junk and `InvalidOperationException` on its `sc#` branch. The shape
is checked first (optional `#`, then 3, 4, 6 or 8 hex digits), then sliced; 3- and 4-digit forms expand
by digit doubling, 6 digits take the default's `0xDD` alpha, and anything else reads as `#DD1A1A2E`.

Two rules make it safe:

- **Alpha is clamped to `[0x66, 0xFF]` on parse**, so a hand-edited `#001A1A2E` cannot produce an
  invisible popup reported as "notifications stopped working". Clamping on construction is what
  `NotificationScale.ScreenPercent` already does.
- **The converter never throws.** An unexpected token returns the default and skips. This is a
  deliberate divergence from `NotificationScaleConverter`, whose `throw new JsonException` escapes
  `AppConfig.Load` into the constructor's catch and shows the startup config-error dialog — a real
  defect, tracked separately, and not one the new converters will share.

Opacity lives in the brush's alpha byte and nowhere else. The `Window.Opacity` property is owned by
the fade animations, which drive it to 1 on every show, so a user opacity written there would be
overwritten 300 ms in.

### Colour — the palette

The `PopupPalette` module is pure, and derives every text colour from the background:

```
ink   = RelativeLuminance(bg) ≤ 0.179129 ? White : Black
muted = ink is white ? #AAAAAA : #444444

Title        ink   @ FF   no floor
Description  muted @ CC   floor 4.5   (WCAG AA)
Game line    muted @ 99   floor 3.4   (what the shipped default already achieves)
Footer       ink   @ BB   floor 4.5

Rescue: a level below its floor blends (base, alpha) toward (ink, FF) by the smallest t
        that meets it; t = 1 when the floor is unreachable.
```

The crossover is `√(1.05 × 0.05) − 0.05 = 0.179129`, where black and white ink tie at 4.583:1 — not the
naive 0.5 midpoint, which would flip the ink while one side was still measurably worse.

Measured, and the reason the shape is a ladder rather than a formula: on `#1A1A2E` every level takes
`t = 0`, so the palette returns exactly `White`, `#CCAAAAAA`, `#99AAAAAA` and `#BBFFFFFF` — byte-identical
to what the XAML and `ShowFooter` hold today. **The default popup is unchanged by construction, not by
promise**, and the test that pins it is the most valuable one in this change.

| Background | Title | Description | Game line | Footer |
|---|---|---|---|---|
| `#1A1A2E` (default) | 17.1:1 | 5.17:1 | 3.50:1 | 9.61:1 |
| `#FFFFFF` | 21:1 | 5.46:1 | — | — |
| `#EEEEEE` | 18.1:1 | 4.95:1 | — | — |
| `#757575` (worst case) | 4.61:1 | 1.75:1 → **4.53:1 rescued** | — | 3.30:1 → 4.5:1 rescued |

The muted base is asymmetric (`#AAAAAA` against dark, `#444444` against light) because an 80%-alpha
brush blends toward its background, and a light one washes out faster: the symmetric `#555555` gives
4.05:1 on `#EEEEEE`, below AA, where `#444444` gives 4.95:1.

Two limits are accepted and written down rather than engineered around:

- Near the crossover no ink exceeds 4.58:1, so the game line cannot reach AA there. It does not reach
  AA on the shipped default either — 3.50:1, and always has.
- The icon's fallback trophy takes its ring and fill from the palette, because goldenrod-on-gold
  vanishes on a cream background. The 16 px tray icon is untouched: different API, different surface,
  and no user-chosen fill behind it.

### The settings cards

Both go on the Notifications page under the existing **Text & appearance** heading, beside **Popup
size**, so the three geometry-and-look settings form one group. The page intro loses its "bottom-right
of the display" clause.

- **Popup position** — "Which corner or edge of the display popups appear at. The recent achievements
  panel stacks from the same place." A 2 × 3 grid of radio buttons inside a bordered rectangle
  standing for the display, in the same 156-wide dock slot the other cards use, so the page rhythm is
  undisturbed. A picture of the answer, with mutual exclusion and arrow-key navigation for free.
- **Popup background** — "The panel behind the popup's text. Text and its contrast are worked out from
  the colour you pick." Row one holds swatch buttons: dark presets, the first labelled **Default** and
  restoring `#DD1A1A2E`, plus a custom slot showing `+` until it is set. Row two is an opacity slider
  with a percent readout, in the same divided two-row layout the Popup size card uses.

The slider's own unit is the **alpha byte** (102–255), with percent only displayed. This is not
stylistic: 93 of the 154 alphas in that range do not survive a whole-percent round trip, and the
shipped `0xDD` is one of them (221 → 86.67% → 87% → 222). With a percent slider, opening settings and
pressing Save with no edits would rewrite the user's colour and make `SettingsDiff` report a change
that never happened.

There is no free-text hex field, so neither card needs a `FindProblem` entry — a radio group, a picker
and a bounded slider cannot produce a value that fails silently, which is that method's stated
criterion. Hand-editors get the tolerant parser and a README row.

## Implementation

Four commits. The first changes no pixel and carries most of the risk.

### 1. Extract the placement maths — behaviour-preserving

Two new files, `src/NotificationAnchor.cs` and `src/NotificationPlacement.cs`, go in unread. Then
`tests/NotificationPlacementTests.cs` replaces `tests/NotificationStackTests.cs`, which is **rewritten
rather than ported**: it applies `heights[i]` both to the slot a popup leaves and to the gap it
asserts, so it passes under either stacking rule and cannot gate this change.

The three call sites — `NotificationWindow`, `RecentAchievementsDisplay` and `SettingsWindow` — then
route through the module at `BottomRight` only. That deletes `RightAlignedLeft`, `SlotHeight` and the
three constants; `_slideDistance` becomes a signed `_slideOffset`; `PlaceRightAligned` becomes `PlaceForStack` and gains
a doc line about its hidden `_recentMode = true`, which is the only thing stopping the hold timer and
is a lifetime decision currently hiding inside a placement method. The write-only
`CascadeContext.NotificationWidth` / `.Margin` / `.StandardSlideDistance` go with it.

Two deliberate exceptions to "behaviour-preserving", both to be named in the commit message:

- `RestackPreviews` and `OnShowMe` collapse into one walk, which fixes the overlap described above.
  Identical output whenever the heights are equal, which is every case today.
- `ApplyScale` takes the width from the rect it was handed instead of re-calling
  `GetForegroundLogicalWidth`. The two agree for the same focused window; this closes a focus-change
  race.

### 2. Honour the anchor — `bot_right` unchanged, five new positions

The config key, the diff line, `NotificationAppearance.Anchor` assigned straight off `app` with no
per-game branch, the anchor threaded into every placement call, the direction-derived easing, and the
**Popup position** card.

The `BottomRight` member must be 0: no existing `config.json` carries the key, so an absent key
deserialises to `default(NotificationAnchor)`.

Also assign `RenderedHeight` / `RenderedWidth` on the recent and footer paths. They are set only in
`SizeAndPosition` today, so every `ShowRecent` / `ShowFooter` window carries 0 — which is why the panel
reads `ActualHeight` instead. Nothing depends on it yet; this makes the field's own doc comment true.

### 3. The colour value and the palette

Two more pure files, `src/PopupBackground.cs` and `src/PopupPalette.cs`, go in with their tests and no
callers. Then the window
applies the palette in its constructor, beside the existing `FontFamily` assignment — the one place all
three `Show*` paths pass through. The four colour literals are **removed** from `NotificationWindow.xaml`
and `ShowFooter` rather than left as design-time values: two sources of one truth is exactly what
drifts.

### 4. The colour setting and its card

The config key, the diff line, `NotificationAppearance.Background`, `DialogControls.PickColor` (owner-
less `ColorDialog` with `FullOpen`, doing the `System.Drawing` conversion in one place, since
`ColorDialog` cannot return alpha and WPF ships no picker), and the **Popup background** card.

Both keys go into `config/default.json` **and** the gitignored `config/local.json`, which shadows it on
every development machine — editing only the committed file tests the C# fallback rather than the
shipped default.

No `ApplySettings` branch is needed for either key: both are read live per popup, the way the sound,
the duration and the language already are.

## Tests

Unit, in the order they gate the commits:

- **The behaviour-preserving pin.** `Place(BottomRight, …)` equals the literal expressions the old code
  produced, and `TopFor(FlushEdge(…))` equals the footer's separate rule. Both must be expressible or
  one of the two surfaces moves.
- **Every anchor.** Left, centred and right horizontals; margin-inset verticals at top and bottom; the
  placed box inside the rect at all six; and the same on an off-origin rect such as
  `(1920, 40, 2560, 1400)` — a secondary monitor with a top-docked taskbar, which is the arithmetic
  half of the DPI risk and needs no second monitor.
- **The signed slide.** Negative at the three top anchors, positive at the three bottom ones, magnitude
  1.5% of the rect height, and the resting position always inset from the anchored edge.
- **Stacking with distinct heights** `{95, 130, 71, 210}` at every anchor: the gap between adjacent
  popups is exactly `StackGap` each time. Substituting the neighbour's height gives `95 + 6 − 130 = −29`,
  an overlap — which is precisely what the deleted test could not see.
- **Anchor parsing.** The six spellings plus `TOP_RIGHT`, ` top-right `, `Bottom Right` and
  `bottomright`; empty, null, `middle` and `42` all read as `BottomRight`; every member round-trips.
- **Palette identity.** `For(#DD1A1A2E)` returns exactly `White`, `#CCAAAAAA`, `#99AAAAAA`, `#BBFFFFFF`.
  A failure here means the default popup's look has shifted for every existing user.
- **Ink flip and rescue.** Black ink on `#FFFFFF`, `#FFFF00`, `#EEEEEE`; white on `#1A1A2E`, `#000000`;
  a flip either side of 0.179129; and, over a grey ramp plus the six primaries, a description contrast
  of at least `min(4.5, contrast(ink, bg))` — hitting the floor wherever it is reachable and falling
  back to full ink where it is not.
- **Colour parsing.** Null, empty, `red`, `sc#1`, `#12345` and `#GGGGGG` all read as `#DD1A1A2E` without
  throwing; `#001A1A2E` clamps to `0x66`; `Parse(ToString(c)) == c`.
- **Startup hygiene.** A `config.json` holding `"notificationPosition": true` or
  `"notificationBackground": {}` still loads, at defaults, with no exception out of `AppConfig.Load`.
- **Config round trip.** `UpdateConfigValues` with a boxed anchor and a boxed background writes
  `"top_right"` and `"#DD1A1A2E"` — strings, not an integer and not an object — matching the existing
  scale round-trip test. The type-level converters are what make this work; a property-scoped one is
  skipped when the value is serialized on its own.
- **Diff and appearance.** Each key is reported only when changed; a save with no edits produces an
  empty change set; `Resolve` takes both from the app even when a `GameOverlaySettings` is supplied.

Manual, on screen:

- Commit 1 only: screenshot-diff an unlock popup and the recent panel against a pre-change build. Both
  pixel-identical, the footer's ~16 px inset difference included.
- **Show me** at each anchor: it lands there, slides in from the nearest edge, and repeated clicks stack
  away from the anchor at a constant gap. Let one expire and confirm survivors re-seat toward the
  anchor — falling at the bottom, rising at the top.
- Ctrl+Shift+H at a top anchor and a centre anchor, with five achievements of very different
  description lengths.
- A real unlock at `top_right`, with the game focused.
- Colour: white, `#FFFF00`, and a mid-grey near `#757575`; read the title, description, game line and
  the panel's dismiss hint at each — that hint is the one a XAML-only fix would miss. Then return to
  the Default preset and confirm the popup matches the pre-change screenshot.
- Opacity: drag to the 102 floor and back, confirm the fade still runs to full, then open and close
  settings with no edits and confirm `config.json` is untouched.
- Hand-edited junk (`"notificationPosition": "sideways"`, `"notificationBackground": "sc#1"`): the app
  starts and draws `bot_right` in `#DD1A1A2E`.
- Multi-monitor: the game on a secondary display, ideally at a different scale, unlocking at
  `top_left`.

## Why a game's own position stays unread

Nothing changes for `PosAchievement` or the `Notification_R/G/B/A` keys — `GameOverlayConfig` keeps
parsing two keys and no more. The question was re-opened deliberately once the app had settings of its
own, since every 2026-08-18 objection had been about a per-game key with no app-wide counterpart. It
was decided against on evidence, surveyed 2026-08-30 across the ten installs on the development
machine.

**The survey.** Eleven `configs.overlay.ini` files (Atomfall and Coffin each have two `steam_settings`
folders). A `PosAchievement` appears in three — NieR: Automata, Persona 5 Royal, Red Dead Redemption —
and **all three say `bot_right`**. Colour keys appear in exactly one, the Coffin repack's game-root
copy, which carries 24 of them and no position, so on this machine the two sets never co-occur.
The overlay is enabled in only two of the eleven, and not in the colour-bearing one. Four files are
the two-line stub this app's own wizard writes. Neither `PosInvitation` nor `PosChatMsg` appears, and
no `configs.main.ini` or `configs.user.ini` carries any of these keys.

**Why that settles position.** `bot_right` is GBE's compiled default for `ach_earned_pos`
(`settings.h:185`), the value in the shipped `configs.overlay.EXAMPLE.ini`, and this app's own default.
In every real instance the key's presence therefore expresses *no preference*. Honouring it changes
nothing today — and the moment someone uses the setting this plan exists to add, those three games
alone stay bottom-right while the other seven move, which arrives as "the position setting doesn't
work for some games". The damage is exactly correlated with uptake of the new setting: harmless for
everyone who never changes it, wrong for precisely the users it was built for. A sound and a font
carry no such trap, because nobody ships a *default* wav or `Font_Override` — there, the file's
presence is itself the intent. Two further reasons hold: a position is not additive the way a sound, a
duration or a font is, and the recent panel is app-owned by construction, so an unlock shown in one
corner would be reviewed in another; and GBE measures against the game's render surface where this app
measures against the display.

**Why colour goes the same way, less comfortably.** Readability is *not* the reason — `PopupPalette`
derives every foreground from whatever fill it is given, so an unreadable result is unreachable
whoever picks the colour, and `PopupBackground.MinAlpha` already blocks an invisible one. The reasons
are that GBE's notification has one configurable colour under hardcoded white text and a single
`title + "\n" + description` string, so one key would drive six colours here — two of which, the game
line and the panel's dismiss hint, have no counterpart over there; that `Notification_A` is the fill's
alpha *and* `settings_noti_alpha` for the border and text, so no single mapping is correct; and that a
per-game colour overrides an app-wide choice the same way a position does. The verified detail behind
each is in `docs/pages/development/gbe-reference.md`.

**The counter-arguments, recorded because they were not rebutted.** The single config carrying colour
is a repack theme whose `Font_Override=poppins.ttf` and 153 KB unlock wav this app *does* honour, from
the same four lines of the same file, and whose `Notification_R/G/B/A` are a real preference rather
than a default. The line held is that a sound and a font are assets the game ships, while a position
and a colour are presentation choices the user now owns app-wide — not that the author had less of an
opinion. Separately, the strongest point for per-game position is real and unclosed: the corner a
popup must avoid is a property of the game's HUD, not of the user's taste. Its answer is a per-game
override in this app's own UI, where it would be a deliberate choice at both levels, rather than
reading an ini that says `bot_right` by accident. Not built; kept as a memo.

The `GameOverlayConfig` rule stands: a value nothing reads is a value nothing keeps correct. The class
comment needs narrowing all the same — "considered and rejected — position, colours …" must become
"rejected *as per-game overrides*", or it reads as a claim this change has just contradicted.

## Deliberately not built

- **A separate text-colour setting.** The ink is derived so that an unreadable pair is unreachable.
- **A border or drop shadow on the popup.** It would break `RenderedWidth == DesignWidth × scale`, the
  invariant both the placement maths and the settings footer read, and which the earlier plan already
  refused to break for `Icon_Size`. A fill close to the game's own frame blending at the edge is a
  README limitation, as it is today.
- **A free-text hex field**, **a separate opacity key**, **free-form or drag-to-place positions**, and
  **horizontal slide-in for the corner anchors** — each covered above.
- **Following the Windows theme or accent.** Ruled out by name in `CLAUDE.md`, and the reason holds.
- **`AppConfig` convenience getters for the two new keys.** The existing `Font` and `Scale` getters have
  no callers — `NotificationAppearance` reads `SettingsData` directly — so two more would be the third
  and fourth pieces of dead code.
- **Adding the corner to the settings footer readout.** The footer exists to state the width *after* the
  readability clamp, so a value the popup would refuse is visible rather than discovered at the next
  unlock. The corner is not computed and not clamped, and its control is three centimetres away.

## Known limits, and follow-ups this change does not take

- Placement is measured against the display's work area, never the game's window. Most noticeable
  windowed, at a centre anchor.
- Contrast is computed against the chosen colour ignoring its alpha; what shows through is a game
  frame nobody can predict.
- Near luminance 0.179129 the game line cannot reach WCAG AA whatever the app does. Nor does it on the
  shipped default.
- The unlock popup and the recent panel's footer disagree by one slide distance. Preserved here on
  purpose; worth its own commit.
- `NotificationScaleConverter.Read` throws on an unexpected token, so `"scale": true` in a hand-edited
  config stops the app from starting, against that file's own stated policy. A separate defect; the two
  new converters do not share it.
