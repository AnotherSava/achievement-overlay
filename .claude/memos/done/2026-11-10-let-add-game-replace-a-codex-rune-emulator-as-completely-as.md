---
created: 2026-11-10 20:38:00
---

# Let Add game replace a CODEX/RUNE emulator as completely as a hand install

Switching a RUNE game to GBE by hand on 2026-11-10 needed three things the wizard's 'Replace it with GBE' path does not do.

1. Read interfaces from the original Valve DLL when the crack kept it. RUNE ships the original renamed as steam_api64.rne (valid Valve signature, version 8.33.09.23 there) beside its own steam_api64.dll (Valve version info, signature HashMismatch). generate_interfaces on the .rne gave 30 interfaces; the steam_emu.ini [Interfaces] section the wizard uses gave 27, missing SteamNetworkingMessages002, SteamNetworkingSockets012 and SteamNetworkingUtils004. Prefer the original DLL when a validly signed one is present (.rne, and check what CODEX keeps), and keep the ini as the fallback. SceneEmulatorDetector and InterfaceGenerator are where this lives.

2. Keep the game's saves reachable. Many games file saves under the Steam ID (the test game keeps them in a Documents folder named after it). GBE picks its own ID, so after the switch the game finds no saves. Writing account_steamid into steam_settings/configs.user.ini fixes it. The RUNE ID here was 76561197960271872 with AccountId commented out in steam_emu.ini, so it looks like RUNE's fixed default when unset; confirm on another RUNE and a CODEX game, and read AccountId when it is set. Offer it as a choice (keep saves vs fresh start), since a fresh start is also a reasonable wish.

3. Offer GBE's debug build (emu-win-debug-vs26.7z, same release tag). It writes STEAM_LOG.txt beside the DLL with every Steam call, which is how a game that records achievements wrongly gets diagnosed. Not as the default without measuring first: it logs every call to disk and the DLL is about 23 MB, so check frame-time impact and how fast the log grows on a game that polls Steam every frame. A checkbox on the ready page, or a 'diagnose' action that swaps the DLL and can swap it back, are the candidates.
