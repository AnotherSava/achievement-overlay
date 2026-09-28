---
created: 2026-09-27 19:38:28
---

# Refuse a folder already in the Settings list under another spelling

SettingsWindow.AddFolder, used for both the games folders and the GSE Saves folders, refuses a picked folder that is already listed. It compares `AppConfig.ExpandEnvironmentVariables(entry)` with the picked path as strings, OrdinalIgnoreCase. So an existing entry spelled differently from the picker's canonical path is not recognised: a hand-edited `C:\Games\` (trailing separator) or `C:/Games`. Picking `C:\Games` then adds a second card for the same folder.

Impact is small. Since the FolderPath change, GameCache scans gamesPaths as a minimal set, so a duplicate games folder is walked once, and the app logs "is covered by". What remains is a duplicate card in Settings and a duplicate entry written to config.json. For GSE Saves paths a duplicate is not merged. The watcher would watch the same folder twice, and AchievementWatcher.WarnAboutGamesInSeveralPaths would warn about every game in it as if it sat in two paths. That second case is the one worth fixing.

Next step: compare through FolderPath, for example treating two entries as the same folder when each Contains the other, instead of string equality. Consider whether a picked folder inside an existing games entry should also be refused or noted, since the scan will cover it anyway. Add a test at whatever level SettingsWindow's logic is testable (the method is private on a WPF window; the comparison could live beside FolderPath if a second caller wants it, but no speculative helper).
