#!/bin/sh
set -eu

[ "$#" -gt 0 ] && [ -n "$1" ] || exit 0

# Separator interpolation avoids Husky's automatic re-staging. Split only on
# the unit separator, preserving spaces and disabling filename expansion.
IFS=$(printf '\037')
set -f
set -- $1

exec bash scripts/lint-bicep.sh "$@"
