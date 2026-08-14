#!/usr/bin/env bash
set -Eeuo pipefail

APP_ROOT="/opt/garagebalance-staging"
ENV_FILE="/etc/garagebalance-staging.env"
SERVICE_NAME="garagebalance-staging.service"
APP_USER="garagebalance"
APP_GROUP="garagebalance"
PUBLIC_HOST="sgk.blagodaty.ru"
EXPECTED_CONFIRMATION="PREPARE GARAGEBALANCE STAGING"

archive_path="${1:-}"
confirmation="${2:-}"
timestamp="$(date +%Y%m%d-%H%M%S)"
work_dir="${APP_ROOT}/showcase-${timestamp}-$$"
backup_file="${APP_ROOT}/backups/showcase_before_${timestamp}.pgdump"
restore_check_database=""
mutation_started=0
service_stopped=0

log() {
  printf '%s %s\n' "$(date --iso-8601=seconds)" "$*"
}

cleanup() {
  if [[ -n "$restore_check_database" ]]; then
    sudo -u postgres dropdb --if-exists --force "$restore_check_database" >/dev/null 2>&1 || true
  fi
  rm -rf "$work_dir"
}

restore_on_error() {
  local exit_code=$?
  trap - ERR
  log "showcasePrepareStatus=failed; exitCode=${exit_code}"
  if [[ "$mutation_started" == "1" && -s "$backup_file" ]]; then
    systemctl stop "$SERVICE_NAME" >/dev/null 2>&1 || true
    sudo -u postgres psql --set ON_ERROR_STOP=1 --dbname=postgres \
      --command="SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${database_name}' AND pid <> pg_backend_pid();" \
      >/dev/null
    sudo -u postgres pg_restore --clean --if-exists --exit-on-error --no-owner --no-privileges \
      --dbname="$database_name" < "$backup_file" >/dev/null
    log "showcaseRollbackStatus=completed; backup=${backup_file}"
  fi
  if [[ "$service_stopped" == "1" ]]; then
    systemctl start "$SERVICE_NAME" >/dev/null 2>&1 || true
  fi
  cleanup
  exit "$exit_code"
}

trap restore_on_error ERR
trap cleanup EXIT

[[ "$(id -u)" == "0" ]] || { log "showcasePrepareStatus=refused; reason=root-required"; exit 64; }
[[ "$confirmation" == "$EXPECTED_CONFIRMATION" ]] || { log "showcasePrepareStatus=refused; reason=confirmation"; exit 64; }
[[ -s "$archive_path" ]] || { log "showcasePrepareStatus=refused; reason=archive-missing"; exit 66; }
[[ -f "$ENV_FILE" ]] || { log "showcasePrepareStatus=refused; reason=environment-missing"; exit 66; }

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
[[ "$database_name" == "garagebalance_staging" ]] || {
  log "showcasePrepareStatus=refused; reason=database; database=${database_name:-missing}"
  exit 64
}

install -d -o "$APP_USER" -g "$APP_GROUP" -m 750 "${APP_ROOT}/backups"
mkdir -p "$work_dir"
tar -xzf "$archive_path" -C "$work_dir"
[[ -x "${work_dir}/GarageBalance.ShowcaseSeed" ]] || {
  log "showcasePrepareStatus=refused; reason=runner-missing"
  exit 66
}

log "showcaseBackupStatus=started; database=${database_name}"
sudo -u postgres pg_dump --format=custom "$database_name" > "$backup_file"
[[ -s "$backup_file" ]]
chown "$APP_USER:$APP_GROUP" "$backup_file"
chmod 600 "$backup_file"

restore_check_database="garagebalance_showcase_restore_${timestamp//-/}_$$"
sudo -u postgres createdb "$restore_check_database"
sudo -u postgres pg_restore --exit-on-error --no-owner --no-privileges \
  --dbname="$restore_check_database" < "$backup_file" >/dev/null
restored_tables="$(sudo -u postgres psql --tuples-only --no-align --dbname="$restore_check_database" \
  --command="SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname = 'public';")"
(( restored_tables > 0 ))
sudo -u postgres dropdb --force "$restore_check_database"
restore_check_database=""
log "showcaseBackupStatus=verified; tables=${restored_tables}; file=${backup_file}"

systemctl stop "$SERVICE_NAME"
service_stopped=1
mutation_started=1
log "showcasePrepareStatus=running"
sudo -u "$APP_USER" env \
  GARAGEBALANCE_SHOWCASE_CONNECTION="$connection_string" \
  GARAGEBALANCE_SHOWCASE_CONFIRMATION="$EXPECTED_CONFIRMATION" \
  "${work_dir}/GarageBalance.ShowcaseSeed" prepare

sudo -u "$APP_USER" env \
  GARAGEBALANCE_SHOWCASE_CONNECTION="$connection_string" \
  GARAGEBALANCE_SHOWCASE_CONFIRMATION="$EXPECTED_CONFIRMATION" \
  "${work_dir}/GarageBalance.ShowcaseSeed" audit

systemctl start "$SERVICE_NAME"
service_stopped=0
sleep 3
curl -fsSk -H "Host: ${PUBLIC_HOST}" "https://127.0.0.1/health/ready" >/dev/null
curl -fsSk -H "Host: ${PUBLIC_HOST}" "https://127.0.0.1/" >/dev/null
log "showcasePrepareStatus=ok; database=${database_name}; backup=${backup_file}"
