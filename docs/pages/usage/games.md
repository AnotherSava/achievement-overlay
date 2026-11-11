---
layout: default
title: Games
parent: Usage
nav_order: 4
---

# Games

The **Games** window shows every game the overlay knows of and is where you add a game or undo a setup. Open it with a left click on the tray icon, or with **Games…** at the top of the tray menu.

## What the list shows

Each row is one game. Besides the games set up with [Add game](adding-a-game), the list shows games that are **not set up**, on a tinted background: a game in your games folders whose `steam_appid.txt` or `steam_emu.ini` names its AppID, and a game known only from a save folder, typically one that was uninstalled. A game in your games folders that names no AppID is not listed; add it with **Add game…**.

- The icon column after the name checks the game's setup: a green check when it is complete, an amber **!** when something keeps it from working fully, with the problems listed on hover. It checks that the achievement list sits beside the Steam library the game loads, that every achievement has a description (hidden ones included) and an icon, and that the emulator's save folder is one Settings watches. Whether the list is still Steam's current one is not checked.
- **Emulator** — what the game's Steam library is right now: GBE, GBE debug, GBE (loader) for a game that loads GBE through a ColdClientLoader, RUNE, CODEX, or Steam for Valve's own library. For a game not set up, it is read from the unlock file instead.
- **Achievements** — earned out of the total the game's achievement list holds. For a game with no achievement list the total is shown as `?`, and the column is empty when the game has recorded nothing at all.
- **Last unlock** — how long ago the latest achievement was earned. Hover for the date and time.
- **AppID** — the game's Steam AppID.
- **Game folder** — where the game is installed. For a game not set up, the folder in your games folders that names its AppID, if one does; often none does, because the save folder outlives an uninstalled game.
- **Save folder** — where the emulator records the game's unlocks. Empty until the game has saved any.

Double-click a folder to open it in Explorer. The **⋯** button at the end of each row opens a menu with **Open game folder**, **Open save folder**, and **Revert…** or **Set up…**; an action that cannot run is greyed out, with the reason on hover.

**Add game…** at the top opens the [Add game](adding-a-game) wizard, and the list refreshes when the wizard closes. **Columns…** beside it picks which columns are shown; the game's name and the **⋯** button always are. The choice is kept for next time.

## Setting up a game from the list

A game that is not set up has **Set up…** in its row menu in place of **Revert…**. It opens [Add game](adding-a-game) with the game's folder filled in when the list found one, and otherwise lets you pick where the game is installed. If the folder names no AppID, the wizard offers the one the game saved its achievements under for you to confirm.

## Reverting a game

**Revert…**, in the row's **⋯** menu, puts a game back the way it was before Add game set it up. Close the game first: Windows will not let the app replace a library the game has open, and the revert stops before changing anything.

What a revert undoes:

- the game's own Steam library comes back from the backup Add game made (`steam_api64.dll.original`)
- every file Add game wrote in `steam_settings` is removed, and any file it overwrote, such as one a repack shipped, is put back
- the `steam_settings` folder goes too, if Add game created it

The unlock history in the save folders is kept, so setting the game up again later picks up where it left off.

Add game records what it writes in `steam_settings/achievement-overlay.json`, with copies of the files it overwrote in `steam_settings/.achievement-overlay-backup/`. A game set up before that record existed gets only its library back; its `steam_settings` files stay, because nothing says which of them came with the game. **Revert…** is greyed out for a game that has neither a record nor a backup; hover it to see why.
