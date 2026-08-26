#!/usr/bin/env bash
set -Eeuo pipefail

APP_ROOT="/opt/garagebalance-staging"
ENV_FILE="/etc/garagebalance-staging.env"
SERVICE_NAME="garagebalance-staging.service"
PUBLIC_HOST="sgk.blagodaty.ru"
EXPECTED_CONFIRMATION="PURGE GARAGEBALANCE STAGING CODEX"
BACKUP_DIR="${APP_ROOT}/backups"
RESTORE_CHECK_DATABASE=""
SERVICE_STOPPED=0

log() {
  printf '%s %s\n' "$(date --iso-8601=seconds)" "$*"
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

[[ "$(id -u)" == "0" ]] || { echo "script must run as root"; exit 77; }
[[ "$#" == "1" && "$1" == "$EXPECTED_CONFIRMATION" ]] || {
  echo "exact confirmation is required"
  exit 64
}
[[ -f "$ENV_FILE" ]] || { echo "environment file was not found"; exit 66; }

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
[[ -n "$database_name" ]] || { echo "database name was not found"; exit 65; }

timestamp="$(date +%Y%m%d-%H%M%S)"
backup_file="${BACKUP_DIR}/garagebalance_${timestamp}_before_codex_cleanup.pgdump"
install -d -o garagebalance -g garagebalance -m 750 "$BACKUP_DIR"

log "backupStatus=started; file=${backup_file}"
sudo -u postgres pg_dump --format=custom "$database_name" > "$backup_file"
[[ -s "$backup_file" ]] || { echo "PostgreSQL backup was not created"; exit 1; }
chown garagebalance:garagebalance "$backup_file"
chmod 600 "$backup_file"
log "backupStatus=completed; file=${backup_file}"

RESTORE_CHECK_DATABASE="garagebalance_codex_restore_${timestamp//-/}_$$"
log "restoreCheckStatus=started; database=${RESTORE_CHECK_DATABASE}"
sudo -u postgres createdb "$RESTORE_CHECK_DATABASE"
sudo -u postgres pg_restore \
  --exit-on-error \
  --no-owner \
  --no-privileges \
  --dbname="$RESTORE_CHECK_DATABASE" \
  < "$backup_file" >/dev/null
restored_table_count="$(sudo -u postgres psql --set ON_ERROR_STOP=1 --tuples-only --no-align \
  --dbname="$RESTORE_CHECK_DATABASE" \
  --command="SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname = 'public';")"
(( restored_table_count > 0 )) || { echo "restore check database is empty"; exit 1; }
sudo -u postgres dropdb --if-exists --force "$RESTORE_CHECK_DATABASE" >/dev/null
RESTORE_CHECK_DATABASE=""
log "restoreCheckStatus=completed; tables=${restored_table_count}"

log "serviceStatus=stopping; service=${SERVICE_NAME}"
systemctl stop "$SERVICE_NAME"
SERVICE_STOPPED=1

cleanup_result="$(sudo -u postgres psql --set ON_ERROR_STOP=1 --tuples-only --no-align \
  --dbname="$database_name" <<'SQL'
BEGIN;

CREATE TEMP TABLE codex_departments ON COMMIT DROP AS
SELECT "Id" FROM staff_departments WHERE "Name" ILIKE '%codex%';

CREATE TEMP TABLE codex_staff ON COMMIT DROP AS
SELECT "Id" FROM staff_members
WHERE "FullName" ILIKE '%codex%'
   OR ("DepartmentId" IN (SELECT "Id" FROM codex_departments)
       AND "FullName" = 'Тестовый Сотрудник Приёмки');

CREATE TEMP TABLE codex_suppliers ON COMMIT DROP AS
SELECT "Id" FROM suppliers
WHERE concat_ws(' ', "Name", "Inn", "LegalAddress", "ContactPerson", "Phone", "Email", "Comment") ILIKE '%codex%';

CREATE TEMP TABLE codex_garages ON COMMIT DROP AS
SELECT "Id" FROM garages
WHERE concat_ws(' ', "Number", "Comment") ILIKE '%codex%'
   OR "Number" = 'ТЕСТ-260826';

CREATE TEMP TABLE codex_owners ON COMMIT DROP AS
SELECT "Id" FROM owners
WHERE concat_ws(' ', "LastName", "FirstName", "MiddleName", "Phone", "Address", "MeterNotes") ILIKE '%codex%';

DO $guard$
DECLARE
  target_count integer;
  dependent_count integer;
BEGIN
  SELECT (SELECT count(*) FROM codex_departments)
       + (SELECT count(*) FROM codex_staff)
       + (SELECT count(*) FROM codex_suppliers)
       + (SELECT count(*) FROM codex_garages)
       + (SELECT count(*) FROM codex_owners)
    INTO target_count;
  IF target_count = 0 THEN
    RAISE EXCEPTION 'No Codex acceptance records were found';
  END IF;
  IF target_count > 20 THEN
    RAISE EXCEPTION 'Safety limit exceeded: % candidate records', target_count;
  END IF;

  SELECT (SELECT count(*) FROM financial_operations WHERE "GarageId" IN (SELECT "Id" FROM codex_garages))
       + (SELECT count(*) FROM financial_operations WHERE "SupplierId" IN (SELECT "Id" FROM codex_suppliers))
       + (SELECT count(*) FROM financial_operations WHERE "StaffMemberId" IN (SELECT "Id" FROM codex_staff))
       + (SELECT count(*) FROM accruals WHERE "GarageId" IN (SELECT "Id" FROM codex_garages))
       + (SELECT count(*) FROM meter_readings WHERE "GarageId" IN (SELECT "Id" FROM codex_garages))
       + (SELECT count(*) FROM meter_devices WHERE "GarageId" IN (SELECT "Id" FROM codex_garages))
       + (SELECT count(*) FROM supplier_accruals WHERE "SupplierId" IN (SELECT "Id" FROM codex_suppliers))
       + (SELECT count(*) FROM staff_salary_adjustments WHERE "StaffMemberId" IN (SELECT "Id" FROM codex_staff))
    INTO dependent_count;
  IF dependent_count <> 0 THEN
    RAISE EXCEPTION 'Codex candidates have % financial dependencies; cleanup cancelled', dependent_count;
  END IF;
END
$guard$;

DELETE FROM opening_balance_adjustments
WHERE ("TargetKind" = 'garage' AND "TargetId" IN (SELECT "Id" FROM codex_garages))
   OR ("TargetKind" = 'supplier' AND "TargetId" IN (SELECT "Id" FROM codex_suppliers));
DELETE FROM garage_report_quick_list_garages WHERE "GarageId" IN (SELECT "Id" FROM codex_garages);
DELETE FROM fee_campaign_garages WHERE "GarageId" IN (SELECT "Id" FROM codex_garages);

DELETE FROM audit_events
WHERE "SearchText" LIKE '%codex%'
   OR lower(coalesce("MetadataJson", '')) LIKE '%codex%'
   OR "EntityId" IN (
        SELECT "Id"::text FROM codex_departments UNION ALL
        SELECT "Id"::text FROM codex_staff UNION ALL
        SELECT "Id"::text FROM codex_suppliers UNION ALL
        SELECT "Id"::text FROM codex_garages UNION ALL
        SELECT "Id"::text FROM codex_owners)
   OR "RelatedGarageId" IN (SELECT "Id"::text FROM codex_garages)
   OR "RelatedCounterpartyId" IN (
        SELECT "Id"::text FROM codex_suppliers UNION ALL SELECT "Id"::text FROM codex_staff);

DELETE FROM staff_members WHERE "Id" IN (SELECT "Id" FROM codex_staff);
DELETE FROM staff_departments WHERE "Id" IN (SELECT "Id" FROM codex_departments);
DELETE FROM suppliers WHERE "Id" IN (SELECT "Id" FROM codex_suppliers);
DELETE FROM garages WHERE "Id" IN (SELECT "Id" FROM codex_garages);
DELETE FROM owners
WHERE "Id" IN (SELECT "Id" FROM codex_owners)
  AND NOT EXISTS (SELECT 1 FROM garages WHERE garages."OwnerId" = owners."Id");

DO $verify$
BEGIN
  IF EXISTS (SELECT 1 FROM staff_departments WHERE "Name" ILIKE '%codex%')
     OR EXISTS (SELECT 1 FROM staff_members WHERE "FullName" ILIKE '%codex%' OR "FullName" = 'Тестовый Сотрудник Приёмки')
     OR EXISTS (SELECT 1 FROM suppliers WHERE concat_ws(' ', "Name", "Comment") ILIKE '%codex%')
     OR EXISTS (SELECT 1 FROM garages WHERE concat_ws(' ', "Number", "Comment") ILIKE '%codex%' OR "Number" = 'ТЕСТ-260826')
     OR EXISTS (SELECT 1 FROM owners WHERE concat_ws(' ', "LastName", "FirstName", "MiddleName", "MeterNotes") ILIKE '%codex%')
     OR EXISTS (SELECT 1 FROM audit_events WHERE "SearchText" LIKE '%codex%' OR lower(coalesce("MetadataJson", '')) LIKE '%codex%') THEN
    RAISE EXCEPTION 'Codex-marked records remain after cleanup';
  END IF;
END
$verify$;

SELECT format(
  'departments=%s; staff=%s; suppliers=%s; garages=%s; owners=%s',
  (SELECT count(*) FROM codex_departments),
  (SELECT count(*) FROM codex_staff),
  (SELECT count(*) FROM codex_suppliers),
  (SELECT count(*) FROM codex_garages),
  (SELECT count(*) FROM codex_owners));
COMMIT;
SQL
)"
log "cleanupStatus=completed; ${cleanup_result}"

systemctl start "$SERVICE_NAME"
SERVICE_STOPPED=0
sleep 3
curl -fsSk -H "Host: ${PUBLIC_HOST}" "https://127.0.0.1/health/ready" >/dev/null
curl -fsSk -H "Host: ${PUBLIC_HOST}" "https://127.0.0.1/" >/dev/null
log "healthStatus=ok; backup=${backup_file}"
