#!/usr/bin/env bash
# Stop hook for GitHub Copilot, registered in .github/hooks/poolcar.json, and for Claude Code, registered with
# --claude in .claude/settings.json. Before the agent ends its turn, the Husky task group "uncommitted" checks the
# uncommitted changes, and a failed check asks the agent to fix it first. A working tree that was checked before is
# not checked again, so an agent that cannot fix a failure can still stop and report it.
set -euo pipefail

cat > /dev/null

# Copilot CLI also reads .claude/settings.json, so it would run this hook twice
if [[ "${1:-}" == --claude && -n "${COPILOT_CLI:-}" ]]; then
  exit 0
fi

cd "$(dirname "$0")/.."

[[ -n "$(git status --porcelain)" ]] || exit 0

# Identify the working tree, including untracked files, by writing it as a tree with a copy of the index. The copy
# keeps the file stats, so only changed files are hashed, and the real index is left alone.
index="$(mktemp)"
trap 'rm -f "$index"' EXIT
cp "$(git rev-parse --git-path index)" "$index"
GIT_INDEX_FILE="$index" git add --all
state="$(GIT_INDEX_FILE="$index" git write-tree)"

checked="$(git rev-parse --git-path poolcar-checked-tree)"
[[ "$state" != "$(cat "$checked" 2> /dev/null)" ]] || exit 0
echo "$state" > "$checked"

if output="$(dotnet husky run --group uncommitted 2>&1)"; then
  exit 0
fi

reason="The PoolCar checks failed for the uncommitted changes. Fix these problems before you finish; for formatting, \
run \`dotnet format PoolCar.slnx --include <files>\`. If a problem does not come from your changes, tell the user \
instead of changing that code.

$(grep -vE '^(\[Husky\]|-+$|\s*$)' <<< "$output" | awk '!seen[$0]++' | tail -n 60)"

# Claude Code and Copilot CLI read the top-level decision; VS Code reads hookSpecificOutput, which Claude Code rejects
if [[ "${1:-}" == --claude ]]; then
  jq -n --arg reason "$reason" '{decision: "block", reason: $reason}'
else
  jq -n --arg reason "$reason" \
    '{decision: "block", reason: $reason, hookSpecificOutput: {hookEventName: "Stop", decision: "block", reason: $reason}}'
fi
