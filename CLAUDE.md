# CLAUDE.md

Extends [../CLAUDE.md](../CLAUDE.md) — read that file for full context on the GBE emulator forks, build system, overlay approaches, and sibling projects.

## What This Is

C# WPF app — Steam-like achievement overlay notifications for GBE-configured games. Monitors `%appdata%/GSE Saves/` for `achievements.json` changes via `FileSystemWatcher` and displays transparent popup notifications over the game window. No game process interaction — purely filesystem-based.

Stack: .NET 10, WinForms tray + WPF overlay window.
