---
created: 2026-09-02 22:35
---

# Fold the achievement schema across all of a game's steam_settings folders

Atomfall shows 25 achievements with no description: GameCache.MetadataPath (GameCache.cs:186) takes only the deepest steam_settings folder (bin/coldclient/, 25 blank descriptions) while the root steam_settings/ has all 54 populated. GbeOverlaySettingsReader already folds across all SettingsDirs with first-definition-wins; the schema should get the same treatment instead of ordered[0] alone.
