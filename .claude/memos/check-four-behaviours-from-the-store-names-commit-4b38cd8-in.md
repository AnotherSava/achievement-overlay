---
created: 2026-10-15 09:34:38
---

# Check four behaviours from the store-names commit (4b38cd8) in the running app

None of these has been observed in the running app; each was verified only by reading code and by the unit tests.

1. Report a problem's empty state. With no configured game listed (gamesPaths pointing at an empty folder, and Include unconfigured games unticked), every part's pane should hold a hint (games hidden behind the box, or none found), Copy and Save should be disabled, and the window should not throw. Tick the box with only unconfigured save folders present and confirm they appear after a separator, each with a leading *.
2. The Include switch and its label (IncludeToggle, IncludeLabel) should be hidden while no game is selected, in that same empty state, and come back once a game is selected.
3. Wizard-time naming. Add a game whose folder name differs from its Steam store name and whose appid is not yet in names.json (delete names.json first, or use a game never looked up). The tracking-configured popup that registration can raise should name it by its store name, never by the folder name, because AddGameForm awaits SteamAppNames.FetchAsync before calling _onGameConfigured.
4. Wizard placement. Open Add game with the pointer on a secondary monitor: the dialog should open on that monitor, about a third of the way down. Step through pages whose height differs (folder, API key, Ready, progress): the top-left corner should stay put, and the dialog should move up only when its bottom would leave the screen.

Close this memo once all four are seen; anything that misbehaves becomes its own fix.
