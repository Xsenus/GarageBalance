#!/usr/bin/env bash
set -Eeuo pipefail

readonly STATE_FILE="/run/garagebalance-healthcheck.failures"
readonly HEALTH_URL="http://127.0.0.1:3101/health/ready"
readonly FAILURE_LIMIT=3

failures=0
if [[ -r "$STATE_FILE" ]]; then
  read -r failures < "$STATE_FILE" || failures=0
fi

if curl --noproxy 127.0.0.1 --fail --silent --show-error --max-time 8 \
  -H "Host: sgk.blagodaty.ru" "$HEALTH_URL" >/dev/null; then
  rm -f "$STATE_FILE"
  exit 0
fi

failures=$((failures + 1))
printf '%s\n' "$failures" > "$STATE_FILE"
logger -p daemon.warning -t garagebalance-healthcheck "health check failed; consecutiveFailures=${failures}"

if (( failures < FAILURE_LIMIT )); then
  exit 0
fi

logger -p daemon.err -t garagebalance-healthcheck "health check failed ${failures} times; restarting garagebalance-staging.service"
systemctl try-restart garagebalance-staging.service
rm -f "$STATE_FILE"
