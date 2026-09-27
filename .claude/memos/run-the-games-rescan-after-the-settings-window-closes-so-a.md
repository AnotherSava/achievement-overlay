---
created: 2026-09-27 00:56:46
---

# Run the games rescan after the Settings window closes, so a slow scan cannot freeze it

The Settings window closes only after its save function returns, so a failed save keeps it open with its edits, and that function is TrayApplicationContext.ApplySettings, which calls _gameCache.ScanAll() synchronously when gamesPaths changed (then RebuildWatcher and NotifyTrackingConfiguredForExistingFolders). A gamesPaths change over a large or slow library therefore freezes the open window for the whole scan. Next step: split ApplySettings into the save (which must stay inside, so a failed write keeps the window open with its edits) and the post-save re-wiring (rescan, watcher rebuild, tracking sweep), and run the second part after ShowDialog returns in OpenSettingsDialog.
