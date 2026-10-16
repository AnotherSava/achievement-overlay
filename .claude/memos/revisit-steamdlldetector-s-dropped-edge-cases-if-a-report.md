---
created: 2026-10-15 20:19:39
---

# Revisit SteamDllDetector's dropped edge cases if a report reaches one

Dropped in the 2026-10-15 reviews as rare, kept here so a future report can be matched to them: (1) Detect has unguarded IO (FileVersionInfo, File.ReadAllBytes, ColdClientLoader.ini read), so a file deleted mid-scan aborts the whole games root in ScanDirectory; (2) rule 2 reads only ColdClientLoader.ini, while GBE's loader reads <its exe name>.ini first, so a renamed loader is missed; (3) LoaderTarget takes SteamClient64Dll and never falls back to SteamClientDll when that file is missing.
