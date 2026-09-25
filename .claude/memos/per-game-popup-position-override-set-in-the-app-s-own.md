---
created: 2026-08-30 21:12
---

# Per-game popup position override, set in the app's own settings

Per-game **popup position** override, set in this app's own UI rather than read from a game's ini. The gap it closes: the corner a popup must avoid is a property of the game's HUD (a visual novel owns the bottom third, a shooter the top-right), not of the user's taste, so one app-wide anchor cannot be right for every game. Reading `PosAchievement` was rejected as the answer — all three configs that state one say `bot_right`, which is GBE's own default, so the key expresses no preference and would only ever override a deliberate choice with a non-choice (surveyed 2026-08-30; see docs/plans/completed/2026-08-30-popup-position-and-background.md). A per-game entry in Settings would be deliberate at both levels. Note the recent achievements panel is app-owned by construction and would have to keep one anchor for the whole stack.
