#!/usr/bin/env bash
# Lints the given Bicep files, or every file under infra/ without arguments. Any diagnostic, also a warning, fails
# the lint. CI and the pre-commit hook both run this script.
set -euo pipefail

bicep="$(command -v bicep || true)"
# az bicep install puts the CLI here without adding it to PATH
if [[ -z "$bicep" && -x "$HOME/.azure/bin/bicep" ]]; then
  bicep="$HOME/.azure/bin/bicep"
fi

if [[ -z "$bicep" ]]; then
  printf '%s\n' \
    'Bicep lint blocked: the Bicep CLI is not installed or is not on PATH.' \
    'Install it with "az bicep install" or https://learn.microsoft.com/azure/azure-resource-manager/bicep/install, then retry.' >&2
  exit 127
fi

if [[ $# -eq 0 ]]; then
  cd "$(dirname "$0")/.."
  shopt -s globstar nullglob
  set -- infra/**/*.bicep
fi

failed=0
for file in "$@"; do
  output="$("$bicep" lint "$file" 2>&1)" || failed=1
  if [[ -n "$output" ]]; then
    echo "$output"
    failed=1
  fi
done
exit $failed
