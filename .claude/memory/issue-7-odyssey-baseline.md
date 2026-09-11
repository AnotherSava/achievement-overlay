---
name: issue-7-odyssey-baseline
description: Measured baseline for issue #7 — every one of ant-sh's 93 Odyssey achievements matches by padding, never exactly, so his Russian schema reaches nothing
metadata:
  type: project
---

Measured 2026-09-11 by replaying ant-sh's attached diagnostic report for AC Odyssey (appid 812140,
linked from issue #7) through `tools/ReplayReport`. This is the before-state any fix for #7 is judged
against.

- His unlock file names its 93 achievements `1`..`93`, unpadded; his schema names all 93 `001`..`093`,
  zero-padded. Exact-name overlap between the two is **zero**.
- So all 93 resolve through the leading-zero fallback, `matchedExactly` is false for every one, and
  the unlock file's own text leads: displayName and description come from the unlock file 93 times
  and from the schema 0 times.
- His schema does carry russian — 15 real languages, and no blank descriptions in any of them,
  including the 31 Steam redacts. His config is `"language": "russian"`. None of that russian reaches
  the screen.
- His unlock file's text is plain strings (english only), so there is nothing to choose between there.

**Why:** the open memo about the two resolver fixes describes fix (2) as repairing the *exact-match*
path ("on an EXACT match the schema leads unconditionally"). He has no exact matches at all, so a
patch scoped to that branch would do nothing for him. The per-field rule as that memo states it does
fix him, but only because it replaces the `matchedExactly` ternary outright rather than amending one
side of it — and those two readings are easy to confuse while implementing.

**How to apply:** before claiming #7 is fixed, re-run the replay on his report rather than reasoning
about it — `dotnet run --project tools/ReplayReport -- <report.json>`, whose summary counts are the
before and the after. A fix that works shows `displayName schema 93` where the baseline shows
`unlock 93`. Check which of the two readings of fix (2) is being built.
