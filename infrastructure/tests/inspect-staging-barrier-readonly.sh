#!/usr/bin/env bash
# Read-only inventory. It neither starts/stops units nor connects to a database.
set -Eeuo pipefail
export LC_ALL=C
[[ $(id -u) == 0 ]] || { echo 'root is required for complete process inventory' >&2; exit 1; }

check_unit() {
  local unit=$1 expected=$2 actual
  printf 'unit=%s\n' "$unit"
  systemctl show "$unit" --no-pager --property=LoadState --property=ActiveState \
    --property=SubState --property=MainPID --property=FragmentPath --property=DropInPaths
  actual=$(systemctl show "$unit" --no-pager --property=ExecStart --value)
  if [[ "$actual" == *"$expected"* ]]; then
    printf '%s\n' 'expectedExecStartSubstring=present'
  else
    printf '%s\n' 'expectedExecStartSubstring=absent'
  fi
}

count_processes() {
  local label=$1 pattern=$2 count status
  set +e
  count=$(pgrep -fc -- "$pattern")
  status=$?
  set -e
  if (( status > 1 )) || [[ ! "$count" =~ ^[0-9]+$ ]]; then
    printf 'processCountError=%s\n' "$label" >&2
    exit 1
  fi
  printf 'processCount_%s=%s\n' "$label" "$count"
}

check_unit garagebalance-staging.service '/usr/local/bin/garagebalance-maintenance-barrier app'
check_unit garagebalance-healthcheck.service '/usr/local/bin/garagebalance-maintenance-barrier health'
check_unit 'garagebalance-storage-tool@delta-sync.service' '/usr/local/bin/garagebalance-maintenance-barrier storage'
storage_units=$(systemctl list-units --all --type=service --plain --no-legend --no-pager 'garagebalance-storage-tool@*.service')
printf 'storageUnitCount=%s\n' "$(printf '%s\n' "$storage_units" | awk 'NF { count++ } END { print count+0 }')"
jobs=$(systemctl list-jobs --no-legend --no-pager)
printf 'garagebalanceJobCount=%s\n' "$(printf '%s\n' "$jobs" | awk '$2 ~ /^garagebalance-/ { count++ } END { print count+0 }')"
count_processes api '(^|[ /])GarageBalance[.]Api([.]dll)?([ ]|$)'
count_processes storage '(^|[ /])GarageBalance[.]StorageTool([.]dll)?([ ]|$)'
for file in /usr/local/bin/garagebalance-maintenance-barrier /usr/local/bin/garagebalance-deploy-apply; do
  if [[ -f "$file" && ! -L "$file" ]]; then
    sha256sum -- "$file"
  else
    printf 'fileUnavailable=%s\n' "$file"
  fi
done
for file in /var/lib/garagebalance-maintenance /var/lib/garagebalance-maintenance/deploy.lock /var/lib/garagebalance-maintenance/activity.lock \
  /var/lib/garagebalance-maintenance/maintenance-active /var/lib/garagebalance-maintenance/recovery-required /var/lib/garagebalance-maintenance/participants-resume-required; do
  if [[ -L "$file" ]]; then
    printf 'fileState=%s:symlink\n' "$file"
  elif [[ -e "$file" ]]; then
    printf 'fileState=%s:present:' "$file"
    stat -c '%u:%g:%a:%h:%d:%i' -- "$file"
  else
    printf 'fileState=%s:absent\n' "$file"
  fi
done
