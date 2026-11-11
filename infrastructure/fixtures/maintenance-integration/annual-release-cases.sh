#!/usr/bin/env bash
# Sourced only by the reviewed isolated harness, never a standalone/live operation.
[[ ${suite:-} == annual-release && ${prefix:-} =~ ^gb-maint-integration-[a-f0-9]{8}$ &&
   ${state:-} =~ ^/run/gb-maint-integration[.][A-Za-z0-9]{8}$ &&
   ${barrier:-} =~ ^/run/gb-barrier-dotnet[.][A-Za-z0-9]{8}$ ]] || exit 64

annual_call_count() {
  if [[ -f "$state/annual-release-calls" ]]; then wc -l < "$state/annual-release-calls"; else printf '0\n'; fi
}
expect_annual_denied() {
  local before after
  before=$(annual_call_count)
  if run_fixture annual-execute; then echo 'FAIL: annual release retry was allowed' >&2; exit 1; fi
  after=$(annual_call_count)
  [[ "$before" == "$after" ]] || { echo 'FAIL: denied retry invoked annual release' >&2; exit 1; }
}
probe_annual_participants() {
  local unit state_value status pid
  # Queue actual starts together while the owner is paused or a durable marker remains.
  for unit in "$prefix-api.service" "$prefix-health.service" "$prefix-storage.service"; do
    systemctl reset-failed "$unit" >/dev/null 2>&1 || true
    systemctl start --no-block "$unit"
  done
  for unit in "$prefix-api.service" "$prefix-health.service" "$prefix-storage.service"; do
    for _ in {1..100}; do
      state_value=$(systemctl show "$unit" --property=ActiveState --value)
      [[ "$state_value" != activating && "$state_value" != active ]] && break
      sleep 0.1
    done
    status=$(systemctl show "$unit" --property=ExecMainStatus --value)
    pid=$(systemctl show "$unit" --property=MainPID --value)
    [[ "$state_value" == failed && "$pid" == 0 && ( "$status" == 75 || "$status" == 1 ) ]] || {
      printf 'FAIL: competing participant %s state=%s pid=%s status=%s\n' "$unit" "$state_value" "$pid" "$status" >&2; exit 1;
    }
    systemctl reset-failed "$unit"
  done
  probe_deploy
}
kill_annual_owner() {
  systemctl kill --kill-who=main --signal=KILL "$fault_unit"
  systemctl stop "$fault_unit"
  [[ $(systemctl show "$fault_unit" --property=MainPID --value) == 0 ]]
  [[ -z $(systemctl show "$fault_unit" --property=ControlGroup --value) ]]
}
finish_annual_case() {
  local current
  # Recovery restarts the exact original timer/service participants. Confirm that
  # the restored storage work completed, then stop only this fixture's units.
  if [[ "$scenario" != unknown-outcome && "$scenario" != corrupt-receipt ]]; then
    for _ in {1..100}; do
      (( $(wc -l < "$state/native-verify-invocations") > native_before )) && break
      sleep 0.1
    done
    (( $(wc -l < "$state/native-verify-invocations") > native_before )) || { echo 'FAIL: recorded storage job/timer did not run again' >&2; exit 1; }
  else
    [[ $(wc -l < "$state/native-verify-invocations") == "$native_before" ]] || { echo 'FAIL: unresolved outcome resumed storage' >&2; exit 1; }
  fi
  for _ in {1..200}; do
    current=$(systemctl show "$prefix-storage.service" --property=ActiveState --value)
    [[ "$current" != activating && "$current" != active ]] && break
    sleep 0.1
  done
  [[ "$current" != activating && "$current" != active ]] || { echo 'FAIL: restored storage still running' >&2; exit 1; }
  if [[ "$scenario" != unknown-outcome && "$scenario" != corrupt-receipt ]]; then
    [[ $(systemctl show "$prefix-storage.service" --property=ExecMainStatus --value) == 0 ]] || {
      echo 'FAIL: restored storage failed' >&2; exit 1;
    }
  fi
  systemctl stop "$prefix-health.timer" "$prefix-storage.timer" "$prefix-api.service" "$prefix-health.service" "$prefix-storage.service"
  "$pg_bin/dropdb" -h "$state/pg/socket" -p 55483 -U postgres --force "$database"
  # This is disposal of the already stopped synthetic case, not a recovery path.
  rm -f -- "$barrier/maintenance-active" "$barrier/recovery-required" "$barrier/participants-resume-required" "$barrier/operation-progress"
  rm -f -- "$state/annual-plan.json" "$state/annual-preserved.json" "$state/annual-pre-release.pgdump" \
    "$state/annual-release-calls" "$state/backup-receipt.json" "$state/phase" "$state/sync-inspecting" \
    "$state/storage-checkpoint.json" "$state/storage-checkpoint.json.lock" "$state/native-verify-invocations"
  find "$state/backups" -maxdepth 1 -type f -delete
}

case_number=0
for scenario in success before-release before-commit after-commit after-receipt after-start \
    lost-lock lost-lock-before-commit marker-create marker-remove receipt-create outcome-read unknown-outcome corrupt-receipt; do
  printf 'integrationAnnualScenarioStart=%s\n' "$scenario"
  case_number=$((case_number+1))
  database="garagebalance_it_protocol_$(printf '%08x' "$case_number")"
  "$pg_bin/createdb" -h "$state/pg/socket" -p 55483 -U postgres "$database"
  configure_fixture "$database"
  run_fixture annual-seed
  run_fixture sandbox
  "$pg_bin/pg_dump" -h "$state/pg/socket" -p 55483 -U postgres -Fc -f "$state/backups/garagebalance_20261103-110000.pgdump" "$database"
  run_fixture storage-copy
  rm "$state/sync-inspecting"
  touch "$state/sync-hold"
  systemctl start "$prefix-api.service"
  run_fixture health
  systemctl start "$prefix-health.timer" "$prefix-storage.timer"
  wait_file "$state/sync-inspecting"
  native_before=$(wc -l < "$state/native-verify-invocations")
  [[ $(systemctl show "$prefix-storage.timer" --property=SubState --value) == running ]]
  run_fixture annual-preview
  run_fixture annual-backup
  # A real running storage job remains blocked in its private verifier until the
  # protocol stops/drains it. It is released for the later restoration only.
  outcome=committed
  if [[ "$scenario" == success ]]; then
    run_fixture annual-execute
    rm "$state/sync-hold"
    run_fixture annual-assert committed
  elif [[ "$scenario" == before-release || "$scenario" == before-commit || "$scenario" == after-commit || "$scenario" == after-receipt || "$scenario" == unknown-outcome ]]; then
    phase=$scenario
    [[ "$scenario" == unknown-outcome ]] && phase=after-commit
    start_fault "$phase" annual-execute
    wait_file "$state/phase" "$phase"
    probe_deploy
    if [[ "$scenario" == before-release ]]; then
      probe_annual_participants
      expect_annual_denied
    fi
    [[ "$scenario" != unknown-outcome ]] || run_fixture annual-corrupt-outcome
    kill_annual_owner
    rm "$state/sync-hold"
    expect_annual_denied
    if [[ "$scenario" == unknown-outcome ]]; then
      if run_fixture annual-recover; then echo 'FAIL: unknown outcome recovered' >&2; exit 1; fi
      probe_annual_participants
      run_fixture annual-assert blocked
    else
      [[ "$scenario" != before-release && "$scenario" != before-commit ]] || outcome=rolled-back
      if [[ "$scenario" == after-receipt ]]; then
        run_fixture annual-recover-cancel
        probe_annual_participants
        expect_annual_denied
      fi
      run_fixture annual-recover
      run_fixture annual-assert "$outcome"
    fi
  else
    injected=$scenario
    [[ "$scenario" != corrupt-receipt ]] || injected=marker-remove
    if run_fixture annual-execute "$injected"; then echo "FAIL: fault $injected did not fail" >&2; exit 1; fi
    # A marker-create error happens before stop/drain. Stop the still-running
    # fixture API before probing rejected *new* starts; it has not released hold.
    if [[ "$scenario" == marker-create ]]; then
      systemctl stop "$prefix-api.service" "$prefix-health.timer" "$prefix-storage.timer" "$prefix-storage.service"
    fi
    rm "$state/sync-hold"
    expect_annual_denied
    if [[ "$scenario" == corrupt-receipt ]]; then
      [[ -f "$barrier/maintenance-active" && -f "$barrier/recovery-required" ]]
      printf 'synthetic-incomplete-receipt\n' > "$barrier/recovery-required"
      if run_fixture annual-recover; then echo 'FAIL: malformed receipt recovered' >&2; exit 1; fi
      probe_annual_participants
      run_fixture annual-assert blocked
    else
      [[ "$scenario" != lost-lock && "$scenario" != lost-lock-before-commit && "$scenario" != marker-create ]] || outcome=rolled-back
      [[ "$scenario" != lost-lock && "$scenario" != marker-create ]] || [[ $(annual_call_count) == 0 ]]
      run_fixture annual-recover
      run_fixture annual-assert "$outcome"
    fi
  fi
  if [[ "$scenario" != unknown-outcome && "$scenario" != corrupt-receipt ]]; then
    calls=$(annual_call_count)
    if run_fixture annual-recover; then echo 'FAIL: recovery accepted an already completed operation' >&2; exit 1; fi
    [[ $(annual_call_count) == "$calls" && ! -e "$barrier/maintenance-active" && ! -e "$barrier/recovery-required" ]]
    run_fixture annual-assert "$outcome"
  fi
  finish_annual_case
  printf 'integrationAnnualScenario=%s:passed\n' "$scenario"
done
