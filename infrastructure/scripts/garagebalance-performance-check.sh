#!/usr/bin/env bash
set -Eeuo pipefail

readonly LOG_FILE="/var/log/garagebalance-nginx/garagebalance-staging-timing.log"
readonly SAMPLE_LIMIT=1000
readonly MINIMUM_SAMPLES=5
readonly P95_LIMIT_SECONDS="1.500"
readonly MAX_LIMIT_SECONDS="15.000"

[[ -r "$LOG_FILE" ]] || exit 0

sample_file="$(mktemp)"
trap 'rm -f "$sample_file"' EXIT

tail -n "$SAMPLE_LIMIT" "$LOG_FILE" |
  awk '
    {
      status = "";
      duration = "";
      uri = "";
      for (field_index = 1; field_index <= NF; field_index++) {
        if ($field_index ~ /^status=/) { split($field_index, pair, "="); status = pair[2] }
        if ($field_index ~ /^request_time=/) { split($field_index, pair, "="); duration = pair[2] }
        if ($field_index ~ /^uri=/) { split($field_index, pair, "="); uri = pair[2] }
      }
      if (uri ~ /^\/(api\/|health$)/ && duration ~ /^[0-9.]+$/) {
        print duration, status
      }
    }
  ' > "$sample_file"

count="$(wc -l < "$sample_file" | tr -d ' ')"
(( count >= MINIMUM_SAMPLES )) || exit 0

p95_rank=$(( (count * 95 + 99) / 100 ))
p95="$(sort -n -k1,1 "$sample_file" | sed -n "${p95_rank}p" | awk '{ print $1 }')"
maximum="$(sort -n -k1,1 "$sample_file" | tail -n 1 | awk '{ print $1 }')"
server_errors="$(awk '$2 ~ /^5/ { count++ } END { print count + 0 }' "$sample_file")"

if (( server_errors > 0 )) || awk "BEGIN { exit !(${p95} > ${P95_LIMIT_SECONDS} || ${maximum} > ${MAX_LIMIT_SECONDS}) }"; then
  logger -p daemon.warning -t garagebalance-performance "threshold exceeded; samples=${count}; p95Seconds=${p95}; maxSeconds=${maximum}; serverErrors=${server_errors}"
  exit 0
fi

logger -p daemon.info -t garagebalance-performance "threshold ok; samples=${count}; p95Seconds=${p95}; maxSeconds=${maximum}; serverErrors=0"
