#!/bin/sh
set -eu

[ "$#" -gt 0 ] && [ -n "$1" ] || exit 0

# The staged files come as one argument, because separator interpolation
# avoids Husky's automatic re-staging. Split only on the unit separator,
# preserving spaces and disabling filename expansion. Custom variables pass
# one argument per file.
if [ "$#" -eq 1 ]; then
    IFS=$(printf '\037')
    set -f
    set -- $1
fi

exec bash scripts/lint-bicep.sh "$@"
