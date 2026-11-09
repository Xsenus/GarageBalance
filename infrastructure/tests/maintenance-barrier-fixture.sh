#!/usr/bin/env bash
# Synthetic systemd fixture only: no production units, network or database.
set -Eeuo pipefail
[[ $(uname -s) == Linux && -d /run/systemd/system && $(id -u) == 0 ]] || { echo 'requires Linux, running systemd and root' >&2; exit 1; }
[[ $# == 2 && "$1" =~ ^[a-f0-9]{64}$ && "$2" =~ ^[a-f0-9]{64}$ ]] || { echo 'requires reviewed fixture and helper hashes' >&2; exit 64; }
source_dir=$(cd -- "$(dirname -- "$0")/../scripts" && pwd -P)
source_helper="$source_dir/staging-maintenance-barrier.sh"
[[ -f "$source_helper" && ! -L "$source_helper" ]] || exit 1
[[ $(sha256sum -- "$0" | cut -d' ' -f1) == "$1" && $(sha256sum -- "$source_helper" | cut -d' ' -f1) == "$2" ]] || {
  echo 'reviewed fixture or helper hash differs' >&2; exit 1;
}
prefix="gb-barrier-fixture-$(cat /proc/sys/kernel/random/uuid)"
dir=$(mktemp -d /run/gb-barrier-fixture.XXXXXXXX)
[[ "$dir" =~ ^/run/gb-barrier-fixture[.][A-Za-z0-9]+$ ]] || exit 1
printf 'fixturePrefix=%s\nfixtureDirectory=%s\n' "$prefix" "$dir"
units=()
cleanup() {
  local unit load state pid cgroup failed=0 original=$?
  trap - EXIT
  for unit in "${units[@]}"; do
    systemctl stop "$unit" >/dev/null 2>&1 || true
    systemctl reset-failed "$unit" >/dev/null 2>&1 || true
    load=$(systemctl show "$unit" --value --property=LoadState 2>/dev/null || true)
    state=$(systemctl show "$unit" --value --property=ActiveState 2>/dev/null || true)
    pid=$(systemctl show "$unit" --value --property=MainPID 2>/dev/null || true)
    cgroup=$(systemctl show "$unit" --value --property=ControlGroup 2>/dev/null || true)
    if [[ "$load" != not-found ]] && { [[ "$load" != loaded || ( "$state" != inactive && "$state" != failed ) || "$pid" != 0 || -n "$cgroup" ]]; }; then
      printf 'fixtureUnitCleanupUncertain=%s\n' "$unit" >&2
      failed=1
    fi
  done
  if (( failed != 0 )); then printf 'fixtureDirectoryRetained=%s\n' "$dir" >&2; exit 1; fi
  if [[ -d "$dir/maintenance-active" && ! -L "$dir/maintenance-active" ]]; then rmdir -- "$dir/maintenance-active" || exit 1; fi
  rm -f -- "$dir/deploy.lock" "$dir/activity.lock" "$dir/maintenance-active" \
    "$dir/recovery-required" "$dir/participants-resume-required" "$dir/observed" "$dir/helper" "$dir/helper-source" "$dir/mock"
  if ! rmdir -- "$dir"; then printf 'fixtureDirectoryCleanupFailed=%s\n' "$dir" >&2; failed=1; fi
  (( failed == 0 )) || exit 1
  exit "$original"
}
trap cleanup EXIT
trap 'exit 143' TERM
trap 'exit 130' INT

# Use one verified private copy so the source cannot change between review and sed.
cp -- "$source_helper" "$dir/helper-source"
[[ $(sha256sum -- "$dir/helper-source" | cut -d' ' -f1) == "$2" ]] || { echo 'helper copy hash differs' >&2; exit 1; }
# Copy the actual participant logic, replacing only its fixed paths with this fixture.
sed -e "s#/var/lib/garagebalance-maintenance#$dir#g" \
    -e "s#/opt/garagebalance-staging/api/GarageBalance.Api#/bin/sh $dir/mock#g" \
    -e "s#/usr/local/bin/garagebalance-storage-tool-run#/bin/sh $dir/mock#g" \
    -e "s#/usr/local/bin/garagebalance-healthcheck#/bin/sh $dir/mock#g" \
    -e "s#/usr/local/bin/garagebalance-deploy-apply#/bin/sh $dir/mock#g" \
    -e 's/--shared --nonblock "\$lock"/--shared --nonblock --conflict-exit-code 42 "\$lock"/' \
    "$dir/helper-source" > "$dir/helper"
grep -Fq -- '--shared --nonblock --conflict-exit-code 42 "$lock"' "$dir/helper" || { echo 'FAIL: helper instrumentation mismatch' >&2; exit 1; }
if grep -Eq '/opt/garagebalance-staging|/usr/local/bin/garagebalance-|/var/lib/garagebalance-maintenance' "$dir/helper"; then
  echo 'FAIL: helper still references a working path' >&2; exit 1
fi
printf '#!/bin/sh\ntouch -- "%s/observed"\n' "$dir" > "$dir/mock"
chmod 0700 "$dir/helper" "$dir/mock"
touch "$dir/deploy.lock" "$dir/activity.lock"

sequence=0
run_case() {
  local mode=$1 expected=$2 actual status unit result
  shift 2
  sequence=$((sequence + 1))
  unit="$prefix-$sequence.service"
  units+=("$unit")
  rm -f -- "$dir/observed"
  set +e
  systemd-run --quiet --wait --unit "${unit%.service}" --property=Type=oneshot \
    --property=PrivateNetwork=yes --property=NoNewPrivileges=yes --property=SystemCallArchitectures=native \
    --property=PrivateTmp=yes --property=ProtectSystem=strict --property="ReadWritePaths=$dir" \
    --property=RestrictAddressFamilies=AF_UNIX --property=SystemCallErrorNumber=EPERM \
    --property='SystemCallFilter=~connect sendto sendmsg sendmmsg io_uring_setup io_uring_enter io_uring_register' \
    --property='InaccessiblePaths=-/var/lib/postgresql -/run/postgresql -/var/run/postgresql' \
    /bin/bash "$dir/helper" "$mode" "$@"
  status=$?
  set -e
  actual=$(systemctl show --value --property=ExecMainStatus "$unit" 2>/dev/null) || actual=unknown
  result=$(systemctl show --value --property=Result "$unit" 2>/dev/null) || result=unknown
  if [[ "$expected" == 0 ]]; then
    [[ "$status" == 0 && "$actual" == 0 && -f "$dir/observed" ]] || { echo "FAIL: $mode expected success; runner=$status; unit=$actual; result=$result" >&2; exit 1; }
  else
    [[ "$status" != 0 && "$actual" == "$expected" && ! -e "$dir/observed" ]] || { echo "FAIL: $mode expected exit $expected; runner=$status; unit=$actual; result=$result" >&2; exit 1; }
  fi
}

exec 9<"$dir/deploy.lock"
flock --exclusive --nonblock 9
run_case deploy 42 synthetic-release
run_case app 0
flock --unlock 9

exec 8<"$dir/activity.lock"
flock --exclusive --nonblock 8
run_case app 42
run_case storage 42 delta-sync
run_case health 42
flock --unlock 8

touch "$dir/maintenance-active"
run_case app 75
run_case storage 75 delta-sync
run_case health 75
run_case deploy 75 synthetic-release
rm -f -- "$dir/maintenance-active"

# Annual release cannot infer success from a malformed or incomplete marker.
# Every shape, including a dangling symlink, blocks every participant without
# starting its payload. The real annual commit/recovery matrix is separate in
# fixtures/maintenance-integration (this shell fixture has no database).
for marker_shape in incomplete dangling-symlink directory; do
  case "$marker_shape" in
    incomplete) printf 'incomplete-annual-release\n' > "$dir/maintenance-active" ;;
    dangling-symlink) ln -s -- "$dir/does-not-exist" "$dir/maintenance-active" ;;
    directory) mkdir -- "$dir/maintenance-active" ;;
  esac
  run_case app 75
  run_case storage 75 delta-sync
  run_case health 75
  run_case deploy 75 synthetic-annual-release
  if [[ "$marker_shape" == directory ]]; then rmdir -- "$dir/maintenance-active";
  else rm -f -- "$dir/maintenance-active"; fi
done

touch "$dir/recovery-required"
run_case app 0
run_case storage 75 delta-sync
run_case health 75
run_case deploy 75 synthetic-release
rm -f -- "$dir/recovery-required"
run_case storage 0 delta-sync

touch "$dir/participants-resume-required"
run_case app 0
run_case storage 0 delta-sync
run_case health 0
run_case deploy 75 synthetic-release
rm -f -- "$dir/participants-resume-required"

# A killed fixture owner must release its lock while leaving the marker that
# blocks participants. This is a host-lifecycle test, not a database commit test.
crash_unit="$prefix-crash.service"
units+=("$crash_unit")
systemd-run --quiet --unit "${crash_unit%.service}" --property=Type=exec \
  --property=PrivateNetwork=yes --property=NoNewPrivileges=yes --property=SystemCallArchitectures=native \
  --property=PrivateTmp=yes --property=ProtectSystem=strict --property="ReadWritePaths=$dir" \
  --property=RestrictAddressFamilies=AF_UNIX --property=SystemCallErrorNumber=EPERM \
  --property='SystemCallFilter=~connect sendto sendmsg sendmmsg io_uring_setup io_uring_enter io_uring_register' \
  --property='InaccessiblePaths=-/var/lib/postgresql -/run/postgresql -/var/run/postgresql' /bin/bash -c \
  'exec 8<"$1/activity.lock"; flock --exclusive --nonblock 8; touch "$1/maintenance-active"; exec /usr/bin/sleep 60' fixture "$dir"
for _ in {1..100}; do
  [[ -f "$dir/maintenance-active" ]] && break
  sleep 0.1
done
[[ -f "$dir/maintenance-active" ]] || { echo 'FAIL: crash fixture did not start' >&2; exit 1; }
systemctl kill --signal=KILL "$crash_unit"
for _ in {1..100}; do
  [[ $(systemctl show --value --property=MainPID "$crash_unit") == 0 ]] && break
  sleep 0.1
done
[[ $(systemctl show --value --property=MainPID "$crash_unit") == 0 ]] || { echo 'FAIL: crash fixture is still running' >&2; exit 1; }
flock --exclusive --nonblock 8 || { echo 'FAIL: killed owner retained gate' >&2; exit 1; }
flock --unlock 8
run_case app 75
echo 'PASS: participant gates, malformed markers and killed owner (no annual SQL qualification)'
