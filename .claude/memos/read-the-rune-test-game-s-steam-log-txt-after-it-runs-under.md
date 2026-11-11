---
created: 2026-11-11 05:44:47
---

# Read the RUNE test game's STEAM_LOG.txt after it runs under GBE's debug build

The RUNE game that loses its story achievements (named in the encrypted .claude/memory/codex-rune-local-test-bed.secret.md, with the procedure) was switched by hand to GBE's debug build on 2026-11-10 to log its SetAchievement calls. It has not been launched since. Next: launch it, play to a story achievement or just load a save, quit, then read STEAM_LOG.txt beside its steam_api64.dll and check whether the game asks for one-off achievements by an index the RUNE build answered with an empty name. Afterwards swap in GBE's release DLL, since the debug log grows while the game runs.
