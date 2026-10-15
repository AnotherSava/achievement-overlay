---
created: 2026-10-14 19:28:06
---

# Render config/local.json from Doppler at deploy time instead of keeping plaintext API keys

`config/local.json` holds the Steam Web API key and the Firecrawl key in plaintext, and `scripts/deploy.sh` copies it over the installed `config.json`. Other projects render that file at deploy time from Doppler instead, so no plaintext key sits on disk.

Why this repo doesn't: the deploy skill's Doppler block (`doppler secrets substitute` of `config/local.template.json` into `config/local.json`, wiped on exit) is generated only when its target is `deploy-tauri.sh`. This project deploys through the generic `deploy.sh`, so it got the pass-through wrapper.

Findings, 2026-10-14 (this repo is public, so the Doppler coordinates are not written here; they are in the encrypted `refs-private` memory, and the `/doppler` skill gives the shard rule):
- Claude's ad-hoc credential store already holds the Firecrawl key, and it is the same key the installed config has.
- The Steam Web API key is in no Doppler config.
- The app has no Doppler home yet; a workstation-only app takes a config in the workstation shard.

Proposed:
1. Create the app's config in that shard. Pipe the Steam key in from `config/local.json`; make the Firecrawl key a secret reference to the ad-hoc store's copy. The doppler skill says never to copy an ad-hoc credential into an app config; a reference copies nothing but couples the app to that store, so confirm that choice first.
2. Commit `config/local.template.json` (today's `local.json` settings, the two keys as `{{tojson .STEAM_WEB_API_KEY}}` / `{{tojson .FIRECRAWL_API_KEY}}`) and add `DOPPLER_PROJECT` / `DOPPLER_CONFIG` to `config/deploy.env`, which is per-machine and gitignored. Delete the plaintext `local.json`.
3. Generalise the global deploy skill so the render block also applies to the `deploy.sh` target. That edit is in the dotfiles repo: report it to that repo's session, which commits it.
4. Update the "Config files" section of `CLAUDE.md`, which today says to fill keys into `config/local.json`.
