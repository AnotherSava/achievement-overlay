---
created: 2026-10-15 20:19:39
---

# Share one steam_api name test between InstallTarget and SteamDllDetector

Clean-code polish notes (2026-10-15). InstallTarget.Choose tells a steam_api from a steamclient with name.StartsWith("steam_api") while SteamDllDetector has a private exact-match IsSteamApi; expose one. GameCache.LogSteamDll re-derives whether SettingsDirs[0] sits beside the detected DLL, which the ordering in FindGames already decided; recording the outcome on GameInfo would remove the second comparison.
