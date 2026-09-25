---
created: 2026-09-12 05:19
---

# Treat a whitespace-only text value as absent, not as a blank popup title

A whitespace-only text value still renders as a blank popup title. v1.10.1 changed CarriesLanguage (src/AchievementMetadata.cs) to IsNullOrWhiteSpace, so such a value can no longer WIN a field by language — but FirstNonEmpty and IsNonEmpty still test IsNullOrEmpty, so a schema entry whose displayName or description holds only whitespace, with no inline text behind it to fall through to, is accepted as text and drawn as an empty line. Measured during the v1.10.1 adversarial review: zero whitespace-only values across the 11 steam_settings schemas on this machine (479 achievements) and zero in ant-sh's AC Odyssey report, so nothing observed reaches it — which is why the review scoped it out of that diff instead of fixing it. Fix: make FirstNonEmpty skip whitespace so resolution falls through to the achievement's own internal name, which is what the popup shows when nothing else resolves. Look at IsNonEmpty in the same pass, since it decides whether a game counts as self-describing and a whitespace-only inline value would wrongly qualify one.
