#!/usr/bin/env bash
# Prototype only: production cleanup remains blocked until the whole protocol is qualified.
set -Eeuo pipefail

readonly lock_dir=/var/lib/garagebalance-maintenance
readonly marker="$lock_dir/maintenance-active"
readonly receipt="$lock_dir/recovery-required"
readonly participants_receipt="$lock_dir/participants-resume-required"
readonly deploy_lock="$lock_dir/deploy.lock"
readonly activity_lock="$lock_dir/activity.lock"
run_shared() {
  local lock=$1
  local allow_recovery=$2
  shift 2
  [[ -f "$lock" && ! -L "$lock" ]] || { echo 'barrier lock unavailable' >&2; exit 75; }
  # Check the durable marker after acquiring the gate, never before it.
  exec /usr/bin/flock --shared --nonblock "$lock" /bin/bash -c \
    '[[ ! -e "$1" && ! -L "$1" ]] || exit 75; if [[ "$3" != yes ]]; then [[ ! -e "$2" && ! -L "$2" ]] || exit 75; fi; if [[ "$3" == deploy ]]; then [[ ! -e "$4" && ! -L "$4" ]] || exit 75; fi; shift 4; exec "$@"' bash "$marker" "$receipt" "$allow_recovery" "$participants_receipt" "$@"
}
mode="${1:-}"
shift || exit 64
case "$mode" in
  app) [[ "$#" == 0 ]] || exit 64
    run_shared "$activity_lock" yes /opt/garagebalance-staging/api/GarageBalance.Api ;;
  storage) [[ "$#" == 1 ]] || exit 64
    run_shared "$activity_lock" no /usr/local/bin/garagebalance-storage-tool-run "$1" ;;
  health) [[ "$#" == 0 ]] || exit 64
    run_shared "$activity_lock" no /usr/local/bin/garagebalance-healthcheck ;;
  deploy) [[ "$#" == 1 && "$1" =~ ^[A-Za-z0-9._-]+$ ]] || exit 64
    run_shared "$deploy_lock" deploy /usr/local/bin/garagebalance-deploy-apply "$1" ;;
  maintenance) [[ "$#" == 0 ]] || exit 64
    echo 'maintenance orchestration is not yet qualified' >&2; exit 75 ;;
  *) printf '%s\n' 'unsupported barrier participant' >&2; exit 64 ;;
esac
