---
name: no-cracked-game-details-in-public-files
description: Never put the owner's cracked-game names, appids, paths, hashes or crack folders in a public file; keep them in the encrypted test-bed memory
metadata:
  type: feedback
---

Never put details of the owner's cracked games in a public file: game names, appids, install or save
paths, hashes, unlock times, crack folder names. That includes test fixtures, screenshot anchors, memos
and commit messages. Keep them in [[codex-rune-local-test-bed]] (encrypted) and refer to "the test bed"
publicly. CODEX/RUNE as names and the emulator's file format are fine to publish.

**Why:** the owner does not want crack-related information public on GitHub (2026-10-14); the repo is
public.

**How to apply:** before committing, grep added and removed lines for test-bed names; a removed line in
a public diff can reveal as much as an added one.
