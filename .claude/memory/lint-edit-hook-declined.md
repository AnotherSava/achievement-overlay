---
name: lint-edit-hook-declined
description: A PostToolUse hook checking .cs formatting on each edit was offered and declined; the build and commit gate already enforce every lint rule
metadata:
  type: project
---

On 2026-09-25, while adopting the C# linter, the user declined a PostToolUse hook that would run `dotnet format whitespace --verify-no-changes` on each edited `.cs` file (measured at about 1.5 s per edit).

**Why:** the build enforces every lint rule, formatting included: `EnforceCodeStyleInBuild` plus `-warnaserror` in `.claude/commit-checks.sh` and CI. A hook would add latency to every edit for one rule out of dozens.

**How to apply:** do not offer the edit hook again for this repo. The generation-time enforcement preference in the global CLAUDE.md is already met here by the build.
