---
created: 2026-10-14 21:19:52
---

# Keep the tracking-configured record when a deploy copies local.json over the installed config

Each local deploy (`bash scripts/deploy.sh`) copies `config/local.json` over the installed `config.json`. That also replaces the app-managed `trackingConfigured` map, so the next start shows the synthetic "tracking configured" popup again for every game that had already had it (seen 2026-10-14 on every deploy that day).

The map is app state, not a setting: the user never edits it, and `SettingsDiff` deliberately never compares it. Options worth weighing: keep the installed `trackingConfigured` when the deploy copies `local.json` (a merge in the deploy wrapper, which is the global deploy skill's file, so that part routes to the dotfiles session), or move the map out of `config.json` into its own state file next to it, which `AppConfig` and `TryNotifyTrackingConfigured` would read and write instead. The second keeps the fix inside this repo. This only affects developer deploys; a user unzipping a release over an install may hit the same thing, which is worth checking against how the release zip ships `config.json`.
