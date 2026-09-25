---
created: 2026-08-12 22:42
---

# Honour GBE's per-game overlay settings from a game's steam_settings

Honour GBE's own per-game overlay settings when a game's steam_settings/ has them: unlock sound (sounds/overlay_achievement_notification.wav), and display duration, position, font + size from configs.overlay.ini plus steam_settings/fonts. At minimum expose a font-size setting - the default reads as very small. Requested by ant-sh closing issue #5 (all explicitly optional).
