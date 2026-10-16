---
created: 2026-10-15 20:19:39
---

# Update the Add game folder page's help text and refusal suffix for Steam DLL detection

From the docs-relevance polish notes (2026-10-15). AddGameForm's folder-page help still says it searches for steam_api64.dll or steam_api.dll and writes the config beside that file; it now picks the DLL the game loads, which can be a steamclient behind ColdClientLoader. The folder-page refusal appends 'This wizard configures Steam games only — other emulators are tracked automatically…' to every InstallTarget refusal, which reads oddly after the 32-bit and non-GBE steamclient refusals. UI strings, so they need a look at the window.
