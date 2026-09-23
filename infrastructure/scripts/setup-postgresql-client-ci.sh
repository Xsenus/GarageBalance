#!/usr/bin/env bash
set -euo pipefail

# The restore integration test must use the same major client as postgres:17.
# Repository setup follows https://www.postgresql.org/download/linux/ubuntu/.
if [[ "${GITHUB_ACTIONS:-}" != "true" || -z "${GITHUB_PATH:-}" ]]; then
  echo "This setup is restricted to an ephemeral GitHub Actions runner." >&2
  exit 1
fi
. /etc/os-release
if [[ "${ID:-}" != "ubuntu" || -z "${VERSION_CODENAME:-}" ]]; then
  echo "The PostgreSQL client setup requires an Ubuntu runner." >&2
  exit 1
fi

postgres_bin=/usr/lib/postgresql/17/bin
if [[ ! -x "$postgres_bin/pg_dump" || ! -x "$postgres_bin/pg_restore" ]]; then
  # Reuse the official, pinned signing key already used by the API Dockerfile.
  # Preserve a preconfigured PGDG source on hosted runners to avoid conflicting
  # Signed-By declarations for the same repository.
  if ! grep -RqsE 'https?://apt\.postgresql\.org/pub/repos/apt' /etc/apt/sources.list.d /etc/apt/sources.list 2>/dev/null; then
    script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
    key_source="$script_dir/../deployment/postgresql-archive-keyring.asc"
    printf '%s  %s\n' '0144068502a1eddd2a0280ede10ef607d1ec592ce819940991203941564e8e76' "$key_source" | sha256sum --check --strict
    sudo install -D -m 0644 "$key_source" /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc
    printf 'Types: deb\nURIs: https://apt.postgresql.org/pub/repos/apt\nSuites: %s-pgdg\nArchitectures: %s\nComponents: main\nSigned-By: /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc\n' \
      "$VERSION_CODENAME" "$(dpkg --print-architecture)" | sudo tee /etc/apt/sources.list.d/garagebalance-pgdg.sources >/dev/null
  fi
  sudo apt-get update
  sudo apt-get install --yes --no-install-recommends postgresql-client-17
fi

for tool in pg_dump pg_restore; do
  tool_version="$("$postgres_bin/$tool" --version)"
  printf '%s\n' "$tool_version"
  if [[ ! "$tool_version" =~ ^${tool}[[:space:]]\(PostgreSQL\)[[:space:]]17\. ]]; then
    echo "Expected PostgreSQL 17 client tools." >&2
    exit 1
  fi
done
printf '%s\n' "$postgres_bin" >> "$GITHUB_PATH"
