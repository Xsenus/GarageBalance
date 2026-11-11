#!/usr/bin/env bash
# Idempotent CI cancellation cleanup. Operates only on the exact registry written
# by the reviewed harness; no wildcards are used for filesystem removal.
set -Eeuo pipefail
[[ $(uname -s) == Linux && $(id -u) == 0 && $# == 2 ]] || exit 64
package=$1
expected=$2
[[ "$package" =~ ^/opt/gb-maint-integration-package[.][a-f0-9]{8}$ && ! -L "$package" && $(realpath -e "$package") == "$package" ]] || exit 64
[[ "$expected" =~ ^[a-f0-9]{64}$ && $(sha256sum "$package/evidence.sha256" | cut -d' ' -f1) == "$expected" ]] || exit 64
(cd "$package" && sha256sum --quiet -c evidence.sha256)
registry="$package/active-fixture"
[[ -e "$registry" || -L "$registry" ]] || exit 0
[[ -f "$registry" && ! -L "$registry" && $(stat -c '%u:%a:%h' "$registry") == 0:600:1 ]] || exit 1
mapfile -t record < "$registry"
[[ ${#record[@]} == 6 ]] || exit 1
prefix=${record[0]}
state=${record[1]}
barrier=${record[2]}
data=${record[3]}
bin_dir=${record[4]}
pg_bin=${record[5]}
[[ "$prefix" =~ ^gb-maint-integration-[a-f0-9]{8}$ &&
   "$state" =~ ^/run/gb-maint-integration[.][A-Za-z0-9]{8}$ &&
   "$barrier" =~ ^/run/gb-barrier-dotnet[.][A-Za-z0-9]{8}$ &&
   "$data" == "/opt/gb-maint-integration-data.${prefix##*-}" &&
   "$bin_dir" == "/opt/gb-maint-integration-bin.${prefix##*-}" &&
   "$pg_bin" =~ ^/usr/lib/postgresql/[0-9]+/bin$ && $(realpath -e "$pg_bin") == "$pg_bin" ]] || exit 1
for directory in "$state" "$barrier" "$data" "$bin_dir"; do
  [[ ! -L "$directory" ]] || exit 1
  [[ ! -e "$directory" || ( -d "$directory" && $(realpath -e "$directory") == "$directory" ) ]] || exit 1
done
units=()
listing=$(systemctl list-units --all --plain --no-legend --no-pager "$prefix-*.service" "$prefix-*.timer") || exit 1
while read -r unit _; do
  [[ -n "$unit" ]] || continue
  [[ "$unit" =~ ^${prefix}-(api|health|storage|anchor|cli-[0-9]+|fault-[0-9]+|deploy-[0-9]+)[.](service|timer)$ ]] || exit 1
  units+=("$unit")
done <<< "$listing"
if (( ${#units[@]} )); then
  systemctl stop "${units[@]}" || true
  for unit in "${units[@]}"; do
    load=$(systemctl show "$unit" --property=LoadState --value 2>/dev/null || true)
    [[ "$load" != not-found ]] || continue
    [[ "$load" == loaded ]] || exit 1
    state_value=$(systemctl show "$unit" --property=ActiveState --value)
    pid=$(systemctl show "$unit" --property=MainPID --value)
    group=$(systemctl show "$unit" --property=ControlGroup --value)
    [[ "$state_value" == inactive || "$state_value" == failed ]] || exit 1
    [[ -z "$pid" || "$pid" == 0 ]] || exit 1
    [[ -z "$group" ]] || exit 1
    systemctl reset-failed "$unit" >/dev/null 2>&1 || true
  done
fi
if [[ -e "$data/cluster/postmaster.pid" ]]; then
  [[ ! -L "$data/cluster" && $(realpath -e "$data/cluster") == "$data/cluster" ]] || exit 1
  runuser -u postgres -- "$pg_bin/pg_ctl" -D "$data/cluster" -m fast -w stop
  [[ ! -e "$data/cluster/postmaster.pid" ]] || exit 1
fi
for name in api.service health.service storage.service health.timer storage.timer anchor.service; do
  unit_file="/run/systemd/system/$prefix-$name"
  [[ ! -L "$unit_file" ]] || exit 1
  rm -f -- "$unit_file"
done
systemctl daemon-reload
for directory in "$state" "$barrier" "$data" "$bin_dir"; do
  [[ ! -e "$directory" ]] || rm -rf --one-file-system -- "$directory"
done
rm -- "$registry"
printf 'integrationCancellationCleanup=verified\n'
