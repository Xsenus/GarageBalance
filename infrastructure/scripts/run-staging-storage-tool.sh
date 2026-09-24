#!/usr/bin/env bash
set -Eeuo pipefail

tool=/opt/garagebalance-staging/storage-tool/GarageBalance.StorageTool
checkpoint=/var/lib/garagebalance-staging/storage-migration/cloudru-backfill.json
if [[ -f /etc/garagebalance-staging-hostkey.env ]]; then
  checkpoint=/var/lib/garagebalance-staging/storage-migration/multi-s3-backfill.json
fi
[[ -x "$tool" ]] || { echo 'Storage tool is unavailable' >&2; exit 1; }
[[ "$#" == 1 ]] || exit 64

case "$1" in
  copy|resume|delta-sync)
    exec "$tool" "$1" --execute --checkpoint "$checkpoint" --max-jobs 1000
    ;;
  verify)
    exec "$tool" verify --execute
    ;;
  inventory|plan|cutover-check|status)
    exec "$tool" "$1"
    ;;
  *) echo 'Unsupported storage operation' >&2; exit 64 ;;
esac
