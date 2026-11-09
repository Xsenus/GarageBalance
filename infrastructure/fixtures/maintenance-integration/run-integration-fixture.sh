#!/usr/bin/env bash
# Synthetic database + real API/StorageTool/lifecycle. No working credentials,
# database, service or storage path is exposed to a fixture process.
set -Eeuo pipefail
[[ $(uname -s) == Linux && $(id -u) == 0 && ( $# == 2 || $# == 3 ) ]] || exit 64
suite=${3:-selective-cleanup}
[[ "$suite" == selective-cleanup || "$suite" == annual-release ]] || exit 64
[[ -d /run/systemd/system ]] || { echo 'fixture requires running systemd' >&2; exit 1; }
pg_bin=${GB_FIXTURE_PG_BIN:-/usr/lib/postgresql/18/bin}
[[ "$pg_bin" =~ ^/usr/lib/postgresql/[0-9]+/bin$ && $(realpath -e "$pg_bin") == "$pg_bin" ]] || exit 64
for command in initdb pg_ctl pg_dump pg_restore createdb dropdb; do [[ -x "$pg_bin/$command" ]] || exit 1; done
package=$1
expected=$2
[[ "$package" =~ ^/opt/gb-maint-integration-package[.][a-f0-9]{8}$ && ! -L "$package" && $(realpath -e "$package") == "$package" ]] || exit 64
[[ "$expected" =~ ^[a-f0-9]{64}$ && -f "$package/evidence.sha256" && ! -L "$package/evidence.sha256" ]] || exit 64
[[ $(sha256sum "$package/evidence.sha256" | cut -d' ' -f1) == "$expected" ]] || exit 1
(cd "$package" && sha256sum --quiet -c evidence.sha256)
fixture="$package/publish/MaintenanceIntegrationFixture"
[[ -x "$fixture" ]] || exit 1
[[ "$suite" != annual-release || -f "$package/annual-release-cases.sh" ]] || exit 1
[[ -f "$package/cleanup-integration-fixture.sh" && ! -L "$package/cleanup-integration-fixture.sh" ]] || exit 1
id=$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')
prefix="gb-maint-integration-$id"
data="/opt/gb-maint-integration-data.$id"
bin_dir="/opt/gb-maint-integration-bin.$id"
registry="$package/active-fixture"
[[ ! -e "$data" && ! -L "$data" && ! -e "$bin_dir" && ! -L "$bin_dir" ]] || exit 1
[[ ! -e "$registry" && ! -L "$registry" ]] || { echo 'fixture registry already exists; run reviewed cleanup first' >&2; exit 1; }
state=''
barrier=''
registry_claimed=0
registry_ready=0
cleanup_bootstrap() {
  local original=$? directory
  trap - EXIT
  if (( registry_ready )); then
    /bin/bash "$package/cleanup-integration-fixture.sh" "$package" "$expected" || original=1
  else
    for directory in "$state" "$barrier"; do
      [[ -n "$directory" ]] || continue
      [[ "$directory" =~ ^/run/(gb-maint-integration|gb-barrier-dotnet)[.][A-Za-z0-9]{8}$ &&
         ! -L "$directory" && $(realpath -e "$directory") == "$directory" ]] || exit 1
      rmdir -- "$directory" || original=1
    done
    if (( registry_claimed )); then rm -f -- "$registry" || original=1; fi
  fi
  exit "$original"
}
trap cleanup_bootstrap EXIT
trap 'exit 143' TERM
trap 'exit 130' INT
(umask 077; set -o noclobber; : > "$registry")
registry_claimed=1
state=$(mktemp -d /run/gb-maint-integration.XXXXXXXX)
barrier=$(mktemp -d /run/gb-barrier-dotnet.XXXXXXXX)
[[ "$state" =~ ^/run/gb-maint-integration[.][A-Za-z0-9]{8}$ && "$barrier" =~ ^/run/gb-barrier-dotnet[.][A-Za-z0-9]{8}$ ]] || exit 1
printf '%s\n' "$prefix" "$state" "$barrier" "$data" "$bin_dir" "$pg_bin" > "$registry"
registry_ready=1
printf 'integrationPrefix=%s\nintegrationState=%s\nintegrationBarrier=%s\nintegrationData=%s\nintegrationBin=%s\n' "$prefix" "$state" "$barrier" "$data" "$bin_dir"
chown root:postgres "$state"
chmod 0750 "$state"
chmod 0700 "$barrier"
install -d -o postgres -g postgres -m 0700 "$data"
install -d -o root -g root -m 0700 "$bin_dir"
install -d -o postgres -g postgres -m 0700 "$state/pg" "$state/pg/socket"
install -d -o root -g root -m 0700 "$state/backups" "$state/keys" "$state/import-work"
touch "$barrier/deploy.lock" "$barrier/activity.lock"
chown root:root "$barrier/deploy.lock" "$barrier/activity.lock"
chmod 0640 "$barrier/deploy.lock" "$barrier/activity.lock"
units=()
counter=0
pg_started=0
success=0
cleanup() {
  local original=$? unit load state_value pid group uncertain=0
  trap - EXIT
  systemctl show "$prefix-api.service" --property=ActiveState --property=SubState --property=Result --property=ExecMainCode --property=ExecMainStatus > "$state/api-final-status.txt" 2>/dev/null || true
  # Stop queued owners and timers together before checking any individual unit;
  # a still-running timer/recovery owner must not restart a previously checked API.
  if (( ${#units[@]} )); then systemctl stop "${units[@]}" >/dev/null 2>&1 || true; fi
  for unit in "${units[@]}"; do
    systemctl stop "$unit" >/dev/null 2>&1 || true
    systemctl reset-failed "$unit" >/dev/null 2>&1 || true
    load=$(systemctl show "$unit" --property=LoadState --value 2>/dev/null || true)
    [[ "$load" != not-found ]] || continue
    [[ "$load" == loaded ]] || { uncertain=1; continue; }
    if ! state_value=$(systemctl show "$unit" --property=ActiveState --value 2>/dev/null) ||
       ! pid=$(systemctl show "$unit" --property=MainPID --value 2>/dev/null) ||
       ! group=$(systemctl show "$unit" --property=ControlGroup --value 2>/dev/null); then uncertain=1; continue; fi
    if [[ "$state_value" != inactive && "$state_value" != failed ]] || [[ -n "$group" || ( -n "$pid" && "$pid" != 0 ) ]]; then uncertain=1; fi
  done
  if (( pg_started )) || [[ -e "$data/cluster/postmaster.pid" ]]; then
    runuser -u postgres -- "$pg_bin"/pg_ctl -D "$data/cluster" -m fast -w stop >"$state/pg-stop.log" 2>&1 || uncertain=1
    [[ ! -e "$data/cluster/postmaster.pid" ]] || uncertain=1
  fi
  if (( uncertain )); then
    printf 'integrationArtifactsRetained=%s,%s,%s,%s cleanupUncertain=%s\n' "$state" "$barrier" "$data" "$bin_dir" "$uncertain"
    exit 1
  fi
  for unit in "$prefix-api.service" "$prefix-health.service" "$prefix-storage.service" "$prefix-health.timer" "$prefix-storage.timer" "$prefix-anchor.service"; do
    rm -f -- "/run/systemd/system/$unit"
  done
  systemctl daemon-reload
  if (( ! success )); then
    printf 'integrationFailed=1 (synthetic diagnostics follow)\n' >&2
    tail -n 60 "$state/commands.log" "$state/api.log" "$state/api-final-status.txt" 2>/dev/null >&2 || true
    [[ "$original" != 0 ]] || original=1
  fi
  [[ $(realpath -e "$state") == "$state" && $(realpath -e "$barrier") == "$barrier" && $(realpath -e "$data") == "$data" && $(realpath -e "$bin_dir") == "$bin_dir" ]] || exit 1
  rm -rf --one-file-system -- "$state" "$barrier" "$data" "$bin_dir"
  rm -- "$registry"
  printf 'integrationCleanup=verified\n'
  exit "$original"
}
trap cleanup EXIT
trap 'exit 143' TERM
trap 'exit 130' INT
runuser -u postgres -- "$pg_bin"/initdb -D "$data/cluster" --auth-local=trust --auth-host=reject --no-locale --encoding=UTF8 >"$state/pg-init.log" 2>&1
runuser -u postgres -- "$pg_bin"/pg_ctl -D "$data/cluster" -l "$data/postgres.log" -w start \
  -o "-c listen_addresses='' -k $state/pg/socket -p 55483 -c shared_buffers=16MB -c max_connections=20 -c max_wal_size=128MB -c min_wal_size=32MB" >"$state/pg-start.log" 2>&1
pg_started=1
python3 - "$package" "$state" "$barrier" "$prefix" "$fixture" "$bin_dir" "$pg_bin" <<'PY'
import os,sys
package,state,barrier,prefix,fixture,bin_dir,pg_bin=sys.argv[1:]
def write(name,content):
 p=bin_dir+'/'+name
 with open(p,'x') as f:f.write(content)
 os.chmod(p,0o700)
with open(package+'/scripts/staging-maintenance-barrier.sh') as f: wrapper=f.read()
wrapper=wrapper.replace('/var/lib/garagebalance-maintenance',barrier).replace('/opt/garagebalance-staging/api/GarageBalance.Api',bin_dir+'/api')
wrapper=wrapper.replace('/usr/local/bin/garagebalance-storage-tool-run',bin_dir+'/storage').replace('/usr/local/bin/garagebalance-healthcheck',bin_dir+'/healthcheck')
wrapper=wrapper.replace('/usr/local/bin/garagebalance-deploy-apply',bin_dir+'/unexpected-deploy')
if any(path in wrapper for path in ('/opt/garagebalance-staging','/usr/local/bin/garagebalance-','/var/lib/garagebalance-maintenance')):
 raise SystemExit('fixture wrapper retains working path')
write('unexpected-deploy', '#!/bin/bash\nprintf "unexpected deploy executed\\n" >&2\nexit 99\n')
write('barrier',wrapper)
write('api', '#!/bin/bash\nset -euo pipefail\nexec "'+fixture+'" api\n')
write('storage', '#!/bin/bash\nset -euo pipefail\n[[ $# == 1 && "$1" == delta-sync ]] || exit 64\nexec "'+fixture+'" storage\n')
with open(package+'/scripts/garagebalance-healthcheck.sh') as f:health=f.read()
health=health.replace('/run/garagebalance-healthcheck.failures',state+'/health-failures').replace('127.0.0.1:3101','127.0.0.1:33101').replace('garagebalance-staging.service',prefix+'-api.service')
write('healthcheck',health)
write('native-verify','#!/bin/bash\nset -euo pipefail\nprintf "verify\\n" >> "'+state+'/native-verify-invocations"\ntouch "'+state+'/sync-inspecting"\nwhile [[ -f "'+state+'/sync-hold" ]]; do sleep 0.2; done\nexec '+pg_bin+'/pg_restore "$@"\n')
with open(package+'/scripts/vps-apply-release.sh') as f:deploy=f.read()
boundary='\nAPP_ROOT="/opt/garagebalance-staging"\n'
if deploy.count(boundary)!=1:raise SystemExit('fixture deploy boundary changed')
# Exercise the actual gate preamble, never any deployment payload. Fixture locks
# are root:root; no production user/group is created or required by these tests.
deploy=deploy.split(boundary,1)[0]
for old,new in (
 ('barrier_dir=/var/lib/garagebalance-maintenance','barrier_dir='+barrier),
 ('install -d -o root -g garagebalance','install -d -o root -g root'),
 ('chown root:garagebalance','chown root:root'),
 ('id -g garagebalance','id -g root')):
 if deploy.count(old)!=1:raise SystemExit('fixture deploy identity mapping changed: '+old)
 deploy=deploy.replace(old,new,1)
if 'garagebalance' in deploy:raise SystemExit('fixture deploy retains production identity')
write('deploy-denial-probe',deploy+'\necho "unexpected deployment payload reached" >&2\nexit 99\n')
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
InaccessiblePaths=-/opt/garagebalance-staging -/var/lib/garagebalance-staging -/var/lib/garagebalance-maintenance -/var/lib/postgresql -/run/postgresql -/var/run/postgresql -/etc/garagebalance-staging.env -/etc/garagebalance-staging-cloud.env -/etc/garagebalance-staging-hostkey.env -/etc/garagebalance-staging-backup-layout.env
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
  delay=3600
  [[ "$mode" == storage ]] && delay=1
  cat > "/run/systemd/system/$unit" <<EOF
[Timer]
OnActiveSec=$delay
AccuracySec=1ms
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
for protected_path in /opt/garagebalance-staging /var/lib/garagebalance-staging /var/lib/garagebalance-maintenance /var/lib/postgresql /run/postgresql /var/run/postgresql /etc/garagebalance-staging.env /etc/garagebalance-staging-cloud.env /etc/garagebalance-staging-hostkey.env /etc/garagebalance-staging-backup-layout.env; do
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
    --property="StandardOutput=append:$state/commands.log" --property="StandardError=append:$state/commands.log" "$fixture" "${2:-execute}" "$1"
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
configure_fixture() {
  local database=$1
  python3 - "$state" "$barrier" "$prefix" "$database" "$bin_dir" "$pg_bin" <<'PY'
import os,secrets,sys
state,barrier,prefix,database,bin_dir,pg_bin=sys.argv[1:]
v={
 'GB_FIXTURE_PG_BIN':pg_bin,'GB_FIXTURE_STATE':state,'GB_FIXTURE_BARRIER':barrier,'GB_FIXTURE_PREFIX':prefix,
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
}
if [[ "$suite" == annual-release ]]; then
  source "$package/annual-release-cases.sh"
  success=1
  printf 'PASS: annual release real PostgreSQL/systemd protocol, failure, concurrency and read-only recovery\n'
  exit 0
fi
case_number=0
for scenario in success before-commit after-commit after-receipt after-start preparing; do
  case_number=$((case_number+1))
  database="garagebalance_it_protocol_$(printf '%08x' "$case_number")"
  "$pg_bin"/createdb -h "$state/pg/socket" -p 55483 -U postgres "$database"
  configure_fixture "$database"
  run_fixture seed
  run_fixture sandbox
  if [[ "$scenario" == success ]]; then run_fixture local-files; printf 'integrationLocalRecoveryFiles=passed\n'; fi
  "$pg_bin"/pg_dump -h "$state/pg/socket" -p 55483 -U postgres -Fc -f "$state/backups/garagebalance_20261103-110000.pgdump" "$database"
  run_fixture storage-copy
  rm "$state/sync-inspecting"
  touch "$state/sync-hold"
  systemctl start "$prefix-api.service"
  run_fixture health
  systemctl start "$prefix-health.timer" "$prefix-storage.timer"
  wait_file "$state/sync-inspecting"
  [[ $(systemctl show "$prefix-storage.timer" --property=SubState --value) == running ]] || { echo 'integrationStorageTimerNotRunning' >&2; exit 1; }
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
  "$pg_bin"/dropdb -h "$state/pg/socket" -p 55483 -U postgres --force "$database"
  rm -f -- "$state/plan.json" "$state/backup-receipt.json" "$state/precleanup.pgdump" "$state/operation-id" "$state/phase" "$state/sync-inspecting" "$state/storage-checkpoint.json" "$state/storage-checkpoint.json.lock"
  find "$state/backups" -maxdepth 1 -type f -delete
done
success=1
printf 'PASS: real API StorageTool PostgreSQL stage restore commit rollback lifecycle and crash recovery\n'
