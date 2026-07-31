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
    if (( enabled_site_is_regular == 1 )); then
      rm -f "$enabled_site_target"
      cp -a "$enabled_site_backup" "$enabled_site_target"
    fi
    systemctl daemon-reload || true
    nginx -t && systemctl reload nginx || true
    systemctl restart garagebalance-staging.service || true
    echo "Installation failed; nginx and service configuration were rolled back." >&2
  fi
  return "$exit_code"
}
trap rollback EXIT

install -d -o garagebalance -g garagebalance -m 0750 /var/lib/garagebalance-staging
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
    -H "Host: sgk.blagodaty.ru" http://127.0.0.1:3101/health >/dev/null; then
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
logrotate --debug /etc/logrotate.d/garagebalance >/dev/null

installation_complete=1
trap - EXIT

printf 'Installed VPS performance configuration; nginxBackup=%s; serviceBackup=%s\n' \
  "$site_backup" \
  "$service_backup"
