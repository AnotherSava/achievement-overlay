---
created: 2026-09-27 15:24:46
---

# Keep three Add game log lines out of other games' diagnostic reports

DiagnosticReport.KeepLinesForGame drops a log line about another game only when it names that game's appid in the `appid N` / `AppID: N` form (the AppIdReference regex) or holds a path outside this game's folders. The Add game wizard writes every progress line to overlay.log as `[add-game] <level>: <message>` (AddGameForm.Report), and three of those lines identify a game with neither marker, so every game's report keeps them:

- `Searching the Steam store for '<game name>'...` — GbeConfigGenerator's store-search fallback (Debug, logged at Info). It names the game by its folder name. The appid is not known yet at this point, which is why the search runs, so the `appid N` tag other lines use is not available here.
- `Hidden achievements are left with placeholder descriptions. To fill them in by hand, see https://steamdb.info/app/<appid>/stats/` — Warn, when the SteamDB scrape fails. The appid sits inside a URL, which the regex does not read.
- `SteamDB had no description for N hidden achievement(s): <names>` — Warn. It lists that game's achievement API names, with no appid.

`AppID N guessed from store search for '<name>'` is already safe, because `AppID N` matches the regex. Possibly a fourth: AddGameForm's `Could not open '<url>'`, when a link in the wizard log fails to open and the URL is the SteamDB stats page.

Why it matters: troubleshooting.md promises that reporting one game does not publish your library, and these lines put another game's name, appid or achievement names into a report the user may attach to a public issue.

Options, for the owner to choose:
(a) Write `appid N` into the two lines that run after the appid is resolved (the SteamDB warning and the missing-descriptions warning), the convention NotificationQueue's lines follow.
(b) For the store-search line, drop the game name, or name the game folder so the path check drops it from other games' reports. Naming the folder also drops it from its own game's report: a report's own folders are the steam_settings folders, the GSE Saves folders and the app's folder, not the game's root.
(c) Teach AppIdReference to read `steamdb.info/app/N` and `store.steampowered.com/app/N`, which covers any URL-bearing line at the filter rather than one line at a time.

Whichever is chosen, add a KeepLinesForGame test per line that feeds the real message text, as the NotificationQueue tests do.
