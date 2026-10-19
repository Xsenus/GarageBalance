#!/usr/bin/env bash
# Archived garage cleanup on staging through the reviewed StorageTool commands
# (docs/accounting-cleanup-cli.md). "preview" only writes a private plan;
# "cleanup" removes exactly the previewed plan after a verified backup.
set -Eeuo pipefail

APP_ROOT="/opt/garagebalance-staging"
ENV_FILE="/etc/garagebalance-staging.env"
SERVICE_NAME="garagebalance-staging.service"
PUBLIC_HOST="sgk.blagodaty.ru"
EXPECTED_DATABASE="garagebalance_staging"
PREVIEW_CONFIRMATION="PREVIEW GARAGEBALANCE ARCHIVED GARAGES"
CLEANUP_CONFIRMATION="DELETE GARAGEBALANCE ARCHIVED GARAGES"
CLEANUP_REASON="Очистка архивных гаражей без финансовой истории по замечаниям заказчика от 14.10.2026"
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
  log "archivedGarageCleanupStatus=failed; reason=$*"
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
# Plans are bounded (at most 100 eligible and 100 protected rows), so they fit one argument.
json_field() {
  local file="$1"
  local path="$2"
  (( $(stat --format=%s "$file") <= 120000 )) || fail "private JSON file is unexpectedly large"
  sudo -u postgres psql --no-psqlrc --set ON_ERROR_STOP=1 --tuples-only --no-align \
    --set "document=$(cat "$file")" --set "path=${path}" <<'SQL'
SELECT (:'document')::jsonb #>> string_to_array(:'path', ',');
SQL
}

run_plan() {
  local connection="$1"
  local database="$2"
  local output="$3"
  ConnectionStrings__DefaultConnection="$connection" "$TOOL" archived-garages-plan \
    --expected-database "$database" --max-garages 100 --output "$output"
}

print_plan() {
  local plan_file="$1"
  log "planFile=$(basename "$plan_file")"
  (( $(stat --format=%s "$plan_file") <= 120000 )) || fail "private JSON file is unexpectedly large"
  sudo -u postgres psql --no-psqlrc --set ON_ERROR_STOP=1 --tuples-only --no-align \
    --set "document=$(cat "$plan_file")" <<'SQL'
WITH plan AS (SELECT (:'document')::jsonb AS value)
SELECT 'eligibleGarage=' || (garage ->> 'garageNumber')
FROM plan, jsonb_array_elements(plan.value -> 'eligibleGarages') AS garage
UNION ALL
SELECT 'protectionReason=' || reason.key || '; garages=' || reason.value
FROM plan, jsonb_each_text(plan.value -> 'protectionCounts') AS reason;
SQL
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

[[ "$(id -u)" == "0" ]] || fail "script must run as root"
mode="${1:-}"
confirmation="${2:-}"
fingerprint="${3:-}"
case "$mode" in
  preview) [[ "$#" == "2" && "$confirmation" == "$PREVIEW_CONFIRMATION" ]] || fail "exact preview confirmation is required" ;;
  cleanup)
    [[ "$#" == "3" && "$confirmation" == "$CLEANUP_CONFIRMATION" ]] || fail "exact cleanup confirmation is required"
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

install -d -o root -g root -m 700 "$PRIVATE_DIR"
timestamp="$(date +%Y%m%d-%H%M%S)"

if [[ "$mode" == "preview" ]]; then
  plan_file="${PRIVATE_DIR}/archive-plan-${timestamp}.json"
  log "archivedGarageCleanupStatus=preview-started; changes=none"
  run_plan "$connection_string" "$database_name" "$plan_file"
  print_plan "$plan_file"
  log "archivedGarageCleanupStatus=preview-completed; fingerprint=$(json_field "$plan_file" fingerprint)"
  exit 0
fi

plan_file=""
while IFS= read -r candidate; do
  if [[ "$(json_field "$candidate" fingerprint)" == "$fingerprint" ]]; then
    [[ -z "$plan_file" ]] || fail "several plans have the same fingerprint"
    plan_file="$candidate"
  fi
done < <(find "$PRIVATE_DIR" -maxdepth 1 -type f -name 'archive-plan-*.json' -mmin "-${PLAN_MAX_AGE_MINUTES}" | sort)
[[ -n "$plan_file" ]] || fail "no preview from the last 23 hours has this fingerprint; run a new preview"
selected="$(json_field "$plan_file" totalEligible)"
log "archivedGarageCleanupStatus=cleanup-started; planFile=$(basename "$plan_file"); eligible=${selected}"

log "serviceStatus=stopping; service=${SERVICE_NAME}"
systemctl stop "$SERVICE_NAME"
SERVICE_STOPPED=1

install -d -o garagebalance -g garagebalance -m 750 "$BACKUP_DIR"
backup_file="${BACKUP_DIR}/garagebalance_${timestamp}_before_archived_garage_cleanup.pgdump"
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
restore_plan="${PRIVATE_DIR}/restore-check-${timestamp}.json"
run_plan "$restore_connection" "$RESTORE_CHECK_DATABASE" "$restore_plan" >/dev/null
restored_fingerprint="$(json_field "$restore_plan" source,schemaFingerprint)"
source_fingerprint="$(json_field "$plan_file" source,schemaFingerprint)"
[[ -n "$restored_fingerprint" && "$restored_fingerprint" == "$source_fingerprint" ]] ||
  fail "restored schema does not match the previewed database"
restore_verified_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
sudo -u postgres dropdb --if-exists --force "$RESTORE_CHECK_DATABASE" >/dev/null
RESTORE_CHECK_DATABASE=""
log "restoreCheckStatus=completed"

receipt_file="${PRIVATE_DIR}/verified-backup-${timestamp}.json"
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

result_file="${PRIVATE_DIR}/archive-result-${timestamp}.json"
ConnectionStrings__DefaultConnection="$connection_string" "$TOOL" archived-garages-cleanup --execute \
  --plan "$plan_file" --confirm "$fingerprint" --backup-receipt "$receipt_file" \
  --expected-database "$database_name" --service "$SERVICE_NAME" \
  --reason "$CLEANUP_REASON" --output "$result_file" >/dev/null
log "archivedGarageCleanupStatus=committed; deletedGarages=$(json_field "$result_file" deletedGarages)"

log "serviceStatus=starting; service=${SERVICE_NAME}"
systemctl start "$SERVICE_NAME"
SERVICE_STOPPED=0
health_check

next_plan="${PRIVATE_DIR}/archive-plan-${timestamp}-after.json"
run_plan "$connection_string" "$database_name" "$next_plan" >/dev/null
log "archivedGarageCleanupStatus=completed; remainingEligible=$(json_field "$next_plan" totalEligible); remainingArchived=$(json_field "$next_plan" totalArchived)"
