---
created: 2026-09-27 19:38:29
---

# Stop the wizard searching the store with an empty name for a game at a drive root

When the Add game wizard finds no local AppID, it searches the Steam store by the game's folder name. That name comes from `Path.GetFileName(gameDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))`, in two places: AddGameForm (the folder page's detection) and GbeConfigGenerator.ResolveAppIdAsync (the run). For a game folder that is itself a drive root, `D:\` trims to `D:`, whose file name is the empty string. AppIdResolver.FromStoreSearchAsync has no guard, so it requests `https://store.steampowered.com/search/?term=` and takes the first result of an empty search as the "guessed" AppID. The wizard then offers an unrelated game's AppID for confirmation, and the generator's log says it was guessed from a store search for ''.

Realism: rare. It needs a game installed directly at a drive root with no steam_appid.txt and no AppID in its ini files. The guess is shown for confirmation rather than used silently.

Next step: derive the name once, from the folder's last name, and skip the store search when there is none, asking for the AppID instead (the path taken when the search finds nothing). The two copies of the expression are a duplicate to fold into that one place. FolderPath's names give the last name directly, but its FirstNameBelow falls back to the Root for a drive root, which is the wrong answer here.
