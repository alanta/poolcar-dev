#!/usr/bin/env bash
# Starts the AppHost with Aspire and prints the EasyAuth proxy URL once the proxy is healthy.
# Aspire assigns the ports, so several worktrees can run at the same time.
set -euo pipefail

apphost="$(cd "$(dirname "$0")/.." && pwd)/src/PoolCar.AppHost/PoolCar.AppHost.csproj"
aspire_options=(--apphost "$apphost" --non-interactive --nologo)

aspire run --isolated "${aspire_options[@]}" "$@" &
aspire_pid=$!
trap 'kill "$aspire_pid" 2>/dev/null || true; wait "$aspire_pid" 2>/dev/null || true' EXIT INT TERM

until aspire wait easyauth "${aspire_options[@]}" --timeout 600 >/dev/null 2>&1; do
  kill -0 "$aspire_pid" 2>/dev/null || exit 1
  sleep 2
done

proxy_url="$(aspire describe easyauth "${aspire_options[@]}" --format Json \
  | jq -r '[.resources[].urls[]] | (map(select(.name == "http")) + .)[0].url')"

echo "PoolCar web UI: ${proxy_url}"

wait "$aspire_pid"
