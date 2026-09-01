---
layout: default
title: GBE reference
parent: Development
nav_order: 1
---

# GBE reference

Background on how the Goldberg Steam Emulator stores achievements, for anyone configuring a game
by hand or working out why the overlay stays quiet. The [Add game…](../usage/adding-a-game)
wizard handles all of this for you — nothing here is required reading for normal use.

## Where unlocks are stored

The emulator writes one folder per game, named after the Steam AppID:

| Emulator | Path |
|---|---|
| GBE (current) | `%appdata%\GSE Saves\<app_id>\` |
| Goldberg (older builds, and some cracks) | `%appdata%\Goldberg SteamEmu Saves\<app_id>\` |

The overlay watches `%appdata%\GSE Saves` by default. If a game is unlocking achievements but nothing
appears, check whether it is writing to the older Goldberg path instead, and either point
`gseSavesPaths` at that folder too or move the game onto a current GBE build.

A folder named `4294967295` means the emulator never got a valid AppID — `steam_appid.txt` is missing
or malformed. Fix the AppID and the next unlock writes to the right folder.

The `achievements.json` inside these folders is the *unlock* file: which achievements are earned and
when. It is created on the first unlock or stat write, **not** when the game launches, so its absence
before your first achievement means nothing is wrong.

## How unlocks get recorded

The emulator records an achievement only when the game calls Steam's `SetAchievement()` — at the
moment it is earned. There is no retroactive detection, so achievements you earned before setting the
game up will not appear, and neither will progress held in the game's own save files. The counter
starts from the moment the emulator is in place.

The game also needs its achievement schema (`steam_settings/achievements.json`, described below)
present for tracking to work at all.

## The two GBE builds

A GBE release ships the Steam API replacement in two variants:

- **Regular** (~7 MB) — no overlay code at all, and it ignores `configs.overlay.ini` entirely
- **Experimental** (~22 MB) — includes GBE's own in-game overlay and achievement popups

This app is built to replace the experimental overlay, so the wizard installs the **regular** build
and sets `enable_experimental_overlay=0`. That is not only a preference: some games treat any
rendering hook as tampering. Rockstar/Double Eleven titles such as Red Dead Redemption fail with
**error 25D11007** ("closed unexpectedly, possibly due to a third party overlay tool") when the
experimental build is present — regardless of the overlay setting, because it is the hook itself that
is detected. Achievement tracking is unaffected; only in-game popups are lost, which is exactly the
gap this app fills.

For the same reason, a GBE install should not include `steamclient64.dll`, `steamclient.dll`, or
`GameOverlayRenderer64.dll` unless a specific game needs them.

## The overlay config file

GBE reads its own overlay settings from four files inside a game's `steam_settings/` folder —
`configs.app.ini`, `configs.main.ini`, `configs.overlay.ini` and `configs.user.ini`. They share one
key space: the **section** decides what a key means, not the file it sits in, and when more than one
file defines a key the **first file in that order wins**. Unset keys are then filled in from
`%appdata%\GSE Saves\settings\`, so a game's own folder beats the global one.

These files are not written by the emulator. GBE only ever writes back `configs.user.ini` (account
name, Steam ID, language, country). Everything else arrives from somewhere else:

- a repack or scene release that bundled a ready-made `steam_settings/` — by far the most common
- an older config generator
- `steam_settings.EXAMPLE/configs.overlay.EXAMPLE.ini` from a GBE release, renamed and edited by hand
- the [Add game…](../usage/adding-a-game) wizard, which writes only
  `[overlay::general] enable_experimental_overlay=0`

This matters for [Per-game settings](../usage/per-game-settings): the wizard installs the
**regular** GBE build, which has no overlay code and ignores this file entirely. So when the app reads
it, it is not mirroring anything the user sees in-game — it is reading a standard place where someone
has already written down what they wanted.

### Keys the overlay app reads

Both live in `[overlay::appearance]`:

| Key | Meaning | GBE default |
|---|---|---|
| `Notification_Duration_Achievement` | Seconds an unlock notification stays up | `7.0` |
| `Font_Override` | TrueType file; a relative name resolves inside `steam_settings/fonts` | (none) |

Alongside them, `steam_settings/sounds/overlay_achievement_notification.wav` is played on unlock.

### What GBE's own notification actually looks like

Worth knowing before reaching for any of its appearance keys, because the two notifications are less
alike than the key names suggest. Verified against `gbe_fork` source rather than against its docs.

- **Position.** One key per notification type: `PosAchievement` (`settings_parser.cpp:390`), plus
  `PosInvitation` and `PosChatMsg` for the other two. All go through
  `translate_notification_position` (`:125-136`), which takes six **case-sensitive** literals —
  `top_left`, `top_center`, `top_right`, `bot_left`, `bot_center`, `bot_right`. Anything else logs
  *Invalid position* and returns `default_pos = top_right` (`settings.h:128`), which is **not** the
  field's own initial value `ach_earned_pos = bot_right` (`settings.h:185`) — so a typo lands the
  notification top-right, somewhere an unset file would never put it.
  `Notification_Margin_x` / `_y` offset from the anchor, defaulting to 5.0.
- **Colour.** There are 28 colour keys — seven colours × four float channels, where a negative
  channel means "unset" — and **only four of them reach the notification**: `Notification_R/G/B/A`
  (`settings_parser.cpp:275-290`), which set one thing, the window fill, via
  `get_notification_bg_rgba_safe` pushed as `ImGuiCol_WindowBg` (`steam_overlay.cpp:1047-1069, :1101`).
  The other 24 belong elsewhere: `Background_*` and the three `Element*_*` sets colour the shift+tab
  main window through `apply_global_style_color`, called only from `render_main_window`;
  `Stats_Background_*` and `Stats_Text_*` colour the FPS window.
- **The notification's text colour is not configurable at all.** Line `:1102` pushes
  `ImGuiCol_Text = ImVec4(255, 255, 255, settings_noti_alpha * 2)` unconditionally. `Stats_Text_*`, on
  the FPS counter, is the only text colour GBE exposes anywhere.
- **`Notification_A` does double duty**: the fill's alpha, and `settings_noti_alpha` (`:1096`), which
  drives the border (`:1100`) and that hardcoded text (`:1102`).
- **The body is one string.** `ach.title + "\n" + ach.description` (`:1275`), drawn by a single
  `TextWrapped`. GBE has no title/description distinction and no game-name line.

### Keys the overlay app deliberately does not read

Neither `PosAchievement` nor `Notification_R/G/B/A` is read, even though the app now has settings of
its own for both. Set those in [Settings → Notifications](../usage/settings) instead.

For position, the survey is what decides it. Across the ten installs on the development machine,
`PosAchievement` appears in three `configs.overlay.ini` files and every one says `bot_right` — which
is at once GBE's own default for an earned achievement, the value in the shipped
`configs.overlay.EXAMPLE.ini`, and this app's default. The key's presence therefore expresses no
preference, so honouring it changes nothing today and does the wrong thing the moment someone uses the
app's setting: those three games would stay bottom-right while every other game moved. A sound and a
font have no such trap, because nobody ships a *default* wav or `Font_Override` — the file's presence
is itself the intent. Two lesser reasons hold as well: a position is not additive the way a sound, a
duration or a font is, and the recent achievements panel is app-owned by construction, so an unlock
shown in one corner would be reviewed in another; and GBE positions against `io.DisplaySize`, the
game's render surface, where this app positions against the display's work area, so the same key would
not mean the same thing.

For colour the case is closer, and the argument against is narrower than it looks. Readability is
**not** the reason — `PopupPalette` derives every foreground from whatever fill it is handed, so an
unreadable result is unreachable whoever chooses the colour. The reasons are that one GBE key would
drive six colours here, two of which (the game line and the recent panel's dismiss hint) have no
counterpart over there, which is reinterpretation rather than mirroring — the same objection that
dropped `Font_Size`; that `Notification_A` means two things over there and would mean a third here;
and that, like position, a per-game colour silently overrides an app-wide choice the user just made.

The honest counter-argument, recorded because it has not been rebutted: the one config on the
development machine that carries colour is a repack theme whose `Font_Override` and unlock wav this
app **does** honour, from the same four lines of the same file. The line held is that a sound and a
font are assets the game ships, while a position and a colour are presentation choices the user owns
app-wide — not that the author had less of an opinion.

### Parsing quirks

GBE uses SimpleIni in a narrow configuration, and a file written for it behaves accordingly:

- Comments are **whole lines only**, starting with `;` or `#`. There are no trailing comments, so
  `Font_Size=16 # big` has the literal value `16 # big`.
- Quotes are not stripped: `Font_Override="a.ttf"` looks for a file whose name includes the quotes.
- A repeated key inside one file means the last one wins; an empty value reads as absent.
- Numbers are read with `std::stof`, which takes the leading numeric prefix — `7.0s` is 7.0 — and a
  value it cannot read at all costs that one key rather than the file.
- `[overlay::appearance]` keys are **case-sensitive** in GBE: `font_size=22` is silently discarded.
  (This app reads them case-insensitively instead, on the grounds that for a wizard-configured game
  nothing else reads the file anyway.)

## The achievement schema format

Each configured game has a `steam_settings/achievements.json` next to its `steam_api64.dll`. This is
the *schema* — the display text and icons for every achievement in the game, unlocked or not. It is a
JSON array:

```json
[
  {
    "name": "Achievement_0",
    "displayName": "Tower Tussle",
    "description": "Complete the tower challenge",
    "hidden": "0",
    "icon": "images/Achievement_0.jpg",
    "icongray": "images/Achievement_0_gray.jpg",
    "progress": {
      "value": { "operation": ">=", "operand1": "stat_name" },
      "min_val": "0",
      "max_val": "100"
    }
  }
]
```

| Field | Meaning |
|---|---|
| `name` | The internal Steam API name. Required — an entry without it is discarded, and it is the key the unlock file matches on |
| `displayName`, `description` | Either a string or a per-language object, e.g. `{"english": "…", "german": "…"}` |
| `hidden` | `"0"` for a normal achievement, `"1"` for a secret one |
| `icon`, `icongray` | Unlocked and locked icon paths, relative to `steam_settings/` |
| `progress` | Optional, for stat-based achievements: `operand1` names the stat, `operation` is the comparison, and `min_val`/`max_val` bound the progress bar |

Icon paths differ by the tool that produced the config, and both work because the emulator simply
reads whatever path is written:

- The **Add game… wizard** and `generate_emu_config` write `img/` with `<name>_1.png` (unlocked) and `<name>_0.png` (locked)
- Some other generators write `images/` with `<name>.jpg` and `<name>_gray.jpg`

If notifications show the default icon rather than the real one, the paths in the JSON and the files
on disk have drifted apart — see [Troubleshooting](../troubleshooting#the-notification-shows-the-default-icon).

## Why hidden descriptions come back blank

Steam's public `GetSchemaForGame` API redacts the `description` field for any achievement marked
`hidden=1`. Every tool that builds a config from that API — the wizard, `generate_emu_config`, and
configs shipped by third parties — inherits the gap and leaves "No description available" behind. A
config being complete by file count says nothing; scan the JSON for placeholders.

The text is not hiding in the game's files either. For Unreal Engine 5 titles the achievement assets
are metadata stubs holding an icon path, a hidden flag and an ID; the strings are fetched from Steam
at runtime through `GetAchievementDisplayAttribute`. Extracting the game's packages finds nothing.

Two places do have the real text:

- **SteamDB** — `https://steamdb.info/app/<app_id>/stats/` lists hidden descriptions inline. This is
  what the wizard reads through [Firecrawl](https://firecrawl.dev); the site sits behind Cloudflare,
  so a plain HTTP request gets a 403. Each achievement appears as its display name, then
  `Hidden achievement: <description>`, then its completion percentage, then the API name — match rows
  back to the JSON on that API name, never on display order.
- **A legitimate local Steam install** — `Steam/appcache/stats/UserGameStatsSchema_<app_id>.bin` is a
  binary KeyValues blob Steam writes after the real client pulls a game's schema, and it contains
  every string including the hidden ones.

Community achievement guides paraphrase rather than quote, so they are a last resort if you want the
text to match what Steam would have shown.
