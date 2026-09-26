#!/usr/bin/env bash
# What /commit runs before it will draft a commit plan here. This repo commits straight to main, so
# nothing else reads a change before it reaches main; CI runs after the push, when a break is already public.
#
# Runs the conventions checker and the skill-script check, then the same restore, build and test as the
# build job in .github/workflows/build.yml, in the same order. Keep the two level: a gate that differs
# from CI disagrees with it about what main requires. The tag-only publish and packaging steps are not
# here. The checks before restore are not in CI, because they read the developer's ~/.claude, which no
# runner has.
#
# Prerequisites are `python3`, `pwsh` and `dotnet` on PATH. Their absence fails rather than skips: a check
# that cannot tell "passed" from "never ran" turns an open problem into a closed-looking one.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT" || exit 2

status=0

# Buffer each step and print its detail only when it fails, so a pass stays one line per step.
run() {
  local label="$1" out
  shift
  out="$(mktemp)"
  if "$@" >"$out" 2>&1; then
    echo "==> $label — $(grep -v '^\s*$' "$out" | tail -n 1)"
  else
    echo "==> $label — FAILED"
    cat "$out"
    status=1
  fi
  rm -f "$out"
}

run "conventions" python3 ~/.claude/conventions/check.py .
run "skill scripts" pwsh -NoProfile -NonInteractive -File docs/screenshots/check-skill-scripts.ps1
run "restore" dotnet restore
run "build" dotnet build -c Release --no-restore
run "test" dotnet test tests/ -c Release --no-restore

exit $status
