---
created: 2026-10-15 20:19:39
---

# Cover the untested branches of Steam DLL detection

Review findings (dropped as theoretical/rare, 2026-10-15): no fixture reaches IsEmulatorDll's company-name check alone (every emulator fixture has the GBE marker or steam_emu.ini); no test covers GbeConfigGenerator's keep gating (KeepGbe/KeepEmulator skipping DRM, download and DLL replacement, and exempt from InstallRefusal) — a wrong keep would copy GBE's steam_api64 over a repack's GBE steamclient64; no test covers GameCache's fallback when the detected DLL has no collected steam_settings beside it, or either LogSteamDll Warn line.
