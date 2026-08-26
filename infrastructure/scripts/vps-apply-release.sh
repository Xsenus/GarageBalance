#!/usr/bin/env bash
set -Eeuo pipefail

APP_ROOT="/opt/garagebalance-staging"
ENV_FILE="/etc/garagebalance-staging.env"
SERVICE_NAME="garagebalance-staging.service"
DEPLOY_USER="garagebalance-deploy"
APP_USER="garagebalance"
APP_GROUP="garagebalance"
PUBLIC_HOST="sgk.blagodaty.ru"
FRONTEND_ASSET_RETENTION_DAYS=30

if [[ "${1:-}" == "prepare-showcase" ]]; then
  [[ "$#" == "3" ]] || {
    echo "usage: $0 prepare-showcase <archive-path> <confirmation>"
    exit 64
  }
  exec /usr/local/bin/garagebalance-showcase-prepare "$2" "$3"
fi

if [[ "${1:-}" == "cleanup-codex-records" ]]; then
  [[ "$#" == "2" ]] || {
    echo "usage: $0 cleanup-codex-records <confirmation>"
    exit 64
  }
  exec /usr/local/bin/garagebalance-cleanup-codex-records "$2"
fi

release_id="${1:-}"

if [[ -z "$release_id" || ! "$release_id" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "usage: $0 <release-id>"
  echo "release id may contain only letters, numbers, dot, underscore and dash"
  exit 64
fi

UPLOAD_DIR="/home/${DEPLOY_USER}/uploads/${release_id}"
API_ARCHIVE="${UPLOAD_DIR}/api.tar.gz"
FRONTEND_ARCHIVE="${UPLOAD_DIR}/frontend.tar.gz"
MIGRATION_SQL="${UPLOAD_DIR}/deploy-migrations.sql"
OPERATIONS_ARCHIVE="${UPLOAD_DIR}/operations.tar.gz"
OPERATIONS_DIR="${UPLOAD_DIR}/operations"
RELEASE_DIR="${APP_ROOT}/releases/${release_id}"
REEXEC_APPLY_SCRIPT="${RELEASE_DIR}/vps-apply-release.next.sh"
NEXT_API="${APP_ROOT}/api.next-${release_id}"
NEXT_FRONTEND="${APP_ROOT}/frontend.next-${release_id}"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
PREV_API="${APP_ROOT}/api.prev-${TIMESTAMP}-${release_id}"
PREV_FRONTEND="${APP_ROOT}/frontend.prev-${TIMESTAMP}-${release_id}"
BACKUP_DIR="${APP_ROOT}/backups"
DIAGNOSTIC_LOG_DIR="${APP_ROOT}/logs"
SERVICE_STOPPED=0
SWAPPED=0
RESTORE_CHECK_DATABASE=""
DATABASE_MUTATION_STARTED=0
BACKUP_FILE=""

log() {
  printf '%s %s\n' "$(date --iso-8601=seconds)" "$*"
}

fail() {
  cleanup_restore_check
  log "deployStatus=failed; reason=$*"
  exit 1
}

ensure_env_setting() {
  local name="$1"
  local value="$2"
  if ! grep -qE "^${name}=" "$ENV_FILE"; then
    printf '%s=%s\n' "$name" "$value" >> "$ENV_FILE"
  fi
}

restore_previous_release() {
  set +e

  if [[ "$DATABASE_MUTATION_STARTED" == "1" && -s "$BACKUP_FILE" ]]; then
    log "databaseRollbackStatus=started; database=${database_name}; backup=${BACKUP_FILE}"
    systemctl stop "$SERVICE_NAME" >/dev/null 2>&1
    SERVICE_STOPPED=1
    sudo -u postgres psql \
      --set ON_ERROR_STOP=1 \
      --set="target_database=${database_name}" \
      --dbname=postgres \
      --command="SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = :'target_database' AND pid <> pg_backend_pid();" \
      >/dev/null
    # Root opens the protected application-owned dump before sudo changes user.
    sudo -u postgres pg_restore \
      --clean \
      --if-exists \
      --exit-on-error \
      --no-owner \
      --no-privileges \
      --dbname="$database_name" \
      < "$BACKUP_FILE" >/dev/null
    log "databaseRollbackStatus=completed; database=${database_name}"
  fi

  if [[ "$SWAPPED" == "1" ]]; then
    log "rollbackStatus=started"
    systemctl stop "$SERVICE_NAME" >/dev/null 2>&1
    rm -rf "${APP_ROOT}/api" "${APP_ROOT}/frontend"
    if [[ -d "$PREV_API" ]]; then
      mv "$PREV_API" "${APP_ROOT}/api"
    fi
    if [[ -d "$PREV_FRONTEND" ]]; then
      mv "$PREV_FRONTEND" "${APP_ROOT}/frontend"
    fi
    systemctl start "$SERVICE_NAME" >/dev/null 2>&1
    log "rollbackStatus=completed"
  elif [[ "$SERVICE_STOPPED" == "1" ]]; then
    systemctl start "$SERVICE_NAME" >/dev/null 2>&1
    log "rollbackStatus=service-restarted-with-current-release"
  fi
}

cleanup_restore_check() {
  if [[ -n "$RESTORE_CHECK_DATABASE" ]]; then
    sudo -u postgres dropdb --if-exists --force "$RESTORE_CHECK_DATABASE" >/dev/null 2>&1 || true
    RESTORE_CHECK_DATABASE=""
  fi
}

on_error() {
  local exit_code=$?
  local line_number=$1
  log "deployError=line-${line_number}; exitCode=${exit_code}"
  cleanup_restore_check
  restore_previous_release
  exit "$exit_code"
}

trap 'on_error "$LINENO"' ERR

[[ "$(id -u)" == "0" ]] || fail "script must run as root"
[[ -d "$APP_ROOT" ]] || fail "application root was not found: $APP_ROOT"
[[ -f "$ENV_FILE" ]] || fail "environment file was not found: $ENV_FILE"
[[ -s "$API_ARCHIVE" ]] || fail "API archive was not found or empty: $API_ARCHIVE"
[[ -s "$FRONTEND_ARCHIVE" ]] || fail "frontend archive was not found or empty: $FRONTEND_ARCHIVE"
[[ -s "$MIGRATION_SQL" ]] || fail "migration SQL was not found or empty: $MIGRATION_SQL"
[[ -s "$OPERATIONS_ARCHIVE" ]] || fail "operations archive was not found or empty: $OPERATIONS_ARCHIVE"

install -d -o "${APP_USER}" -g "${APP_GROUP}" -m 750 "$DIAGNOSTIC_LOG_DIR"
install -d -o "${APP_USER}" -g "${APP_GROUP}" -m 750 "$BACKUP_DIR"
ensure_env_setting "DiagnosticLogging__Enabled" "true"
ensure_env_setting "DiagnosticLogging__Directory" "$DIAGNOSTIC_LOG_DIR"
ensure_env_setting "DiagnosticLogging__RetentionDays" "14"
ensure_env_setting "DiagnosticLogging__MaxFileSizeMb" "10"
ensure_env_setting "DiagnosticLogging__PackageDays" "7"
ensure_env_setting "DiagnosticLogging__PackageMaxSizeMb" "20"
ensure_env_setting "DatabaseBackup__Enabled" "true"
ensure_env_setting "DatabaseBackup__AutomaticEnabled" "true"
ensure_env_setting "DatabaseBackup__Directory" "$BACKUP_DIR"
ensure_env_setting "DatabaseBackup__IntervalHours" "24"
ensure_env_setting "DatabaseBackup__RetentionCount" "30"

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

if [[ -z "$database_name" ]]; then
  fail "database name was not found in ${ENV_FILE}"
fi

aspnet_urls="$(grep -E '^ASPNETCORE_URLS=' "$ENV_FILE" | head -n 1 | cut -d '=' -f 2-)"
backend_base_url="${aspnet_urls%%;*}"
if [[ -z "$backend_base_url" ]]; then
  fail "ASPNETCORE_URLS was not found in ${ENV_FILE}"
fi

mkdir -p "$RELEASE_DIR"
rm -rf "$NEXT_API" "$NEXT_FRONTEND" "$OPERATIONS_DIR"
mkdir -p "$NEXT_API" "$NEXT_FRONTEND" "$OPERATIONS_DIR"

log "releasePrepare=extracting; releaseId=${release_id}"
tar -xzf "$API_ARCHIVE" -C "$NEXT_API"
tar -xzf "$FRONTEND_ARCHIVE" -C "$NEXT_FRONTEND"
tar -xzf "$OPERATIONS_ARCHIVE" -C "$OPERATIONS_DIR"

[[ -f "${NEXT_API}/GarageBalance.Api" ]] || fail "published API executable was not found"
[[ -f "${NEXT_FRONTEND}/index.html" ]] || fail "frontend index.html was not found"
[[ -f "${OPERATIONS_DIR}/infrastructure/scripts/install-vps-performance-configuration.sh" ]] ||
  fail "VPS performance installer was not found"
[[ -f "${OPERATIONS_DIR}/infrastructure/scripts/vps-apply-release.sh" ]] ||
  fail "VPS release script was not found"
[[ -f "${OPERATIONS_DIR}/infrastructure/scripts/prepare-staging-showcase.sh" ]] ||
  fail "staging showcase preparation script was not found"
[[ -f "${OPERATIONS_DIR}/infrastructure/scripts/cleanup-staging-codex-records.sh" ]] ||
  fail "staging Codex cleanup script was not found"
bash -n \
  "${OPERATIONS_DIR}/infrastructure/scripts/install-vps-performance-configuration.sh" \
  "${OPERATIONS_DIR}/infrastructure/scripts/garagebalance-healthcheck.sh" \
  "${OPERATIONS_DIR}/infrastructure/scripts/garagebalance-performance-check.sh" \
  "${OPERATIONS_DIR}/infrastructure/scripts/vps-apply-release.sh" \
  "${OPERATIONS_DIR}/infrastructure/scripts/prepare-staging-showcase.sh" \
  "${OPERATIONS_DIR}/infrastructure/scripts/cleanup-staging-codex-records.sh"

packaged_apply_script="${OPERATIONS_DIR}/infrastructure/scripts/vps-apply-release.sh"
if [[ "${GARAGEBALANCE_DEPLOY_REEXECUTED:-0}" != "1" ]] &&
   ! cmp --silent "$0" "$packaged_apply_script"; then
  install -o root -g root -m 700 "$packaged_apply_script" "$REEXEC_APPLY_SCRIPT"
  log "releasePrepare=reexec-updated-apply-script; releaseId=${release_id}"
  GARAGEBALANCE_DEPLOY_REEXECUTED=1 exec bash "$REEXEC_APPLY_SCRIPT" "$release_id"
fi

mapfile -t frontend_entry_assets < <(
  grep -oE '"/assets/[^"]+"' "${NEXT_FRONTEND}/index.html" \
    | tr -d '"' \
    | sort -u
)
(( ${#frontend_entry_assets[@]} > 0 )) || fail "frontend index.html does not reference production assets"
for asset_path in "${frontend_entry_assets[@]}"; do
  [[ -s "${NEXT_FRONTEND}${asset_path}" ]] || fail "frontend entry asset was not found or empty: ${asset_path}"
done

# Keep content-hashed assets from recent releases available for browser tabs that
# were open during deployment. New files win; preserved files are pruned by age.
if [[ -d "${APP_ROOT}/frontend/assets" ]]; then
  mkdir -p "${NEXT_FRONTEND}/assets"
  cp -a -n "${APP_ROOT}/frontend/assets/." "${NEXT_FRONTEND}/assets/"
  find "${NEXT_FRONTEND}/assets" -type f -mtime "+${FRONTEND_ASSET_RETENTION_DAYS}" -delete
fi

chmod +x "${NEXT_API}/GarageBalance.Api"
find "$NEXT_API" "$NEXT_FRONTEND" -type d -exec chmod 755 {} +
find "$NEXT_API" "$NEXT_FRONTEND" -type f -exec chmod 644 {} +
chmod +x "${NEXT_API}/GarageBalance.Api"
chown -R "${APP_USER}:${APP_GROUP}" "$NEXT_API" "$NEXT_FRONTEND"

cp "$MIGRATION_SQL" "${RELEASE_DIR}/deploy-migrations.sql"
cp "$OPERATIONS_ARCHIVE" "${RELEASE_DIR}/operations.tar.gz"
chown "${APP_USER}:${APP_GROUP}" "${RELEASE_DIR}/deploy-migrations.sql"
chown "${APP_USER}:${APP_GROUP}" "${RELEASE_DIR}/operations.tar.gz"
chmod 640 "${RELEASE_DIR}/deploy-migrations.sql"
chmod 640 "${RELEASE_DIR}/operations.tar.gz"

BACKUP_FILE="${BACKUP_DIR}/garagebalance_${TIMESTAMP}_${release_id}.pgdump"
log "backupStatus=started; file=${BACKUP_FILE}"
sudo -u postgres pg_dump --format=custom "$database_name" > "$BACKUP_FILE"
[[ -s "$BACKUP_FILE" ]] || fail "PostgreSQL backup was not created"
chown "${APP_USER}:${APP_GROUP}" "$BACKUP_FILE"
chmod 600 "$BACKUP_FILE"
log "backupStatus=completed; file=${BACKUP_FILE}"

RESTORE_CHECK_DATABASE="garagebalance_restore_check_${TIMESTAMP//-/}_$$"
log "restoreCheckStatus=started; database=${RESTORE_CHECK_DATABASE}"
sudo -u postgres createdb "$RESTORE_CHECK_DATABASE"
# Root opens the mode-600 dump before pg_restore runs as the database OS user.
sudo -u postgres pg_restore \
  --exit-on-error \
  --no-owner \
  --no-privileges \
  --dbname="$RESTORE_CHECK_DATABASE" \
  < "$BACKUP_FILE" >/dev/null
restored_table_count="$(
  sudo -u postgres psql \
    --set ON_ERROR_STOP=1 \
    --tuples-only \
    --no-align \
    --dbname="$RESTORE_CHECK_DATABASE" \
    --command="SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname = 'public';"
)"
(( restored_table_count > 0 )) || fail "restore check database does not contain application tables"
cleanup_restore_check
log "restoreCheckStatus=completed; tables=${restored_table_count}"

nginx -t

log "serviceStatus=stopping; service=${SERVICE_NAME}"
systemctl stop "$SERVICE_NAME"
SERVICE_STOPPED=1

log "migrationStatus=started; database=${database_name}"
DATABASE_MUTATION_STARTED=1
sudo -u postgres psql --set ON_ERROR_STOP=1 --dbname="$database_name" < "$MIGRATION_SQL" >/dev/null
log "migrationStatus=completed"

mv "${APP_ROOT}/api" "$PREV_API"
mv "${APP_ROOT}/frontend" "$PREV_FRONTEND"
mv "$NEXT_API" "${APP_ROOT}/api"
mv "$NEXT_FRONTEND" "${APP_ROOT}/frontend"
SWAPPED=1

log "serviceStatus=starting; service=${SERVICE_NAME}"
systemctl start "$SERVICE_NAME"
SERVICE_STOPPED=0

sleep 3
curl -fsS -H "Host: ${PUBLIC_HOST}" "${backend_base_url%/}/health/ready" >/dev/null
curl -fsSk -H "Host: ${PUBLIC_HOST}" "https://127.0.0.1/health/ready" >/dev/null
curl -fsSk -H "Host: ${PUBLIC_HOST}" "https://127.0.0.1/" >/dev/null
for asset_path in "${frontend_entry_assets[@]}"; do
  curl -fsSk -H "Host: ${PUBLIC_HOST}" "https://127.0.0.1${asset_path}" >/dev/null
done

bash "${OPERATIONS_DIR}/infrastructure/scripts/install-vps-performance-configuration.sh" "$OPERATIONS_DIR"
install -o root -g root -m 0750 \
  "${OPERATIONS_DIR}/infrastructure/scripts/vps-apply-release.sh" \
  /usr/local/bin/garagebalance-deploy-apply
install -o root -g root -m 0750 \
  "${OPERATIONS_DIR}/infrastructure/scripts/prepare-staging-showcase.sh" \
  /usr/local/bin/garagebalance-showcase-prepare
install -o root -g root -m 0750 \
  "${OPERATIONS_DIR}/infrastructure/scripts/cleanup-staging-codex-records.sh" \
  /usr/local/bin/garagebalance-cleanup-codex-records

find "/home/${DEPLOY_USER}/uploads" -mindepth 1 -maxdepth 1 -type d -mtime +14 -exec rm -rf {} +

log "deployStatus=ok; releaseId=${release_id}; backup=${BACKUP_FILE}; previousApi=${PREV_API}; previousFrontend=${PREV_FRONTEND}"
