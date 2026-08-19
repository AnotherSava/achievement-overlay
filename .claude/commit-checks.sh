#!/bin/bash
# Pre-commit gate for /commit. Non-zero exit blocks the commit plan.
#
# Why this exists: a compiler warning in the *test* project reached a release once, because the
# checks run by hand were `dotnet build src/...` (which never compiles the test project) and
# `dotnet test` filtered down to its pass/fail line (which hides warnings). CI surfaced it as a
# build annotation after the tag was already pushed. Both projects are checked here, warnings are
# errors, and the build is forced to run rather than being served from the incremental cache —
# MSBuild skips analysis for unchanged projects, so a cached build reports no warnings even when
# the code still has them.

set -uo pipefail

cd "$(dirname "$0")/.." || exit 1

fail() { echo; echo "FAILED: $1"; exit 1; }

# Building the test project also builds src through its project reference, so this covers both.
echo "=== Build (Release, warnings as errors) ==="
dotnet build tests/AchievementOverlay.Tests.csproj -c Release --no-incremental -warnaserror \
    || fail "build produced errors or warnings"

echo
echo "=== Tests ==="
dotnet test tests/AchievementOverlay.Tests.csproj -c Release --no-build \
    || fail "tests did not pass"

echo
echo "All commit checks passed."
