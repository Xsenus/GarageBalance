#!/usr/bin/env bash
# Staging cleanup of test records through the reviewed StorageTool commands
# (docs/accounting-cleanup-cli.md):
#   garage-history — archived garages together with their accruals, payments and fund deposits;
#   meter-history  — all meter readings and meter replacements, start values stay;
#   fund-balance   — deleted funds back to a zero balance after their history was changed.
# "preview" only writes a private plan; "cleanup" executes exactly the previewed plan after a verified backup.
set -Eeuo pipefail

APP_ROOT="/opt/garagebalance-staging"
ENV_FILE="/etc/garagebalance-staging.env"
SERVICE_NAME="garagebalance-staging.service"
PUBLIC_HOST="sgk.blagodaty.ru"
EXPECTED_DATABASE="garagebalance_staging"
TOOL="${APP_ROOT}/storage-tool/GarageBalance.StorageTool"
BACKUP_DIR="${APP_ROOT}/backups"
PRIVATE_DIR="/var/lib/garagebalance-staging/private-maintenance"
PLAN_MAX_AGE_MINUTES=1380
RESTORE_CHECK_DATABASE=""
SERVICE_STOPPED=0

log() {
  printf '%s %s\n' "$(date --iso-8601=seconds)" "$*"
}

fail() {
  log "recordsCleanupStatus=failed; reason=$*"
  exit 1
}

cleanup() {
  if [[ -n "$RESTORE_CHECK_DATABASE" ]]; then
    sudo -u postgres dropdb --if-exists --force "$RESTORE_CHECK_DATABASE" >/dev/null 2>&1 || true
  fi
  if [[ "$SERVICE_STOPPED" == "1" ]]; then
    systemctl start "$SERVICE_NAME" >/dev/null 2>&1 || true
  fi
}

trap cleanup EXIT

# Reads one JSON field with PostgreSQL, so the host needs no extra JSON tooling.
json_field() {
  local file="$1"
  local path="$2"
  (( $(stat --format=%s "$file") <= 200000 )) || fail "private JSON file is unexpectedly large"
  sudo -u postgres psql --no-psqlrc --set ON_ERROR_STOP=1 --tuples-only --no-align \
    --set "document=$(cat "$file")" --set "path=${path}" <<'SQL'
SELECT (:'document')::jsonb #>> string_to_array(:'path', ',');
SQL
}

[[ "$(id -u)" == "0" ]] || fail "script must run as root"
kind="${1:-}"
mode="${2:-}"
confirmation="${3:-}"
fingerprint="${4:-}"
case "$kind" in
  garage-history)
    PLAN_COMMAND="archived-garages-purge-plan"
    CLEANUP_COMMAND="archived-garages-purge"
    PREVIEW_CONFIRMATION="PREVIEW GARAGEBALANCE DELETED GARAGE HISTORY"
    CLEANUP_CONFIRMATION="DELETE GARAGEBALANCE DELETED GARAGE HISTORY"
    CLEANUP_REASON="Удаление архивных гаражей с финансовой историей по замечаниям заказчика (п. 4) и решению от 21.10.2026"
    ;;
  meter-history)
    PLAN_COMMAND="meter-history-plan"
    CLEANUP_COMMAND="meter-history-cleanup"
    PREVIEW_CONFIRMATION="PREVIEW GARAGEBALANCE METER HISTORY"
    CLEANUP_CONFIRMATION="DELETE GARAGEBALANCE METER HISTORY"
    CLEANUP_REASON="Очистка формы «Показания» после проверки замены счётчиков (п. 6) по решению от 21.10.2026"
    ;;
  fund-balance)
    PLAN_COMMAND="archived-fund-balance-plan"
    CLEANUP_COMMAND="archived-fund-balance-repair"
    PREVIEW_CONFIRMATION="PREVIEW GARAGEBALANCE ARCHIVED FUND BALANCE"
    CLEANUP_CONFIRMATION="REPAIR GARAGEBALANCE ARCHIVED FUND BALANCE"
    CLEANUP_REASON="Возврат удалённого фонда к нулевому остатку после удаления архивного гаража (п. 4) по решению от 21.10.2026"
    ;;
  *) fail "kind must be garage-history, meter-history or fund-balance" ;;
esac
case "$mode" in
  preview) [[ "$#" == "3" && "$confirmation" == "$PREVIEW_CONFIRMATION" ]] || fail "exact preview confirmation is required" ;;
  cleanup)
    [[ "$#" == "4" && "$confirmation" == "$CLEANUP_CONFIRMATION" ]] || fail "exact cleanup confirmation is required"
    [[ "$fingerprint" =~ ^[0-9a-f]{64}$ ]] || fail "plan fingerprint must be 64 lowercase hexadecimal characters"
    ;;
  *) fail "mode must be preview or cleanup" ;;
esac
[[ -f "$ENV_FILE" ]] || fail "environment file was not found"
[[ -x "$TOOL" ]] || fail "storage tool is unavailable"

connection_string="$(
  grep -E '^(ConnectionStrings__DefaultConnection|ConnectionStrings__Postgres)=' "$ENV_FILE" \
    | head -n 1 \
    | cut -d '=' -f 2-
)"
database_name="$(
  printf '%s' "$connection_string" \
    | tr ';' '\n' \
    | awk -F '=' 'tolower($1) == "database" { print $2; exit }'
)"
application_role="$(
  printf '%s' "$connection_string" \
    | tr ';' '\n' \
    | awk -F '=' 'tolower($1) == "username" || tolower($1) == "user id" || tolower($1) == "user" { print $2; exit }'
)"
[[ "$database_name" == "$EXPECTED_DATABASE" ]] || fail "refusing to work with an unexpected database"
[[ "$application_role" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || fail "application database role was not found"

run_plan() {
  local connection="$1"
  local database="$2"
  local output="$3"
  ConnectionStrings__DefaultConnection="$connection" "$TOOL" "$PLAN_COMMAND" \
    --expected-database "$database" --output "$output"
}

health_check() {
  local backend_url
  backend_url="$(grep -E '^ASPNETCORE_URLS=' "$ENV_FILE" | head -n 1 | cut -d '=' -f 2- | cut -d ';' -f 1)"
  [[ -n "$backend_url" ]] || backend_url="http://127.0.0.1:5000"
  for _ in $(seq 1 30); do
    if curl -fsS -H "Host: ${PUBLIC_HOST}" "${backend_url%/}/health/ready" >/dev/null 2>&1; then
      curl -fsSk -H "Host: ${PUBLIC_HOST}" "https://127.0.0.1/health/ready" >/dev/null
      return 0
    fi
    sleep 2
  done
  fail "application did not become ready"
}

install -d -o root -g root -m 700 "$PRIVATE_DIR"
timestamp="$(date +%Y%m%d-%H%M%S)"

if [[ "$mode" == "preview" ]]; then
  plan_file="${PRIVATE_DIR}/${kind}-plan-${timestamp}.json"
  log "recordsCleanupStatus=preview-started; kind=${kind}; changes=none"
  run_plan "$connection_string" "$database_name" "$plan_file"
  log "recordsCleanupStatus=preview-completed; kind=${kind}; fingerprint=$(json_field "$plan_file" fingerprint)"
  exit 0
fi

plan_file=""
while IFS= read -r candidate; do
  if [[ "$(json_field "$candidate" fingerprint)" == "$fingerprint" ]]; then
    [[ -z "$plan_file" ]] || fail "several plans have the same fingerprint"
    plan_file="$candidate"
  fi
done < <(find "$PRIVATE_DIR" -maxdepth 1 -type f -name "${kind}-plan-*.json" -mmin "-${PLAN_MAX_AGE_MINUTES}" | sort)
[[ -n "$plan_file" ]] || fail "no preview from the last 23 hours has this fingerprint; run a new preview"
log "recordsCleanupStatus=cleanup-started; kind=${kind}; planFile=$(basename "$plan_file")"

log "serviceStatus=stopping; service=${SERVICE_NAME}"
systemctl stop "$SERVICE_NAME"
SERVICE_STOPPED=1

install -d -o garagebalance -g garagebalance -m 750 "$BACKUP_DIR"
backup_file="${BACKUP_DIR}/garagebalance_${timestamp}_before_${kind//-/_}_cleanup.pgdump"
log "backupStatus=started"
sudo -u postgres pg_dump --format=custom "$database_name" > "$backup_file"
[[ -s "$backup_file" ]] || fail "PostgreSQL backup was not created"
chown garagebalance:garagebalance "$backup_file"
chmod 600 "$backup_file"
backup_created_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
backup_sha256="$(sha256sum "$backup_file" | cut -d ' ' -f 1)"
log "backupStatus=completed; file=$(basename "$backup_file")"

# The restored copy is owned by the application role so the same tool can read its schema.
RESTORE_CHECK_DATABASE="garagebalance_cleanup_restore_${timestamp//-/}_$$"
log "restoreCheckStatus=started"
sudo -u postgres createdb --owner="$application_role" "$RESTORE_CHECK_DATABASE"
sudo -u postgres pg_restore --exit-on-error --no-owner --no-privileges --role="$application_role" \
  --dbname="$RESTORE_CHECK_DATABASE" < "$backup_file" >/dev/null
restore_connection="$(
  printf '%s' "$connection_string" \
    | tr ';' '\n' \
    | awk -F '=' -v database="$RESTORE_CHECK_DATABASE" \
      'tolower($1) == "database" { print $1 "=" database; next } NF { print }' \
    | paste -sd ';'
)"
restore_plan="${PRIVATE_DIR}/${kind}-restore-check-${timestamp}.json"
run_plan "$restore_connection" "$RESTORE_CHECK_DATABASE" "$restore_plan" >/dev/null
restored_fingerprint="$(json_field "$restore_plan" source,schemaFingerprint)"
source_fingerprint="$(json_field "$plan_file" source,schemaFingerprint)"
[[ -n "$restored_fingerprint" && "$restored_fingerprint" == "$source_fingerprint" ]] ||
  fail "restored schema does not match the previewed database"
restore_verified_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
sudo -u postgres dropdb --if-exists --force "$RESTORE_CHECK_DATABASE" >/dev/null
RESTORE_CHECK_DATABASE=""
log "restoreCheckStatus=completed"

receipt_file="${PRIVATE_DIR}/${kind}-verified-backup-${timestamp}.json"
(
  umask 077
  sudo -u postgres psql --no-psqlrc --set ON_ERROR_STOP=1 --tuples-only --no-align \
    --set "document=$(cat "$plan_file")" --set "backup_path=${backup_file}" --set "sha256=${backup_sha256}" \
    --set "backup_created_at=${backup_created_at}" --set "restore_verified_at=${restore_verified_at}" \
    --set "restored_fingerprint=${restored_fingerprint}" > "$receipt_file" <<'SQL'
SELECT jsonb_build_object('formatVersion', 1, 'source', (:'document')::jsonb -> 'source',
  'backupPath', :'backup_path', 'sha256', :'sha256', 'backupCreatedAtUtc', :'backup_created_at',
  'restoreVerifiedAtUtc', :'restore_verified_at', 'restoredSchemaFingerprint', :'restored_fingerprint',
  'restoreSucceeded', true);
SQL
)

result_file="${PRIVATE_DIR}/${kind}-result-${timestamp}.json"
ConnectionStrings__DefaultConnection="$connection_string" "$TOOL" "$CLEANUP_COMMAND" --execute \
  --plan "$plan_file" --confirm "$fingerprint" --backup-receipt "$receipt_file" \
  --expected-database "$database_name" --service "$SERVICE_NAME" \
  --reason "$CLEANUP_REASON" --output "$result_file"
log "recordsCleanupStatus=committed; kind=${kind}"

log "serviceStatus=starting; service=${SERVICE_NAME}"
systemctl start "$SERVICE_NAME"
SERVICE_STOPPED=0
health_check

next_plan="${PRIVATE_DIR}/${kind}-plan-${timestamp}-after.json"
run_plan "$connection_string" "$database_name" "$next_plan"
log "recordsCleanupStatus=completed; kind=${kind}"
