# Playnite and SuccessStory

If you launch your games through [Playnite](https://playnite.link) and track completion with the
[SuccessStory](https://github.com/Lacro59/playnite-successstory-plugin) plugin, an emulator-tracked
game will not show its achievements automatically. This page covers the one-time mapping that fixes
it. Nothing here affects the overlay itself — notifications work either way.

## Why it does not just work

For a game imported from Steam, SuccessStory queries Steam using the game's own `GameId`. A game added
to Playnite any other way — a manual entry, a folder picked up by the filesystem scanner — has no
Steam AppID attached, so there is nothing to query. SuccessStory's "Local" source does not fill the
gap either: it is enabled by default but its scan path list is empty, and it does not look inside
`GSE Saves` on its own. The result is a game whose achievement list stays empty even though the
overlay is happily showing unlocks for it.

The fix is to tell SuccessStory which Steam AppID the game corresponds to, through the plugin's
`ForcedSteamAppIds` setting.

## Wiring it up

The plugin's data lives under
`%appdata%\Playnite\ExtensionsData\cebe6d32-8c46-4459-b993-5a5189d60788\`.

**1. Find the game's Playnite GUID.** It is the filename of the game's data file under `SuccessStory\`:

```
%appdata%\Playnite\ExtensionsData\cebe6d32-8c46-4459-b993-5a5189d60788\SuccessStory\<guid>.json
```

Match it by the `Name` field inside. If no file exists for the game yet, SuccessStory has never
touched it — open Playnite and select the game once, which creates the file.

**2. Close Playnite.** The plugin loads its config at startup and writes it back on shutdown, so an
edit made while Playnite is running is silently overwritten.

**3. Add the mapping** to `config.json` in the folder above:

```json
"ForcedSteamAppIds": {
  "<playnite-game-guid>": <steam-app-id>
}
```

**4. Reopen Playnite**, then right-click the game → SuccessStory → Refresh achievements.

**5. Check it worked.** The game's `<guid>.json` should now have a populated `Items` list, and its
`SourcesLink.Url` should point at `https://steamcommunity.com/stats/<app_id>/achievements`.

## Where the data comes from

With the mapping in place, SuccessStory assembles the game from two sources:

- **Achievement metadata** — name, description, icons and global completion percentage are scraped
  anonymously from `https://steamcommunity.com/stats/<app_id>/achievements`. No API key or login.
- **Unlock state** — merged in from `%appdata%\GSE Saves\<app_id>\achievements.json`, the same file
  this overlay watches. Unlock dates line up with the `earned_time` timestamps written there.

That second half is why the mapping alone is enough: SuccessStory already reads GSE Saves once it
knows which AppID a game is.

## Fields in the per-game file

Each `<guid>.json` under `SuccessStory\` holds:

| Field | Meaning |
|---|---|
| `Items` | The achievements themselves — name, API name, description, locked/unlocked icon URLs, unlock date, hidden flag, global percentage |
| `IsEmulators` | Marks the game as emulator-tracked. Informational; it does not change how the source is resolved |
| `IsManual` | True when the achievements were typed in by hand through the SuccessStory UI |
| `SourcesLink` | Where the achievements were last fetched from |
| `DateLastRefresh` | Timestamp of the last successful fetch |

## Other settings worth knowing

- **`SteamApiSettings.UseApi`** enabled with `UseAuth` disabled is the default, and is all a forced
  AppID needs — the community-stats scrape is anonymous, so no Steam Web API key is involved.
- **`EnableAchievementWatcher`** wires in xan105's separate Achievement Watcher tool. Not needed when
  the forced-AppID mapping is doing the job.
- **`IncludeHiddenGames`** is off by default, so a game flagged Hidden in Playnite is skipped by
  SuccessStory entirely.
