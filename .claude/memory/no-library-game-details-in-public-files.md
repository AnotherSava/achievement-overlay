---
name: no-library-game-details-in-public-files
description: Never put the owner's library games (names, appids, paths, hashes, crack folders) in a public file; use invented or well-known examples instead
metadata:
  type: feedback
---

Never put details of games in the owner's library in a public file: game names, appids, install or
save paths, hashes, unlock times, crack folder names. That covers every game they own, GBE-configured
ones included, not only the CODEX/RUNE ones, and it includes test fixtures, examples in UI text,
screenshot anchors, completed plans, memos, code comments and commit messages. Keep the specifics in
[[codex-rune-local-test-bed]] (encrypted) and refer to "the test bed" publicly. In examples, use
invented names ("Example Game", appid 2000001) or a well-known title that isn't in the library
(620 is Portal 2).

The one exception is the Red Dead Redemption overlay-incompatibility statements in the README, the
docs index, troubleshooting and the GBE reference, and the RDR popup in
`docs/screenshots/sample-notification.png` (kept by the owner on 2026-10-15). They stay. Screenshots
are public files too: a capture taken on this machine shows library names unless it is pointed at
an invented game. CODEX/RUNE as names and the emulator's file format are fine to publish too.

**Why:** the owner does not want crack-related information public on GitHub (2026-10-14), and widened
the scrub to every library game on 2026-10-15. The repo is public.

**How to apply:** before committing, grep the added and removed lines for library titles and appids.
A removed line in a public diff reveals as much as an added one.
