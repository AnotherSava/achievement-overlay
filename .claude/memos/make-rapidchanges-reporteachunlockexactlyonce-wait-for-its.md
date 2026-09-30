---
created: 2026-09-29 22:36:29
---

# Make RapidChanges_ReportEachUnlockExactlyOnce wait for its events instead of a fixed 500 ms

The watcher test RapidChanges_ReportEachUnlockExactlyOnce (tests/AchievementWatcherTests.cs) writes achievements.json twice through a real FileSystemWatcher, then sleeps a fixed Task.Delay(500) before asserting two events. On a slow GitHub runner the debounce plus processing had not finished: the v1.11.0 tag build (run 36673234992, 2026-09-30) failed with Expected: 2, Actual: 0, and the same commit passed on the main push build and on a rerun of the failed job. It is the only failure of this test in the last 40 build runs.

Next step: replace the fixed delay with a poll that waits until the expected event count arrives or a generous timeout (several seconds) expires, then assert. Check the file's other tests that use a fixed Task.Delay after a real file write for the same pattern, and fix them in the same change.
