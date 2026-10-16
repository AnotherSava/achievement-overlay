---
created: 2026-10-15 20:19:38
---

# Run SteamDllDetector on the game's own folder, not a shared folder above several games

Review finding r2-4 (dropped as rare, 2026-10-15). GameCache.FindGames calls SteamDllDetector.Detect(entry.Key.GameFolder). With a games root such as D:\ and a game at D:\Games\A, GameFolder is D:\Games, so detection walks every other game's Steam DLLs and loader inis, can tie or pick another game's DLL, and logs a Warn listing them. A narrower starting folder (the common ancestor of the game's own steam_settings folders) would keep it to one install.
