---
layout: default
title: Installation
nav_order: 2
---

# Installation

Achievement Overlay needs Windows 10 or later. It does not need administrator rights, and it installs nothing — unzip it wherever you like and run it.

## Download a release

Grab the latest build from [GitHub Releases](https://github.com/AnotherSava/achievement-overlay/releases). Each release carries two archives:

| Archive | Size | Needs |
|---|---|---|
| **Self-contained** | larger | nothing — a single exe, unzip and run |
| **Framework-dependent** | smaller | [.NET Desktop Runtime 10](https://dotnet.microsoft.com/download/dotnet/10.0) |

After extracting, open `config.json` next to the executable and check `gamesPaths` — it should list the folders your games are installed in. Every other value has a working default; the full list is on the [Configuration](usage/configuration) page.

Run the exe and the app appears in the system tray. There is no main window: everything happens through the [tray menu](usage/tray-menu).

## SmartScreen and code signing

The executable is not signed, so Windows shows a SmartScreen warning the first time you run it. Choose **More info → Run anyway**.

The project is planning to apply for free code signing through the [SignPath Foundation](https://signpath.org) once it meets their community adoption requirements. **You can help** — starring the repo, forking it, or contributing all count towards those requirements.

## Build it yourself

To run the newest, potentially less stable code, build from source instead — see the [Developer guide](development).
