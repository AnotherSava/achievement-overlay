---
layout: default
title: Which Steam DLL a game loads
parent: Development
nav_order: 3
---

# Which Steam DLL a game loads

A game often carries more than one `steam_settings` folder. A repack ships one at the game root and
another beside the DLL its emulator runs from, a config generator adds a third, and the copies rarely
hold the same text. The emulator reads exactly one of them: GBE takes the folder beside its own DLL,
the file `GetModuleFileNameW` names. The app has to read the same folder, so that a popup describes an
unlock from the schema that defined it, and the Add game wizard has to write there, or the emulator
never sees what it wrote.

Nothing records which DLL a game loads. `SteamDllDetector` works it out from the files in the game's
folder, applying the rules below in order; the first rule that names a DLL decides. Both callers use
it:

- **`GameCache`** runs it for every game with more than one `steam_settings` folder, and reads the
  schema from the folder beside the DLL it names. A game with one folder has nothing to decide.
- **The Add game wizard** runs it on the folder the user picks, then writes `steam_settings` beside
  the DLL it names, replacing that DLL with GBE when the game needs it installed.

## The files the rules look at

The rules work from three kinds of file:

- **Steam DLLs**: `steam_api.dll`, `steam_api64.dll`, `steamclient.dll` and `steamclient64.dll`,
  anywhere under the game folder.
- **An emulator DLL** is a Steam DLL that is not Valve's: it carries a GBE marker, its version info
  names a company other than Valve or none at all, or a `steam_emu.ini` sits beside it. The last test
  is for the CODEX and RUNE emulators, which keep Valve's `steam_api64.dll` in place, version info
  included, and load themselves through it.
- **An importer** is an `.exe`, `.dll` or `.node` file whose import table, delay-load imports
  included, names `steam_api.dll` or `steam_api64.dll`. Windows looks for an executable's imports in
  the executable's own folder first, and Node loads a `.node` addon so that its imports are found
  beside it. A `.dll` gets its own folder searched only when it was loaded by full path, which is how
  engines and plugin hosts load theirs. So a Steam API DLL beside its importer is the one that loads.

A `steam_api` **loads** when an importer sits beside it, or when it sits where a game engine loads it
from by explicit path, which leaves nothing beside the DLL importing it:

- **Unreal Engine** keeps its executable in `<Project>\Binaries\Win64` and loads the DLL from
  `Engine\Binaries\ThirdParty\Steamworks\`.
- **Unity** loads it from its plugin folder, `<Game>_Data\Plugins\`, usually from `x86_64` below it.

## The rules

Each rule names a DLL or passes to the next:

1. **An emulator `steam_api` that loads.** It comes first because an emulator `steam_api` answers
   every Steam call itself and never loads a `steamclient`, whatever a loader ini says. A Node.js game
   with GBE in `www\greenworks\lib`, loaded by `greenworks-win32.node`, is decided here even though a
   ColdClientLoader sits at its root.
2. **A ColdClientLoader target.** A `ColdClientLoader.ini` whose `[SteamClient]` section names
   `SteamClient64Dll` (or `SteamClientDll`) picks that DLL; the path is relative to the ini. The loader
   starts the game with that DLL standing in for Steam, so the game's own Valve `steam_api` talks to
   it. A GBE `steam_api64.dll` at the root of such an install, with nothing beside it that imports it,
   never loads, and rule 1 passes over it for that reason.
3. **Valve's `steam_api` that loads.** No emulator is installed yet, which is the install the wizard
   starts from.
4. **The only folder of emulator DLLs.** When every emulator DLL in the game sits in one folder, the
   one there. This catches a packed executable whose import table names nothing.
5. **The only folder of `steam_api` DLLs.** When every `steam_api` in the game sits in one folder, the
   one there. This catches a clean install that loads the DLL itself, through P/Invoke, Python's
   `ctypes` or `LoadLibrary`, from a folder no engine rule names.

In every rule, a 64-bit DLL beats a 32-bit one in the same folder. Within rules 1 and 3 the
shallowest folder wins. A crack or repack keeps copies of the game's executable and DLL in subfolders
such as `_crack`, and the game runs from the copy above them. Two candidates at the same depth in different folders
stop the detection undecided, rather than passing to the next rule, which could only guess; so do two
loader inis naming different DLLs.

## What each caller does with the answer

`GameCache` puts the folder beside the chosen DLL first in `GameInfo.SettingsDirs`, which makes it
`MetadataPath`'s folder; the other folders follow deepest first. When no rule decides, or no
`steam_settings` with an `achievements.json` sits beside the chosen DLL, the scan falls back to
deepest first.

The wizard acts on what the chosen DLL is:

- **A `steam_api64.dll`**: the wizard backs it up and replaces it with GBE's regular build, unless it
  is a CODEX or RUNE emulator the user chose to keep.
- **A 32-bit `steam_api.dll`**: accepted only when its CODEX or RUNE emulator is kept. Otherwise the
  folder page refuses it, since the wizard installs only GBE's 64-bit build.
- **A GBE `steamclient`**, of either build and whichever rule named it: the emulator is already in
  place, so the wizard writes the schema, icons and `steam_appid.txt` beside it and installs nothing.
- **A `steamclient` that is not GBE**, or nothing decided: the folder page refuses and says why. GBE's
  `steam_api` build cannot stand in for a `steamclient`.

## When a game breaks the rules

Every scan logs the outcome for each game with more than one `steam_settings` folder. The line names
the game's appid, and a **Report a problem…** file counts the folder holding the chosen DLL as already
disclosed, alongside the game's `steam_settings` folders, so the report keeps the line:

```
Steam DLL: appid=480, rule=coldclient-loader, dll='C:\Games\Example Game\bin\coldclient\steamclient64.dll'
```

When no rule decides, the line is a warning naming the candidates instead, relative to the game
folder, and when the chosen DLL has no schema beside it, a warning saying so. That warning, or a popup
whose text differs from what the emulator recorded, means an install the rules do not describe. Fix it
in this order:

1. Work out which DLL the game actually loads, from its launcher, its loader ini or its import tables.
2. Add a fixture recreating that layout to `tests/SteamDllDetectorTests.cs`, and check that it fails.
3. Change or add a rule in `SteamDllDetector` until the fixture passes, along with every existing one.
4. Update this page so its rules match the code.
