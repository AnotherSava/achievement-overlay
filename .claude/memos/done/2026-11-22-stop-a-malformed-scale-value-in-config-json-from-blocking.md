---
created: 2026-08-30 19:05
---

# Stop a malformed scale value in config.json from blocking startup

`NotificationScaleConverter.Read` throws `JsonException` on an unexpected token (src/NotificationScale.cs:111), which escapes `AppConfig.Load` into the constructor catch at src/TrayApplicationContext.cs:57 and shows the startup config-error dialog. So `"scale": true` in a hand-edited config.json stops the app from starting, against that file's own stated policy that a bad value must cost at most the popup's size. Return the default and `reader.Skip()` instead, as the new anchor/background converters do.
