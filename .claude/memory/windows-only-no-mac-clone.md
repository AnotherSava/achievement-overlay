---
name: windows-only-no-mac-clone
description: achievement-overlay is Windows-only and never checked out on the Mac; no cross-machine sync concerns apply
metadata:
  type: project
---

This repo is worked on from the Windows machine only. The app is Windows-only (WPF, WinForms, Win32 hooks) and there is no macOS clone, so the global "these repos are used from both a Windows and a macOS machine" premise does not hold here.

**Why:** a force-push plan warned that "the macOS clone must reset" afterwards, and the owner pointed out, not for the first time, that no such clone exists.

**How to apply:** never raise macOS-clone concerns for this repo — no "the other machine must reset/pull", no routing work to a Mac session, no `[macos]` memo tags. A history rewrite or force-push here affects only `origin` and this checkout.
