#!/bin/sh
set -eu

if ! command -v gitleaks >/dev/null 2>&1; then
    printf '%s\n' \
        'Pre-commit blocked: gitleaks is not installed or is not on PATH.' \
        'Install gitleaks using https://github.com/gitleaks/gitleaks#installing, then retry the commit.' >&2
    exit 127
fi

exec gitleaks git --staged --redact
