#!/usr/bin/env bash
set -Eeuo pipefail

if [[ "$(id -u)" != "0" ]]; then
  echo "This installer must run as root." >&2
  exit 1
fi

source_dir="${1:-}"
if [[ -z "$source_dir" || ! -d "$source_dir" ]]; then
  echo "Usage: $0 <repository-or-package-root>" >&2
  exit 64
fi

timestamp="$(date +%Y%m%d-%H%M%S)"
site_target="/etc/nginx/sites-available/garagebalance-staging"
enabled_site_target="/etc/nginx/sites-enabled/garagebalance-staging"
service_target="/etc/systemd/system/garagebalance-staging.service"
site_backup="${site_target}.backup-${timestamp}"
nginx_backup_dir="/etc/nginx/config-backups"
enabled_site_backup="${nginx_backup_dir}/garagebalance-staging-enabled.backup-${timestamp}"
service_backup="${service_target}.backup-${timestamp}"
enabled_site_is_regular=0
installation_complete=0
barrier_backup_dir="$(mktemp -d /tmp/garagebalance-barrier-install.XXXXXXXX)"
health_target=/etc/systemd/system/garagebalance-healthcheck.service
barrier_target=/usr/local/bin/garagebalance-maintenance-barrier
sync_dropin=/etc/systemd/system/garagebalance-storage-tool@.service.d/70-maintenance-barrier.conf
for backup_name in health barrier sync; do
  case "$backup_name" in
    health) backup_target=$health_target ;;
    barrier) backup_target=$barrier_target ;;
    sync) backup_target=$sync_dropin ;;
  esac
  if [[ -e "$backup_target" || -L "$backup_target" ]]; then
    cp -a -- "$backup_target" "$barrier_backup_dir/$backup_name"
  fi
done

cp -a "$site_target" "$site_backup"
cp -a "$service_target" "$service_backup"
if [[ -e "$enabled_site_target" && ! -L "$enabled_site_target" ]]; then
  install -d -o root -g root -m 0700 "$nginx_backup_dir"
  cp -a "$enabled_site_target" "$enabled_site_backup"
  enabled_site_is_regular=1
fi

rollback() {
  local exit_code=$?
  if (( installation_complete == 0 )); then
    cp -a "$site_backup" "$site_target"
    cp -a "$service_backup" "$service_target"
    for backup_name in health barrier sync; do
      case "$backup_name" in
        health) backup_target=$health_target ;;
        barrier) backup_target=$barrier_target ;;
        sync) backup_target=$sync_dropin ;;
      esac
      if [[ -e "$barrier_backup_dir/$backup_name" || -L "$barrier_backup_dir/$backup_name" ]]; then
        cp -a -- "$barrier_backup_dir/$backup_name" "$backup_target"
      else
        rm -f -- "$backup_target"
      fi
    done
    if (( enabled_site_is_regular == 1 )); then
      rm -f "$enabled_site_target"
      cp -a "$enabled_site_backup" "$enabled_site_target"
    fi
    systemctl daemon-reload || true
    nginx -t && systemctl reload nginx || true
    systemctl restart garagebalance-staging.service || true
    echo "Installation failed; nginx and service configuration were rolled back." >&2
  fi
  rm -f -- "$barrier_backup_dir/health" "$barrier_backup_dir/barrier" "$barrier_backup_dir/sync"
  rmdir -- "$barrier_backup_dir"
  return "$exit_code"
}
trap rollback EXIT

if ! command -v mdb-tables >/dev/null 2>&1; then
  apt-get update
  DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends mdbtools
  rm -rf /var/lib/apt/lists/* /var/cache/apt/*
fi

install -d -o garagebalance -g garagebalance -m 0750 /var/lib/garagebalance-staging
install -d -o root -g garagebalance -m 0750 /var/lib/garagebalance-maintenance
for barrier_lock in deploy activity; do
  barrier_file="/var/lib/garagebalance-maintenance/${barrier_lock}.lock"
  [[ ! -L "$barrier_file" ]] || { echo 'Barrier lock is a symlink' >&2; exit 1; }
  if [[ ! -e "$barrier_file" ]]; then
    (set -o noclobber; umask 027; : > "$barrier_file")
    chown root:garagebalance "$barrier_file"
    chmod 0640 "$barrier_file"
  fi
  [[ -f "$barrier_file" && "$(stat -c '%u:%g:%a:%h' "$barrier_file")" == "0:$(id -g garagebalance):640:1" ]] || {
    echo 'Barrier lock metadata is invalid' >&2; exit 1;
  }
done
install -o root -g root -m 0755 "$source_dir/infrastructure/scripts/staging-maintenance-barrier.sh" /usr/local/bin/garagebalance-maintenance-barrier
if [[ -f /etc/systemd/system/garagebalance-storage-tool@.service ]]; then
  install -d -o root -g root -m 0755 /etc/systemd/system/garagebalance-storage-tool@.service.d
  printf '[Service]\nExecStart=\nExecStart=/usr/local/bin/garagebalance-maintenance-barrier storage %%i\n' \
    > /etc/systemd/system/garagebalance-storage-tool@.service.d/70-maintenance-barrier.conf
  chmod 0644 /etc/systemd/system/garagebalance-storage-tool@.service.d/70-maintenance-barrier.conf
fi
install -d -o www-data -g adm -m 0750 /var/log/garagebalance-nginx
install -o root -g root -m 0644 "$source_dir/infrastructure/deployment/garagebalance-staging.nginx.conf" "$site_target"
if (( enabled_site_is_regular == 1 )); then
  rm -f "$enabled_site_target"
  ln -s "$site_target" "$enabled_site_target"
fi
install -o root -g root -m 0644 "$source_dir/infrastructure/deployment/garagebalance-staging.service" "$service_target"
install -o root -g root -m 0644 "$source_dir/infrastructure/deployment/garagebalance-healthcheck.service" /etc/systemd/system/garagebalance-healthcheck.service
install -o root -g root -m 0644 "$source_dir/infrastructure/deployment/garagebalance-healthcheck.timer" /etc/systemd/system/garagebalance-healthcheck.timer
install -o root -g root -m 0644 "$source_dir/infrastructure/deployment/garagebalance-performance-check.service" /etc/systemd/system/garagebalance-performance-check.service
install -o root -g root -m 0644 "$source_dir/infrastructure/deployment/garagebalance-performance-check.timer" /etc/systemd/system/garagebalance-performance-check.timer
install -o root -g root -m 0644 "$source_dir/infrastructure/deployment/garagebalance.logrotate" /etc/logrotate.d/garagebalance
install -o root -g root -m 0755 "$source_dir/infrastructure/scripts/garagebalance-healthcheck.sh" /usr/local/bin/garagebalance-healthcheck
install -o root -g root -m 0755 "$source_dir/infrastructure/scripts/garagebalance-performance-check.sh" /usr/local/bin/garagebalance-performance-check

nginx -t

systemctl daemon-reload
systemctl reload nginx
systemctl restart garagebalance-staging.service

health_ready=0
for _ in {1..30}; do
  if curl --noproxy 127.0.0.1 --fail --silent --max-time 3 \
    -H "Host: sgk.blagodaty.ru" http://127.0.0.1:3101/health/ready >/dev/null; then
    health_ready=1
    break
  fi
  sleep 1
done
if (( health_ready == 0 )); then
  echo "GarageBalance did not become healthy within 30 seconds." >&2
  exit 1
fi

systemctl enable --now garagebalance-healthcheck.timer garagebalance-performance-check.timer
systemctl start garagebalance-healthcheck.service
systemctl start garagebalance-performance-check.service

curl --http2 --compressed --fail --silent --show-error --max-time 20 https://sgk.blagodaty.ru/ >/dev/null
logrotate --debug /etc/logrotate.conf >/dev/null 2>&1
systemctl start logrotate.service
systemctl reset-failed logrotate.service

installation_complete=1
trap - EXIT

printf 'Installed VPS performance configuration; nginxBackup=%s; serviceBackup=%s\n' \
  "$site_backup" \
  "$service_backup"
