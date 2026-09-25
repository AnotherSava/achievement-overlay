#!/bin/bash
# Pre-commit gate for /commit. Non-zero exit blocks the commit plan.
#
# Why this exists: a compiler warning in the *test* project reached a release once, because the
# checks run by hand were `dotnet build src/...` (which never compiles the test project) and
# `dotnet test` filtered down to its pass/fail line (which hides warnings). CI surfaced it as a
# build annotation after the tag was already pushed. Every project is checked here, warnings are
# errors, and the build is forced to run rather than being served from the incremental cache —
# MSBuild skips analysis for unchanged projects, so a cached build reports no warnings even when
# the code still has them.
#
# It also runs the conventions checker first, which re-measures every rule this repo has adopted
# (see .claude/conventions) plus the universal ones; nothing else runs it between /adopt walks.
# A clone without the maintainer's dotfiles has no checker, and that step prints SKIPPED.
#
# A pass does not cover publishing: the local deploy runs `dotnet publish src`, and a tag build in
# CI runs a self-contained single-file publish. Neither is repeated here.

set -uo pipefail

cd "$(dirname "$0")/.." || exit 1

fail() { echo; echo "FAILED: $1"; exit 1; }

echo "=== Conventions ==="
if [ -f ~/.claude/conventions/check.py ]; then
    python3 ~/.claude/conventions/check.py . \
        || fail "a convention rule failed or could not be measured"
else
    echo "SKIPPED: no ~/.claude/conventions/check.py (the maintainer's conventions checker)"
fi

echo

# The whole solution rather than the test project alone. src comes in through the project reference
# either way, but tools/ReplayReport calls the app's own resolver, and a tool that does that has to
# break the build when the resolver changes shape rather than rotting unnoticed until the next time
# someone needs it.
echo "=== Build (Release, warnings as errors) ==="
dotnet build achievement-overlay.slnx -c Release --no-incremental -warnaserror \
    || fail "build produced errors or warnings"

echo
echo "=== Tests ==="
dotnet test tests/AchievementOverlay.Tests.csproj -c Release --no-build \
    || fail "tests did not pass"

echo
echo "All commit checks passed."
