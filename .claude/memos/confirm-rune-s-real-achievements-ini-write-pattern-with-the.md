---
created: 2026-10-14 21:19:53
---

# Confirm RUNE's real achievements.ini write pattern with the issue #11 reporter after the release

CODEX/RUNE support (issue #11) was verified with tests and with synthetic writes into a copy of a real RUNE save folder: in-place rewrite, temp file plus rename, and delete plus create each raised exactly one popup. How the RUNE emulator itself writes `achievements.ini` during a real unlock was never measured, because nobody played a RUNE game for it. The watcher's `Filters`, debounce and rename handling assume one of those three patterns.

Once the release with CODEX/RUNE support is out, draft a comment on issue #11 for the owner's approval (outward text, so draft, show, confirm, send): ask the reporter to add the game through the wizard, unlock one achievement under RUNE, and say whether the popup appeared, attaching a "Report a problem" file if it did not. The log line "New achievement unlocked" with the INI path confirms the pattern works; its absence plus the file's mtime changing means the write pattern is one the watcher misses. Close this memo when the reporter confirms, or on a local unlock in the test bed (see the encrypted test-bed memory).
