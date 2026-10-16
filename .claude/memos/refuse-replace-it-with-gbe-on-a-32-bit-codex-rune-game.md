---
created: 2026-10-15 20:19:38
---

# Refuse Replace it with GBE on a 32-bit CODEX/RUNE game before the run starts

Review finding (dropped as rare, 2026-10-15). The folder-page check in AddGameForm.DetectAndAdvanceAsync skips InstallTarget.InstallRefusal when a CODEX/RUNE emulator is detected, because keeping it installs nothing. Ticking Replace it with GBE on the Ready page for a 32-bit steam_api.dll is refused only when GbeConfigGenerator runs, after the AppID, API key and schema steps. Disable or explain the checkbox when the target is 32-bit. steam-dll-detection.md's wizard section says the folder page refuses it, which is true only when nothing is kept.
