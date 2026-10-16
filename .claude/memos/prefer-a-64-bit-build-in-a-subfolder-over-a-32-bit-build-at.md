---
created: 2026-10-15 20:19:38
---

# Prefer a 64-bit build in a subfolder over a 32-bit build at the root in SteamDllDetector

Review finding (dropped as plausible but unreached, 2026-10-15). PickShallowest ranks by folder depth before bitness, so a game shipping game.exe + steam_api.dll at the root and x64\game.exe + steam_api64.dll below resolves to the 32-bit DLL under rule 3, and InstallTarget.InstallRefusal then refuses it on the folder page. The removed DllLocator sorted 64-bit first and picked x64\. Older games that added a 64-bit build later (reportedly Grim Dawn, Titan Quest AE) may use this layout; unconfirmed. Workaround today: pick the x64 folder in the wizard. A fix must keep the _crack rule (a crack copy one level down loses to the root copy) — e.g. prefer a 64-bit candidate when the shallower folder has only 32-bit, and update docs/pages/development/steam-dll-detection.md plus a fixture in SteamDllDetectorTests.
