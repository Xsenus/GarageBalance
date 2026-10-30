#!/usr/bin/env bash
set -Eeuo pipefail

# Read-only identity guard, before uploads or release application. Credentials and
# the configured secret host value are never printed.
expected_address="31.192.110.221"
expected_name="questroom"
[[ -n "${VPS_HOST:-}" ]] || { echo "Staging target is not configured." >&2; exit 64; }
resolved="$(getent ahostsv4 "$VPS_HOST" | awk '{print $1}' | sort -u)"
[[ "$resolved" == "$expected_address" ]] || { echo "Staging target address does not match the authorized VPS." >&2; exit 65; }
identity="$(ssh -o BatchMode=yes garagebalance-staging 'printf "%s\n" "$(hostname -s | tr "[:upper:]" "[:lower:]")"; printf "%s\n" "$SSH_CONNECTION" | awk "{print \$3}"')"
[[ "$identity" == "${expected_name}"$'\n'"${expected_address}" ]] || { echo "Staging SSH host identity does not match the authorized VPS." >&2; exit 65; }
echo "Confirmed staging target: QuestRoom 31.192.110.221."
