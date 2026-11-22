---
created: 2026-09-27 15:24:56
---

# Stop the watcher retrying a missing unlock file as if it were locked

In AchievementWatcher.ReadFileWithRetryAsync the first catch is `catch (IOException) when (attempt < _maxRetries)`. FileNotFoundException and DirectoryNotFoundException both derive from IOException, so a file that has gone is caught there first. The watcher logs `File locked, retry 1/3` through `3/3`, waits 3 × 200 ms (the defaults), and only on the last attempt reaches `catch (FileNotFoundException)`, which logs "File not found (may have been deleted)". A game folder that has gone takes the last catch instead and logs a Warn, `Failed to read after 3 retries`, blaming a lock that never existed. The order is the same at ff7db5c, so it predates the September changes.

Impact: small. The read runs after the debounce that follows a change event, so the file is normally there. The case needs the file or its folder removed between the event and the read. What it costs is three misleading log lines, a Warn in the folder case, and 600 ms.

Next step: move the FileNotFoundException catch above the filtered IOException one and catch DirectoryNotFoundException with it. Add a watcher test that deletes the file before the read and checks the log has no `File locked` line (the tests that read the log belong to the "App log" collection). Two lines move, and no logic is added.
