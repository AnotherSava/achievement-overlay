---
created: 2026-09-27 00:56:47
---

# Check the fixes verified only by reading code in the running app

Three behaviours were fixed in September 2026 on the strength of reading the WPF and WinForms sources, with no run and no unit test that reaches them: (1) Exit from the tray menu while the Settings window is open, and again while the Report a problem window is open - the process should end and the tray icon disappear, not linger. (2) A failed Settings save - make config.json read-only, change a setting, press Save - should say the settings were not saved and keep the window open with the edit; Cancel then discards it. A config.json.tmp left beside it afterwards is expected: nothing removes it, and the next save writes over it. (3) The global unhandled-exception handlers wired in Program.Main and HotkeyWindow (Application.ThreadException forwarding) should log an unexpected UI-thread exception at Error rather than crash silently; this one needs a debug build that throws on purpose, so check it only if a crash report ever lacks a log line. Driving the app takes the machine, so ask before doing it rather than scripting it unannounced.
