#!/usr/bin/env bash
# Review and verify this script and the three build artifacts externally before root execution.
# Synthetic filesystem/process test only: it does not access a database or working units.
set -Eeuo pipefail
[[ $(id -u) == 0 && $# == 4 ]] || { echo 'requires root, stage directory and three reviewed hashes' >&2; exit 64; }
stage=$1
[[ "$stage" =~ ^/run/gb-dotnet-stage[.][A-Za-z0-9]{8}$ && ! -L "$stage" && $(realpath -e -- "$stage") == "$stage" ]] || exit 64
[[ $(stat -c '%u:%a' -- "$stage") == 0:700 ]] || exit 1
shopt -s nullglob dotglob
stage_items=("$stage"/*)
[[ ${#stage_items[@]} == 3 ]] || { echo 'stage contains unexpected entries' >&2; exit 1; }
for item in "${stage_items[@]}"; do
  case "${item##*/}" in MaintenanceFixture.dll|MaintenanceFixture.deps.json|MaintenanceFixture.runtimeconfig.json) ;; *) exit 1 ;; esac
done
for hash in "$2" "$3" "$4"; do [[ "$hash" =~ ^[a-f0-9]{64}$ ]] || exit 64; done
artifacts=(MaintenanceFixture.dll MaintenanceFixture.deps.json MaintenanceFixture.runtimeconfig.json)
expected=("$2" "$3" "$4")
for index in 0 1 2; do
  file="$stage/${artifacts[$index]}"
  [[ -f "$file" && ! -L "$file" && $(stat -c '%u:%h' -- "$file") == 0:1 ]] || exit 1
  [[ $(sha256sum -- "$file" | cut -d' ' -f1) == "${expected[$index]}" ]] || exit 1
done
[[ -x /usr/bin/dotnet ]] || { echo 'dotnet unavailable' >&2; exit 1; }
prefix="gb-dotnet-fixture-$(cat /proc/sys/kernel/random/uuid)"
dir=$(mktemp -d /run/gb-barrier-dotnet.XXXXXXXX)
[[ "$dir" =~ ^/run/gb-barrier-dotnet[.][A-Za-z0-9]{8}$ ]] || exit 1
printf 'fixturePrefix=%s\nfixtureDirectory=%s\n' "$prefix" "$dir"
units=()
runner=''
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
      printf 'fixtureUnitCleanupUncertain=%s\n' "$unit" >&2; failed=1
    fi
  done
  if [[ -n "$runner" ]] && jobs -pr | grep -Fxq -- "$runner"; then
    kill "$runner" >/dev/null 2>&1 || true
    wait "$runner" 2>/dev/null || true
  fi
  if (( failed != 0 )); then printf 'fixtureDirectoryRetained=%s\n' "$dir" >&2; exit 1; fi
  rm -f -- "$dir/deploy.lock" "$dir/activity.lock" "$dir/maintenance-active" "$dir/recovery-required" "$dir/participants-resume-required" "$dir/operation-progress" "$dir/phase" "$dir/holder-ready" "$dir/waiting-activity" "$dir/operation-entered"
  if ! rmdir -- "$dir"; then printf 'fixtureDirectoryCleanupFailed=%s\n' "$dir" >&2; exit 1; fi
  exit "$original"
}
trap cleanup EXIT
touch "$dir/deploy.lock" "$dir/activity.lock"
chmod 0600 "$dir/deploy.lock" "$dir/activity.lock"

start_unit() {
  local mode=$1 unit="$prefix-$1.service"
  local -a wait_arg=()
  [[ "$mode" == success || "$mode" == concurrent ]] && wait_arg=(--wait)
  units+=("$unit")
  systemd-run --quiet "${wait_arg[@]}" --unit "${unit%.service}" --property=Type=exec \
    --property=RuntimeMaxSec=120s --property=TimeoutStopSec=10s \
    --property=PrivateNetwork=yes --property=NoNewPrivileges=yes --property=SystemCallArchitectures=native \
    --property=PrivateTmp=yes --property=ProtectSystem=strict --property="ReadWritePaths=$dir" \
    --property=RestrictAddressFamilies=AF_UNIX --property=SystemCallErrorNumber=EPERM \
    --property='SystemCallFilter=~connect sendto sendmsg sendmmsg io_uring_setup io_uring_enter io_uring_register' \
    --property='InaccessiblePaths=-/var/lib/postgresql -/run/postgresql -/var/run/postgresql' \
    --property=Environment=DOTNET_EnableDiagnostics=0 /usr/bin/dotnet "$stage/MaintenanceFixture.dll" "$mode" "$dir"
}

start_unit success
[[ ! -e "$dir/maintenance-active" && ! -e "$dir/recovery-required" && ! -e "$dir/participants-resume-required" ]] || { echo 'success left recovery state' >&2; exit 1; }

# Simulate an already-running sync participant retaining the activity gate.
# Only this fixture's transient service is stopped; no working unit is named.
holder="$prefix-holder.service"
units+=("$holder" "$prefix-concurrent.service")
systemd-run --quiet --unit "${holder%.service}" --property=Type=exec --property=RuntimeMaxSec=120s --property=TimeoutStopSec=10s \
  --property=PrivateNetwork=yes --property=NoNewPrivileges=yes --property=SystemCallArchitectures=native \
  --property=PrivateTmp=yes --property=ProtectSystem=strict --property="ReadWritePaths=$dir" \
  --property=RestrictAddressFamilies=AF_UNIX --property=SystemCallErrorNumber=EPERM \
  --property='SystemCallFilter=~connect sendto sendmsg sendmmsg io_uring_setup io_uring_enter io_uring_register' \
  --property='InaccessiblePaths=-/var/lib/postgresql -/run/postgresql -/var/run/postgresql' \
  /usr/bin/flock --shared --nonblock "$dir/activity.lock" /bin/bash -c 'touch "$1/holder-ready"; exec /usr/bin/sleep 90' fixture "$dir"
for _ in {1..200}; do [[ -f "$dir/holder-ready" ]] && break; sleep 0.1; done
[[ -f "$dir/holder-ready" ]] || { echo 'shared fixture participant did not start' >&2; exit 1; }
start_unit concurrent &
runner=$!
for _ in {1..200}; do
  if [[ -f "$dir/waiting-activity" && $(cat -- "$dir/waiting-activity" 2>/dev/null || true) == blocked ]]; then break; fi
  sleep 0.1
done
[[ -f "$dir/waiting-activity" && $(cat -- "$dir/waiting-activity") == blocked && -f "$dir/maintenance-active" ]] && kill -0 "$runner" 2>/dev/null || { echo 'coordinator did not observe a blocked activity gate' >&2; exit 1; }
sleep 1
[[ ! -e "$dir/operation-entered" ]] && systemctl is-active --quiet "$holder" || { echo 'operation entered while shared participant was active' >&2; exit 1; }
systemctl stop "$holder"
wait "$runner"
[[ -f "$dir/operation-entered" ]] || { echo 'operation did not enter after shared participant stopped' >&2; exit 1; }
[[ ! -e "$dir/maintenance-active" && ! -e "$dir/recovery-required" && ! -e "$dir/participants-resume-required" ]] || { echo 'concurrent case left recovery state' >&2; exit 1; }
rm -f -- "$dir/holder-ready" "$dir/waiting-activity" "$dir/operation-entered"

wait_for_phase() {
  local phase=$1
  for _ in {1..200}; do
    [[ -f "$dir/phase" && $(cat -- "$dir/phase") == "$phase" ]] && return 0
    sleep 0.1
  done
  echo "fixture phase timed out: $phase" >&2
  return 1
}

expect_lock() {
  local path=$1 expected=$2 status
  set +e
  flock --shared --nonblock --conflict-exit-code 42 "$path" /usr/bin/true
  status=$?
  set -e
  [[ "$status" == "$expected" ]] || { echo "fixture lock expected $expected, got $status" >&2; exit 1; }
}

expect_recovery_state() {
  local phase=$1 state_file
  local -a lines
  local encoded
  encoded=$(printf '%s' '{"HealthTimerWasActive":false,"StorageTimerWasActive":false,"ActiveStorageUnits":[]}' | base64 -w0)
  mapfile -t lines < "$dir/operation-progress"
  [[ ${#lines[@]} == 3 && "${lines[0]}" == 5aa5e64f-d46d-4af5-85a0-2cc016219477 && "${lines[1]}" == executing &&
     "${lines[2]}" == aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ]] || return 1
  if [[ "$phase" == after-receipt ]]; then
    state_file="$dir/recovery-required"
    mapfile -t lines < "$state_file"
    [[ ${#lines[@]} == 5 && "${lines[0]}" == 5aa5e64f-d46d-4af5-85a0-2cc016219477 && "${lines[1]}" == active &&
       "${lines[2]}" == aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa && "${lines[3]}" == "$encoded" && "${lines[4]}" == outcome=committed ]] || return 1
  else
    state_file="$dir/maintenance-active"
    mapfile -t lines < "$state_file"
    [[ ${#lines[@]} == 3 && "${lines[0]}" == 5aa5e64f-d46d-4af5-85a0-2cc016219477 && "${lines[1]}" == active && "${lines[2]}" == "$encoded" ]] || return 1
  fi
}

for phase in before-commit before-receipt after-receipt; do
  start_unit "$phase"
  wait_for_phase "$phase"
  expect_recovery_state "$phase" || { echo 'participant snapshot did not match the recorded recovery phase' >&2; exit 1; }
  expect_lock "$dir/deploy.lock" 42
  if [[ "$phase" == after-receipt ]]; then
    [[ ! -e "$dir/maintenance-active" && -f "$dir/recovery-required" ]] || exit 1
    expect_lock "$dir/activity.lock" 0
  else
    [[ -f "$dir/maintenance-active" && ! -e "$dir/recovery-required" ]] || exit 1
    expect_lock "$dir/activity.lock" 42
  fi
  systemctl kill --signal=KILL "$prefix-$phase.service"
  systemctl stop "$prefix-$phase.service"
  expect_lock "$dir/deploy.lock" 0
  expect_lock "$dir/activity.lock" 0
  if [[ "$phase" == after-receipt ]]; then [[ -f "$dir/recovery-required" ]] || exit 1
  else [[ -f "$dir/maintenance-active" ]] || exit 1; fi
  rm -f -- "$dir/maintenance-active" "$dir/recovery-required" "$dir/operation-progress" "$dir/phase"
done
echo 'PASS: linked host/coordinator gates and synthetic process-crash phases'
