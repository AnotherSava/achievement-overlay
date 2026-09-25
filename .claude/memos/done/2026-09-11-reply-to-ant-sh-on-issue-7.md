---
created: 2026-09-03 02:32
---

# Reply to ant-sh on issue #7

Reply to ant-sh on issue #7 - promised repeatedly on 2026-09-02 and never written. His last comment (2026-09-02 08:14) confirms v1.9.1 fixes the icons against his ORIGINAL padded schema, and asks that the Achievement text language setting be honoured where the Goldberg json has the text. What the reply needs to carry: (a) his diagnosis is right, and the mechanism is the padding-vs-exact ternary at AchievementMetadata.cs:494; (b) his old hand-patched schema WOULD have shown Russian, so v1.9.1 traded his hand-editing for his localisation - worth saying plainly; (c) ask for his steam_settings/achievements.json for 812140 and his GSE Saves 812140/achievements.json, plus what Achievement text is set to, since whether his schema carries russian at all is still unmeasured; (d) v1.9.2 now ships Report a problem..., which produces exactly those files in one attachment. Verified facts to draw on: Steam's GetSchemaForGame accepts l=<language> (14 real languages for 812140), 31 of its 93 achievements are hidden with blank descriptions in EVERY language, and 001 - his own example - is one of them, while his unlock file carries a description Steam does not have.
