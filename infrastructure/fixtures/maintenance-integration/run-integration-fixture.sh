#!/usr/bin/env bash
# Synthetic database + real API/StorageTool/lifecycle. No working credentials,
# database, service or storage path is exposed to a fixture process.
set -Eeuo pipefail
[[ $(id -u) == 0 && $# == 2 ]] || exit 64
package=$1
expected=$2
[[ "$package" =~ ^/opt/gb-maint-integration-package[.][a-f0-9]{8}$ && ! -L "$package" && $(realpath -e "$package") == "$package" ]] || exit 64
[[ "$expected" =~ ^[a-f0-9]{64}$ && -f "$package/evidence.sha256" && ! -L "$package/evidence.sha256" ]] || exit 64
[[ $(sha256sum "$package/evidence.sha256" | cut -d' ' -f1) == "$expected" ]] || exit 1
(cd "$package" && sha256sum --quiet -c evidence.sha256)
fixture="$package/publish/MaintenanceIntegrationFixture"
[[ -x "$fixture" && -x /usr/lib/postgresql/18/bin/initdb ]] || exit 1
id=$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')
prefix="gb-maint-integration-$id"
state=$(mktemp -d /run/gb-maint-integration.XXXXXXXX)
barrier=$(mktemp -d /run/gb-barrier-dotnet.XXXXXXXX)
data="/opt/gb-maint-integration-data.$id"
bin_dir="/opt/gb-maint-integration-bin.$id"
[[ "$state" =~ ^/run/gb-maint-integration[.][A-Za-z0-9]{8}$ && "$barrier" =~ ^/run/gb-barrier-dotnet[.][A-Za-z0-9]{8}$ && ! -e "$data" ]] || exit 1
[[ ! -e "$bin_dir" ]] || exit 1
printf 'integrationPrefix=%s\nintegrationState=%s\nintegrationBarrier=%s\nintegrationData=%s\nintegrationBin=%s\n' "$prefix" "$state" "$barrier" "$data" "$bin_dir"
chown root:postgres "$state"
chmod 0750 "$state"
chmod 0700 "$barrier"
install -d -o postgres -g postgres -m 0700 "$data"
install -d -o root -g root -m 0700 "$bin_dir"
install -d -o postgres -g postgres -m 0700 "$state/pg" "$state/pg/socket"
install -d -o root -g root -m 0700 "$state/backups" "$state/keys" "$state/import-work"
touch "$barrier/deploy.lock" "$barrier/activity.lock"
chown root:garagebalance "$barrier/deploy.lock" "$barrier/activity.lock"
chmod 0640 "$barrier/deploy.lock" "$barrier/activity.lock"
units=()
counter=0
pg_started=0
success=0
cleanup() {
  local original=$? unit state_value pid group uncertain=0
  trap - EXIT
  systemctl show "$prefix-api.service" --property=ActiveState --property=SubState --property=Result --property=ExecMainCode --property=ExecMainStatus > "$state/api-final-status.txt" 2>/dev/null || true
  for unit in "${units[@]}"; do
    systemctl stop "$unit" >/dev/null 2>&1 || true
    systemctl reset-failed "$unit" >/dev/null 2>&1 || true
    state_value=$(systemctl show "$unit" --property=ActiveState --value 2>/dev/null || true)
    group=$(systemctl show "$unit" --property=ControlGroup --value 2>/dev/null || true)
    if [[ "$state_value" != inactive && "$state_value" != failed && -n "$state_value" ]] || [[ -n "$group" ]]; then uncertain=1; fi
  done
  if (( pg_started )) || [[ -e "$data/cluster/postmaster.pid" ]]; then
    runuser -u postgres -- /usr/lib/postgresql/18/bin/pg_ctl -D "$data/cluster" -m fast -w stop >"$state/pg-stop.log" 2>&1 || uncertain=1
    [[ ! -e "$data/cluster/postmaster.pid" ]] || uncertain=1
  fi
  for unit in "$prefix-api.service" "$prefix-health.service" "$prefix-storage.service" "$prefix-health.timer" "$prefix-storage.timer" "$prefix-anchor.service"; do
    rm -f -- "/run/systemd/system/$unit"
  done
  systemctl daemon-reload
  if (( uncertain || ! success )); then
    printf 'integrationArtifactsRetained=%s,%s,%s,%s cleanupUncertain=%s\n' "$state" "$barrier" "$data" "$bin_dir" "$uncertain"
    exit 1
  fi
  [[ $(realpath -e "$state") == "$state" && $(realpath -e "$barrier") == "$barrier" && $(realpath -e "$data") == "$data" && $(realpath -e "$bin_dir") == "$bin_dir" ]] || exit 1
  rm -rf --one-file-system -- "$state" "$barrier" "$data" "$bin_dir"
  printf 'integrationCleanup=verified\n'
  exit "$original"
}
trap cleanup EXIT
runuser -u postgres -- /usr/lib/postgresql/18/bin/initdb -D "$data/cluster" --auth-local=trust --auth-host=reject --no-locale >"$state/pg-init.log" 2>&1
runuser -u postgres -- /usr/lib/postgresql/18/bin/pg_ctl -D "$data/cluster" -l "$data/postgres.log" -w start \
  -o "-c listen_addresses='' -k $state/pg/socket -p 55483 -c shared_buffers=16MB -c max_connections=20 -c max_wal_size=128MB -c min_wal_size=32MB" >"$state/pg-start.log" 2>&1
pg_started=1
python3 - "$package" "$state" "$barrier" "$prefix" "$fixture" "$bin_dir" <<'PY'
import os,sys
package,state,barrier,prefix,fixture,bin_dir=sys.argv[1:]
def write(name,content):
 p=bin_dir+'/'+name
 with open(p,'x') as f:f.write(content)
 os.chmod(p,0o700)
with open(package+'/scripts/staging-maintenance-barrier.sh') as f: wrapper=f.read()
wrapper=wrapper.replace('/var/lib/garagebalance-maintenance',barrier).replace('/opt/garagebalance-staging/api/GarageBalance.Api',bin_dir+'/api')
wrapper=wrapper.replace('/usr/local/bin/garagebalance-storage-tool-run',bin_dir+'/storage').replace('/usr/local/bin/garagebalance-healthcheck',bin_dir+'/healthcheck')
write('barrier',wrapper)
write('api', '#!/bin/bash\nset -euo pipefail\nexec "'+fixture+'" api\n')
write('storage', '#!/bin/bash\nset -euo pipefail\n[[ $# == 1 && "$1" == delta-sync ]] || exit 64\nexec "'+fixture+'" storage\n')
with open(package+'/scripts/garagebalance-healthcheck.sh') as f:health=f.read()
health=health.replace('/run/garagebalance-healthcheck.failures',state+'/health-failures').replace('127.0.0.1:3101','127.0.0.1:33101').replace('garagebalance-staging.service',prefix+'-api.service')
write('healthcheck',health)
write('native-verify','#!/bin/bash\nset -euo pipefail\ntouch "'+state+'/sync-inspecting"\nwhile [[ -f "'+state+'/sync-hold" ]]; do sleep 0.2; done\nexec /usr/lib/postgresql/18/bin/pg_restore "$@"\n')
with open(package+'/scripts/vps-apply-release.sh') as f:deploy=f.read()
write('deploy-denial-probe',deploy.replace('barrier_dir=/var/lib/garagebalance-maintenance','barrier_dir='+barrier,1))
PY
anchor="$prefix-anchor.service"
units+=("$anchor")
cat > "/run/systemd/system/$anchor" <<EOF
[Service]
Type=exec
ExecStart=/usr/bin/sleep 1800
PrivateNetwork=yes
RuntimeMaxSec=1800
EOF
for mode in api health storage; do
  unit="$prefix-$mode.service"
  units+=("$unit")
  type=oneshot
  participant=$mode
  [[ "$mode" == api ]] && type=simple
  [[ "$mode" == api ]] && participant=app
  [[ "$mode" == storage ]] && participant='storage delta-sync'
  cat > "/run/systemd/system/$unit" <<EOF
[Unit]
JoinsNamespaceOf=$anchor
After=$anchor
Requires=$anchor
[Service]
Type=$type
ExecStart=$bin_dir/barrier $participant
EnvironmentFile=$state/fixture.env
WorkingDirectory=$state
KillMode=control-group
TimeoutStopSec=20
Restart=no
PrivateNetwork=yes
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
ReadWritePaths=$state
InaccessiblePaths=-/opt/garagebalance-staging -/var/lib/garagebalance-staging -/var/lib/postgresql -/run/postgresql -/var/run/postgresql -/etc/garagebalance-staging.env -/etc/garagebalance-staging-cloud.env -/etc/garagebalance-staging-hostkey.env -/etc/garagebalance-staging-backup-layout.env
MemoryMax=700M
CPUQuota=100%
UMask=0077
StandardOutput=append:$state/$mode.log
StandardError=append:$state/$mode.log
EOF
done
for mode in health storage; do
  unit="$prefix-$mode.timer"
  units+=("$unit")
  cat > "/run/systemd/system/$unit" <<EOF
[Timer]
OnActiveSec=3600
Unit=$prefix-$mode.service
EOF
done
systemctl daemon-reload
systemctl start "$anchor"
properties=(
  --property=Type=exec --property=PrivateNetwork=yes --property="JoinsNamespaceOf=$anchor"
  --property=NoNewPrivileges=yes --property=ProtectSystem=strict --property=ProtectHome=yes
  --property="ReadWritePaths=$state" --property="ReadWritePaths=$barrier" --property="WorkingDirectory=$state"
  --property="EnvironmentFile=$state/fixture.env" --property=UMask=0077
  --property=MemoryMax=900M --property=CPUQuota=100% --property=RuntimeMaxSec=180s --property=TimeoutStopSec=20s
)
for protected_path in /opt/garagebalance-staging /var/lib/garagebalance-staging /var/lib/postgresql /run/postgresql /var/run/postgresql /etc/garagebalance-staging.env /etc/garagebalance-staging-cloud.env /etc/garagebalance-staging-hostkey.env /etc/garagebalance-staging-backup-layout.env; do
  properties+=("--property=InaccessiblePaths=-$protected_path")
done
run_fixture() {
  counter=$((counter+1))
  last_cli="$prefix-cli-$counter.service"
  units+=("$last_cli")
  systemd-run --quiet --wait --unit="${last_cli%.service}" "${properties[@]}" \
    --property="StandardOutput=append:$state/commands.log" --property="StandardError=append:$state/commands.log" "$fixture" "$@"
}
start_fault() {
  counter=$((counter+1))
  fault_unit="$prefix-fault-$counter.service"
  units+=("$fault_unit")
  systemd-run --quiet --unit="${fault_unit%.service}" "${properties[@]}" \
    --property="StandardOutput=append:$state/commands.log" --property="StandardError=append:$state/commands.log" "$fixture" execute "$1"
}
wait_file() {
  local path=$1 expected_value=${2:-}
  for _ in {1..300}; do
    if [[ -f "$path" ]] && { [[ -z "$expected_value" ]] || [[ $(cat "$path") == "$expected_value" ]]; }; then return 0; fi
    sleep 0.1
  done
  printf 'integrationWaitFailed=%s\n' "${path##*/}" >&2
  return 1
}
probe_deploy() {
  [[ -e "$barrier/maintenance-active" || -e "$barrier/recovery-required" || -e "$barrier/participants-resume-required" ]] || exit 1
  counter=$((counter+1))
  local unit="$prefix-deploy-$counter.service" code
  units+=("$unit")
  set +e
  systemd-run --quiet --wait --unit="${unit%.service}" "${properties[@]}" \
    --property=InaccessiblePaths=-/run/systemd/private --property=InaccessiblePaths=-/run/dbus/system_bus_socket \
    --property="StandardOutput=append:$state/deploy-probe.log" --property="StandardError=append:$state/deploy-probe.log" \
    /bin/bash "$bin_dir/deploy-denial-probe" fixture-denial-probe
  code=$?
  set -e
  [[ "$code" == 75 ]] || { printf 'integrationDeployDeniedUnexpectedStatus=%s\n' "$code"; exit 1; }
}
case_number=0
for scenario in success before-commit after-commit after-receipt after-start preparing; do
  case_number=$((case_number+1))
  database="garagebalance_it_protocol_$(printf '%08x' "$case_number")"
  /usr/lib/postgresql/18/bin/createdb -h "$state/pg/socket" -p 55483 -U postgres "$database"
  python3 - "$state" "$barrier" "$prefix" "$database" "$bin_dir" <<'PY'
import os,secrets,sys
state,barrier,prefix,database,bin_dir=sys.argv[1:]
v={
 'GB_FIXTURE_STATE':state,'GB_FIXTURE_BARRIER':barrier,'GB_FIXTURE_PREFIX':prefix,
 'ConnectionStrings__DefaultConnection':f'Host={state}/pg/socket;Port=55483;Database={database};Username=postgres;Pooling=false',
 'ASPNETCORE_ENVIRONMENT':'Testing','ASPNETCORE_URLS':'http://127.0.0.1:33101','AllowedHosts':'sgk.blagodaty.ru',
 'Jwt__SigningKey':secrets.token_hex(48),'HttpsRedirection__Enabled':'false','Database__ApplyMigrationsOnStartup':'false',
 'InitialAdministrator__Enabled':'false','DataProtection__KeysPath':state+'/keys','DiagnosticLogging__Enabled':'false',
 'DatabasePerformance__MinimumPoolSize':'0','DatabasePerformance__MaximumPoolSize':'8',
 'Finance__RegularAccrualAutomation__Enabled':'false','ImportProcessing__WorkDirectory':state+'/import-work',
 'DatabaseBackup__Enabled':'false','DatabaseBackup__AutomaticEnabled':'false','DatabaseBackup__CatchUpEnabled':'false',
 'DatabaseBackup__Directory':state+'/backups','DatabaseBackup__PgRestorePath':bin_dir+'/native-verify','RecoveryDrill__Enabled':'false',
 'Storage__Mode':'Single','Storage__TenantId':'garagebalance','Storage__Destinations__0__Id':'local-hot',
 'Storage__Destinations__0__Type':'LocalFileSystem','Storage__Destinations__0__RootPath':state+'/backups',
 'Storage__Destinations__0__FailureDomain':'fixture-local','Storage__Destinations__0__TenantId':'garagebalance',
 'Storage__Destinations__0__Capabilities__0':'Read','Storage__Destinations__0__Capabilities__1':'Write',
 'Storage__Destinations__0__Capabilities__2':'Stat','Storage__Destinations__0__Capabilities__3':'Delete',
 'Storage__Pools__0__Id':'backups','Storage__Pools__0__DestinationIds__0':'local-hot','Storage__Policies__0__Id':'backup-policy',
 'Storage__Policies__0__DataClass':'DatabaseBackup','Storage__Policies__0__PoolId':'backups',
 'Storage__Policies__0__RequiredIndependentCopies':'1','Storage__Policies__0__MinimumOffsiteCopies':'0','Storage__Policies__0__DesiredCopies':'1'
}
os.makedirs(state+'/test-temp',mode=0o700,exist_ok=True)
v['TMPDIR']=state+'/test-temp'
with open(state+'/fixture.env','w') as f:
 for k,x in v.items():f.write(k+'='+x+'\n')
os.chmod(state+'/fixture.env',0o600)
PY
  run_fixture seed
  run_fixture sandbox
  if [[ "$scenario" == success ]]; then run_fixture local-files; printf 'integrationLocalRecoveryFiles=passed\n'; fi
  /usr/lib/postgresql/18/bin/pg_dump -h "$state/pg/socket" -p 55483 -U postgres -Fc -f "$state/backups/garagebalance_20261103-110000.pgdump" "$database"
  run_fixture storage-copy
  rm "$state/sync-inspecting"
  touch "$state/sync-hold"
  systemctl start "$prefix-api.service"
  run_fixture health
  systemctl start "$prefix-health.timer" "$prefix-storage.timer"
  systemctl start --no-block "$prefix-storage.service"
  wait_file "$state/sync-inspecting"
  if [[ "$scenario" == preparing ]]; then
    run_fixture stage preparing
    probe_deploy
    rm "$state/sync-hold"
    run_fixture abort
    run_fixture assert rolled-back
  else
    run_fixture stage
    [[ -e "$barrier/maintenance-active" && -f "$state/plan.json" ]]
    probe_deploy
    run_fixture backup
    rm "$state/sync-hold"
    if [[ "$scenario" == success ]]; then
      run_fixture execute
      run_fixture assert committed
    elif [[ "$scenario" == after-start ]]; then
      run_fixture execute after-start
      [[ -f "$barrier/recovery-required" && ! -e "$barrier/maintenance-active" ]]
      probe_deploy
      run_fixture recover
      run_fixture assert committed
    else
      start_fault "$scenario"
      wait_file "$state/phase" "$scenario"
      probe_deploy
      systemctl kill --kill-who=main --signal=KILL "$fault_unit"
      systemctl stop "$fault_unit"
      [[ $(systemctl show "$fault_unit" --property=MainPID --value) == 0 ]]
      if run_fixture execute; then echo 'integrationUnexpectedDeleteRetryAllowed' >&2; exit 1; fi
      run_fixture recover
      outcome=committed
      [[ "$scenario" == before-commit ]] && outcome=rolled-back
      run_fixture assert "$outcome"
    fi
  fi
  for _ in {1..200}; do
    current=$(systemctl show "$prefix-storage.service" --property=ActiveState --value)
    [[ "$current" != activating && "$current" != active ]] && break
    sleep 0.1
  done
  [[ "$current" != activating && "$current" != active ]] || { echo 'integrationRestoredSyncStillRunning' >&2; exit 1; }
  [[ $(systemctl show "$prefix-storage.service" --property=ExecMainStatus --value) == 0 ]] || { echo 'integrationRestoredSyncFailed' >&2; exit 1; }
  printf 'integrationScenario=%s:passed\n' "$scenario"
  systemctl stop "$prefix-health.timer" "$prefix-storage.timer" "$prefix-api.service" "$prefix-health.service" "$prefix-storage.service"
  /usr/lib/postgresql/18/bin/dropdb -h "$state/pg/socket" -p 55483 -U postgres --force "$database"
  rm -f -- "$state/plan.json" "$state/backup-receipt.json" "$state/precleanup.pgdump" "$state/operation-id" "$state/phase" "$state/sync-inspecting" "$state/storage-checkpoint.json" "$state/storage-checkpoint.json.lock"
  find "$state/backups" -maxdepth 1 -type f -delete
done
success=1
printf 'PASS: real API StorageTool PostgreSQL stage restore commit rollback lifecycle and crash recovery\n'
