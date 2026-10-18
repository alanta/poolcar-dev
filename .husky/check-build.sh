#!/bin/sh
set -eu

# The changed files only decide whether the solution needs a build
[ "$#" -gt 0 ] && [ -n "$1" ] || exit 0

exec dotnet build PoolCar.slnx -nologo -v q -tl:off -clp:NoSummary
