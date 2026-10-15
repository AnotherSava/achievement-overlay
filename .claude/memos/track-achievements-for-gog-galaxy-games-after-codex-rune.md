---
created: 2026-10-15 14:47:18
---

# Track achievements for GOG Galaxy games, after CODEX/RUNE support is done

GOG builds report unlocks through the Galaxy SDK, which writes no file the watcher can see. Pick this up once CODEX/RUNE (issue #11) is finished.

The emulator route, the half-run test on a local GOG install (its game folder is modified right now — read this before touching it) and the key-matching evidence are in the encrypted project memory gog-galaxy-emulator-test (.claude/memory/gog-galaxy-emulator-test.secret.md).

Public findings for the legitimate routes:

- The GOG Galaxy client keeps a SQLite store at C:\ProgramData\GOG.com\Galaxy\storage\galaxy-2.0.db with tables Achievements (icon URLs, visibility), LocalizedAchievements (name/description per language) and UserAchievements (unlockTime, isUnlocked), keyed by gameReleaseKey (gog_<gameId>). Unverified: whether a row changes the moment an achievement unlocks or only after a server sync, and whether the WAL makes file-change events unreliable.
- Comet (imLinguin/comet, Apache-2.0), the open-source Galaxy communication service Heroic uses, stores each game's state at %LOCALAPPDATA%\comet\gameplay\<client_id>\<user_id>\gameplay.db; its achievement table carries key, name, description, unlock_time and icon URLs, so an unlock is self-describing. It needs a GOG account that owns the game.
- A GOG install's goggame-<gameId>.info carries its clientId, which is the Comet path component.
