#!/usr/bin/env bash
set -Eeuo pipefail

APP_ROOT=/opt/garagebalance-staging
SERVICE=garagebalance-staging.service
ENV_FILE=/etc/garagebalance-staging.env
CLOUD_ENV=/etc/garagebalance-staging-cloud.env
DROP_IN_DIR=/etc/systemd/system/garagebalance-staging.service.d
DROP_IN="$DROP_IN_DIR/50-cloud-backup.conf"
BACKUP_DIR="$APP_ROOT/backups"
TOOL_UNIT=/etc/systemd/system/garagebalance-storage-tool@.service
SYNC_TIMER=/etc/systemd/system/garagebalance-storage-sync.timer
MIGRATION_DIR=/var/lib/garagebalance-staging/storage-migration

wait_for_api() {
  local attempt
  for attempt in {1..15}; do
    if curl --fail --silent --show-error --connect-timeout 5 --max-time 10 \
      https://sgk.blagodaty.ru/health/ready >/dev/null 2>&1; then
      return 0
    fi
    sleep 2
  done
  echo 'API did not become healthy after restart' >&2
  return 1
}

[[ "$(id -u)" == 0 ]] || { echo 'root is required' >&2; exit 77; }

case "${1:-}" in
  inspect)
    [[ "$#" == 1 ]] || exit 64
    [[ -d "$BACKUP_DIR" ]] || { echo 'backup directory missing' >&2; exit 1; }
    find "$BACKUP_DIR" -maxdepth 1 -type f -name '*.pgdump' -printf '%s\n' |
      awk '{count++; bytes+=$1} END {printf "backups=%d bytes=%.0f\n", count, bytes}'
    if [[ -f "$CLOUD_ENV" ]]; then
      grep -E '^Storage__Mode=' "$CLOUD_ENV" || true
    else
      echo 'Storage__Mode=Single'
    fi
    systemctl is-active "$SERVICE"
    systemctl is-enabled garagebalance-storage-sync.timer 2>/dev/null || true
    exit 0
    ;;
  audit-files)
    [[ "$#" == 1 ]] || exit 64
    [[ -d "$BACKUP_DIR" ]] || exit 1
    find "$BACKUP_DIR" -maxdepth 1 -type f \
      \( -name '*.pgdump' -o -name '*.pgdump.manifest.json' \) \
      -printf '%f owner=%u group=%g mode=%m size=%s\n' | sort
    ;;
  allow-legacy-read)
    [[ "$#" == 1 ]] || exit 64
    names=(
      emergency_before_regular_seed_20260715-094520.pgdump
      garagebalance_20260624-164006.pgdump
      garagebalance_20260624-164103.pgdump
      garagebalance_20260624-164138.pgdump
      garagebalance_20260624-200510.pgdump
      garagebalance_auth_reset_20260624-164543.pgdump
      working_data_analysis_20260904-151333.pgdump
    )
    for name in "${names[@]}"; do
      file="$BACKUP_DIR/$name"
      [[ -f "$file" && ! -L "$file" && ! -e "$file.manifest.json" ]] || exit 1
      owner_group="$(stat -c '%U:%G' -- "$file")"
      mode="$(stat -c '%a' -- "$file")"
      case "$owner_group:$mode" in
        postgres:postgres:600|root:root:600|postgres:garagebalance:640|root:garagebalance:640) ;;
        *) echo "unexpected ownership or mode: $name" >&2; exit 1 ;;
      esac
      pg_restore --list "$file" >/dev/null
    done
    for name in "${names[@]}"; do
      file="$BACKUP_DIR/$name"
      chgrp garagebalance -- "$file"
      chmod 640 -- "$file"
    done
    echo 'seven validated legacy archives are readable by the backup service'
    ;;
  apply)
    [[ "$#" == 4 ]] || { echo 'usage: apply <bucket> <kms-key-id> <tenant-id>' >&2; exit 64; }
    bucket="$2"
    kms_key_id="$3"
    tenant_id="$4"
    [[ "$bucket" =~ ^[a-z0-9][a-z0-9.-]{2,62}$ ]] || exit 64
    [[ "$kms_key_id" =~ ^[0-9a-fA-F-]{36}$ ]] || exit 64
    [[ "$tenant_id" =~ ^[0-9a-fA-F-]{36}$ ]] || exit 64
    [[ -f "$ENV_FILE" && -d "$BACKUP_DIR" && -x "$APP_ROOT/storage-tool/GarageBalance.StorageTool" ]] || exit 1
    [[ ! -e "$CLOUD_ENV" && ! -e "$DROP_IN" && ! -e "$TOOL_UNIT" ]] || {
      echo 'Cloud backup is already configured; refusing to overwrite it' >&2
      exit 1
    }

    # The two credential lines are supplied on stdin, never as arguments or log output.
    IFS= read -r key_id || exit 64
    IFS= read -r key_secret || exit 64
    [[ "$key_id" =~ ^[A-Za-z0-9._-]{16,128}$ ]] || exit 64
    [[ "$key_secret" =~ ^[A-Za-z0-9/+=._-]{16,256}$ ]] || exit 64

    umask 077
    temporary_env="$(mktemp /etc/garagebalance-staging-cloud.env.XXXXXXXX)"
    rollback() {
      rm -f -- "$temporary_env" "$CLOUD_ENV" "$DROP_IN" "$TOOL_UNIT"
      systemctl daemon-reload || true
      systemctl restart "$SERVICE" || true
    }
    on_error() {
      trap - ERR
      rollback
      exit 1
    }
    trap on_error ERR

    {
      printf 'AWS_ACCESS_KEY_ID=%s:%s\n' "$tenant_id" "$key_id"
      printf 'AWS_SECRET_ACCESS_KEY=%s\n' "$key_secret"
      printf '%s\n' \
        'Storage__Mode=AsyncMirror' \
        'Storage__TenantId=garagebalance' \
        'Storage__Destinations__0__Id=local-hot' \
        'Storage__Destinations__0__Type=LocalFileSystem' \
        'Storage__Destinations__0__State=Enabled' \
        'Storage__Destinations__0__FailureDomain=staging-vps' \
        'Storage__Destinations__0__TenantId=garagebalance' \
        "Storage__Destinations__0__RootPath=$BACKUP_DIR" \
        'Storage__Destinations__0__Capabilities__0=Read' \
        'Storage__Destinations__0__Capabilities__1=Write' \
        'Storage__Destinations__0__Capabilities__2=Stat' \
        'Storage__Destinations__0__Capabilities__3=Delete' \
        'Storage__Destinations__1__Id=cloudru-cold' \
        'Storage__Destinations__1__Type=S3Compatible' \
        'Storage__Destinations__1__State=Enabled' \
        'Storage__Destinations__1__FailureDomain=cloudru-evolution-moscow' \
        'Storage__Destinations__1__TenantId=garagebalance' \
        'Storage__Destinations__1__Endpoint=https://s3.cloud.ru' \
        'Storage__Destinations__1__AllowedEndpointHosts__0=s3.cloud.ru' \
        "Storage__Destinations__1__Bucket=$bucket" \
        'Storage__Destinations__1__Prefix=garagebalance/staging/backups' \
        'Storage__Destinations__1__SigningRegion=ru-central-1' \
        'Storage__Destinations__1__ForcePathStyle=true' \
        'Storage__Destinations__1__CredentialSource=DefaultChain' \
        'Storage__Destinations__1__EncryptionMode=SseKms' \
        "Storage__Destinations__1__KmsKeyId=$kms_key_id" \
        'Storage__Destinations__1__PrivateAccess=true' \
        'Storage__Destinations__1__EncryptionAtRest=true' \
        'Storage__Destinations__1__Capabilities__0=Read' \
        'Storage__Destinations__1__Capabilities__1=Write' \
        'Storage__Destinations__1__Capabilities__2=Stat' \
        'Storage__Destinations__1__Capabilities__3=Delete' \
        'Storage__Destinations__1__Capabilities__4=ServerSideEncryption' \
        'Storage__Pools__0__Id=database-backups' \
        'Storage__Pools__0__DestinationIds__0=local-hot' \
        'Storage__Pools__0__DestinationIds__1=cloudru-cold' \
        'Storage__Policies__0__Id=database-backups' \
        'Storage__Policies__0__DataClass=DatabaseBackup' \
        'Storage__Policies__0__PoolId=database-backups' \
        'Storage__Policies__0__RequiredIndependentCopies=2' \
        'Storage__Policies__0__MinimumOffsiteCopies=1' \
        'Storage__Policies__0__DesiredCopies=2'
    } > "$temporary_env"
    chmod 600 "$temporary_env"
    mv -- "$temporary_env" "$CLOUD_ENV"
    install -d -o root -g root -m 755 "$DROP_IN_DIR"
    printf '[Service]\nEnvironmentFile=%s\n' "$CLOUD_ENV" > "$DROP_IN"
    chmod 644 "$DROP_IN"
    install -d -o garagebalance -g garagebalance -m 700 "$MIGRATION_DIR"
    printf '%s\n' \
      '[Unit]' \
      'Description=GarageBalance Cloud.ru storage migration (%i)' \
      'After=postgresql.service network-online.target' \
      'Wants=network-online.target' \
      '[Service]' \
      'Type=oneshot' \
      'User=garagebalance' \
      'Group=garagebalance' \
      "WorkingDirectory=$APP_ROOT/storage-tool" \
      "EnvironmentFile=$ENV_FILE" \
      "EnvironmentFile=$CLOUD_ENV" \
      'ExecStart=/usr/local/bin/garagebalance-storage-tool-run %i' \
      'TimeoutStartSec=4h' \
      'UMask=0077' \
      'NoNewPrivileges=true' \
      'PrivateTmp=true' \
      'ProtectSystem=strict' \
      "ReadWritePaths=$BACKUP_DIR $MIGRATION_DIR" \
      > "$TOOL_UNIT"
    chmod 644 "$TOOL_UNIT"
    systemctl daemon-reload
    systemctl restart "$SERVICE"
    wait_for_api
    trap - ERR
    echo 'cloud backup configuration installed; application is healthy'
    ;;
  run)
    [[ "$#" == 2 ]] || exit 64
    case "$2" in
      inventory|plan|copy|resume|delta-sync|verify|cutover-check|status) ;;
      *) echo 'unsupported migration command' >&2; exit 64 ;;
    esac
    [[ -f "$CLOUD_ENV" && -f "$TOOL_UNIT" ]] || exit 1
    if ! systemctl start "garagebalance-storage-tool@$2.service"; then
      journalctl -u "garagebalance-storage-tool@$2.service" -n 2000 -o cat --no-pager | cut -c 1-4000
      exit 1
    fi
    systemctl show "garagebalance-storage-tool@$2.service" \
      --property=Result --property=ExecMainStatus --no-pager
    ;;
  diagnose)
    [[ "$#" == 2 ]] || exit 64
    case "$2" in
      inventory|plan|copy|resume|delta-sync|verify|cutover-check|status) ;;
      *) exit 64 ;;
    esac
    journalctl -u "garagebalance-storage-tool@$2.service" -n 2000 -o cat --no-pager | cut -c 1-4000
    ;;
  schedule)
    [[ "$#" == 1 ]] || exit 64
    [[ -f "$CLOUD_ENV" && -f "$TOOL_UNIT" && -f "$MIGRATION_DIR/cloudru-backfill.json" ]] || exit 1
    printf '%s\n' \
      '[Unit]' \
      'Description=Synchronize GarageBalance database backups to Cloud.ru hourly' \
      '[Timer]' \
      'OnCalendar=hourly' \
      'Persistent=true' \
      'Unit=garagebalance-storage-tool@delta-sync.service' \
      '[Install]' \
      'WantedBy=timers.target' > "$SYNC_TIMER"
    chmod 644 "$SYNC_TIMER"
    systemctl daemon-reload
    systemctl enable --now garagebalance-storage-sync.timer
    ;;
  disable)
    [[ "$#" == 1 ]] || exit 64
    [[ -f "$CLOUD_ENV" && -f "$DROP_IN" ]] || exit 1
    systemctl disable --now garagebalance-storage-sync.timer 2>/dev/null || true
    rm -f -- "$DROP_IN" "$TOOL_UNIT" "$SYNC_TIMER" "$CLOUD_ENV"
    systemctl daemon-reload
    systemctl restart "$SERVICE"
    wait_for_api
    echo 'Cloud backup configuration disabled; application is healthy'
    ;;
  *) echo 'usage: inspect | audit-files | allow-legacy-read | apply <bucket> <kms-key-id> <tenant-id> | run <command> | diagnose <command> | schedule | disable' >&2; exit 64 ;;
esac
