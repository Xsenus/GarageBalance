#!/usr/bin/env bash
set -Eeuo pipefail

APP_ROOT="/opt/garagebalance-staging"
ENV_FILE="/etc/garagebalance-staging.env"
SERVICE_NAME="garagebalance-staging.service"
DEPLOY_USER="garagebalance-deploy"
APP_USER="garagebalance"
APP_GROUP="garagebalance"
PUBLIC_HOST="sgk.blagodaty.ru"

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
RELEASE_DIR="${APP_ROOT}/releases/${release_id}"
NEXT_API="${APP_ROOT}/api.next-${release_id}"
NEXT_FRONTEND="${APP_ROOT}/frontend.next-${release_id}"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
PREV_API="${APP_ROOT}/api.prev-${TIMESTAMP}-${release_id}"
PREV_FRONTEND="${APP_ROOT}/frontend.prev-${TIMESTAMP}-${release_id}"
BACKUP_DIR="${APP_ROOT}/backups"
SERVICE_STOPPED=0
SWAPPED=0

log() {
  printf '%s %s\n' "$(date --iso-8601=seconds)" "$*"
}

fail() {
  log "deployStatus=failed; reason=$*"
  exit 1
}

restore_previous_release() {
  set +e

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

on_error() {
  local exit_code=$?
  local line_number=$1
  log "deployError=line-${line_number}; exitCode=${exit_code}"
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

mkdir -p "$BACKUP_DIR" "$RELEASE_DIR"
rm -rf "$NEXT_API" "$NEXT_FRONTEND"
mkdir -p "$NEXT_API" "$NEXT_FRONTEND"

log "releasePrepare=extracting; releaseId=${release_id}"
tar -xzf "$API_ARCHIVE" -C "$NEXT_API"
tar -xzf "$FRONTEND_ARCHIVE" -C "$NEXT_FRONTEND"

[[ -f "${NEXT_API}/GarageBalance.Api" ]] || fail "published API executable was not found"
[[ -f "${NEXT_FRONTEND}/index.html" ]] || fail "frontend index.html was not found"

chmod +x "${NEXT_API}/GarageBalance.Api"
find "$NEXT_API" "$NEXT_FRONTEND" -type d -exec chmod 755 {} +
find "$NEXT_API" "$NEXT_FRONTEND" -type f -exec chmod 644 {} +
chmod +x "${NEXT_API}/GarageBalance.Api"
chown -R "${APP_USER}:${APP_GROUP}" "$NEXT_API" "$NEXT_FRONTEND"

cp "$MIGRATION_SQL" "${RELEASE_DIR}/deploy-migrations.sql"
chown "${APP_USER}:${APP_GROUP}" "${RELEASE_DIR}/deploy-migrations.sql"
chmod 640 "${RELEASE_DIR}/deploy-migrations.sql"

backup_file="${BACKUP_DIR}/garagebalance_${TIMESTAMP}_${release_id}.pgdump"
log "backupStatus=started; file=${backup_file}"
sudo -u postgres pg_dump --format=custom "$database_name" > "$backup_file"
[[ -s "$backup_file" ]] || fail "PostgreSQL backup was not created"
chmod 600 "$backup_file"
log "backupStatus=completed; file=${backup_file}"

nginx -t

log "serviceStatus=stopping; service=${SERVICE_NAME}"
systemctl stop "$SERVICE_NAME"
SERVICE_STOPPED=1

log "migrationStatus=started; database=${database_name}"
sudo -u postgres psql --set ON_ERROR_STOP=1 --dbname="$database_name" --file="$MIGRATION_SQL" >/dev/null
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
curl -fsS -H "Host: ${PUBLIC_HOST}" "${backend_base_url%/}/health" >/dev/null
curl -fsSk -H "Host: ${PUBLIC_HOST}" "https://127.0.0.1/health" >/dev/null
curl -fsSk -H "Host: ${PUBLIC_HOST}" "https://127.0.0.1/" >/dev/null

find "/home/${DEPLOY_USER}/uploads" -mindepth 1 -maxdepth 1 -type d -mtime +14 -exec rm -rf {} +

log "deployStatus=ok; releaseId=${release_id}; backup=${backup_file}; previousApi=${PREV_API}; previousFrontend=${PREV_FRONTEND}"
