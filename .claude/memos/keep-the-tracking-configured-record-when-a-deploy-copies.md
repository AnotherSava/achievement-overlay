---
created: 2026-10-14 21:19:52
---

# Keep the tracking-configured record when a deploy copies local.json over the installed config

Each local deploy (`bash scripts/deploy.sh`) copies `config/local.json` over the installed `config.json`. That also replaces the app-managed `trackingConfigured` map, so the next start shows the synthetic "tracking configured" popup again for every game that had already had it (seen 2026-10-14 on every deploy that day).

The map is app state, not a setting: the user never edits it, and `SettingsDiff` deliberately never compares it. Options worth weighing: keep the installed `trackingConfigured` when the deploy copies `local.json` (a merge in the deploy wrapper, which is the global deploy skill's file, so that part routes to the dotfiles session), or move the map out of `config.json` into its own state file next to it, which `AppConfig` and `TryNotifyTrackingConfigured` would read and write instead. The second keeps the fix inside this repo. This only affects developer deploys; a user unzipping a release over an install may hit the same thing, which is worth checking against how the release zip ships `config.json`.

`lastGamesRoot` (added 2026-11-01, where the Add game folder picker opens when several `gamesPaths` roots exist) is app-managed state in the same file and is wiped by each deploy the same way, so whichever fix is chosen should cover it too. A separate state file would hold both.

Seen again 2026-11-10: a deploy re-fired the popup for two games at once. The Games window's `hiddenGamesColumns` (added that day) is a third app-managed key in the same file, wiped the same way. A state file beside `config.json` would also need protecting from the deploy, which deletes the install folder's top-level files (`names.json` among them). Until this is fixed, the developer machine carries a hand-made copy of `trackingConfigured` in the gitignored `config/local.json`, which goes stale whenever a game is added.
