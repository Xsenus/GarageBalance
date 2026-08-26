#!/usr/bin/env bash
set -Eeuo pipefail

APP_ROOT="/opt/garagebalance-staging"
ENV_FILE="/etc/garagebalance-staging.env"
EXPECTED_DATABASE="garagebalance_staging"
EXPECTED_CONFIRMATION="AUDIT GARAGEBALANCE STAGING"
BACKUP_DIR="${APP_ROOT}/backups"
confirmation="${1:-}"
audit_database=""
critical_findings=0
warning_findings=0

log() {
  printf '%s %s\n' "$(date --iso-8601=seconds)" "$*"
}

cleanup() {
  if [[ -n "$audit_database" ]]; then
    sudo -u postgres dropdb --if-exists --force "$audit_database" >/dev/null 2>&1 || true
    audit_database=""
  fi
}

fail() {
  log "auditStatus=failed; reason=$*"
  exit 1
}

trap cleanup EXIT

[[ "$(id -u)" == "0" ]] || fail "script must run as root"
[[ "$confirmation" == "$EXPECTED_CONFIRMATION" ]] || fail "confirmation phrase is invalid"
[[ -f "$ENV_FILE" ]] || fail "environment file was not found"

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

[[ "$database_name" == "$EXPECTED_DATABASE" ]] ||
  fail "refusing to audit an unexpected database"

install -d -o garagebalance -g garagebalance -m 750 "$BACKUP_DIR"
timestamp="$(date +%Y%m%d-%H%M%S)"
backup_file="${BACKUP_DIR}/garagebalance_${timestamp}_before_integrity_audit.pgdump"
audit_database="garagebalance_integrity_audit_${timestamp//-/}_$$"

log "backupStatus=started"
sudo -u postgres pg_dump --format=custom "$database_name" > "$backup_file"
[[ -s "$backup_file" ]] || fail "PostgreSQL backup was not created"
chown garagebalance:garagebalance "$backup_file"
chmod 600 "$backup_file"
log "backupStatus=completed"

log "restoreStatus=started"
sudo -u postgres createdb "$audit_database"
sudo -u postgres pg_restore \
  --exit-on-error \
  --no-owner \
  --no-privileges \
  --dbname="$audit_database" \
  < "$backup_file" >/dev/null
log "restoreStatus=completed"

run_check() {
  local check_name="$1"
  local severity="$2"
  local query="$3"
  local count

  count="$(
    sudo -u postgres psql \
      --set ON_ERROR_STOP=1 \
      --tuples-only \
      --no-align \
      --dbname="$audit_database" \
      --command="SET statement_timeout = '60s'; SET default_transaction_read_only = on; ${query}" \
      | tail -n 1 \
      | tr -d '[:space:]'
  )"

  [[ "$count" =~ ^[0-9]+$ ]] || fail "check did not return a count: ${check_name}"
  log "check=${check_name}; severity=${severity}; findings=${count}"

  if (( count > 0 )); then
    if [[ "$severity" == "critical" ]]; then
      critical_findings=$((critical_findings + count))
    else
      warning_findings=$((warning_findings + count))
    fi
  fi
}

run_check "unvalidated_constraints" "critical" \
  "SELECT count(*) FROM pg_catalog.pg_constraint WHERE connamespace = 'public'::regnamespace AND NOT convalidated;"
run_check "invalid_indexes" "critical" \
  "SELECT count(*) FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND NOT i.indisvalid;"
run_check "foreign_key_violations" "critical" \
  "SELECT count(*) FROM (SELECT conname FROM pg_catalog.pg_constraint WHERE connamespace = 'public'::regnamespace AND contype = 'f' AND NOT convalidated) q;"

run_check "duplicate_active_garage_numbers" "critical" \
  "SELECT count(*) FROM (SELECT lower(btrim(\"Number\")) FROM garages WHERE NOT \"IsArchived\" GROUP BY lower(btrim(\"Number\")) HAVING count(*) > 1) q;"
run_check "duplicate_active_service_names" "critical" \
  "SELECT count(*) FROM (SELECT lower(btrim(\"Name\")) FROM charge_service_settings WHERE NOT \"IsArchived\" GROUP BY lower(btrim(\"Name\")) HAVING count(*) > 1) q;"
run_check "duplicate_active_income_names" "critical" \
  "SELECT count(*) FROM (SELECT lower(btrim(\"Name\")) FROM income_types WHERE NOT \"IsArchived\" GROUP BY lower(btrim(\"Name\")) HAVING count(*) > 1) q;"
run_check "duplicate_active_expense_names" "critical" \
  "SELECT count(*) FROM (SELECT lower(btrim(\"Name\")) FROM expense_types WHERE NOT \"IsArchived\" GROUP BY lower(btrim(\"Name\")) HAVING count(*) > 1) q;"
run_check "duplicate_active_supplier_names" "critical" \
  "SELECT count(*) FROM (SELECT \"GroupId\", lower(btrim(\"Name\")) FROM suppliers WHERE NOT \"IsArchived\" GROUP BY \"GroupId\", lower(btrim(\"Name\")) HAVING count(*) > 1) q;"
run_check "duplicate_active_fund_names" "critical" \
  "SELECT count(*) FROM (SELECT lower(btrim(\"NormalizedName\")) FROM funds WHERE NOT \"IsArchived\" GROUP BY lower(btrim(\"NormalizedName\")) HAVING count(*) > 1) q;"

run_check "invalid_tariff_periods" "critical" \
  "SELECT count(*) FROM charge_service_tariff_versions WHERE NOT \"IsArchived\" AND \"EffectiveTo\" IS NOT NULL AND \"EffectiveTo\" < \"EffectiveFrom\";"
run_check "overlapping_tariff_periods" "critical" \
  "SELECT count(*) FROM charge_service_tariff_versions a JOIN charge_service_tariff_versions b ON a.\"ChargeServiceSettingId\" = b.\"ChargeServiceSettingId\" AND a.\"EffectiveFrom\" < b.\"EffectiveFrom\" AND NOT a.\"IsArchived\" AND NOT b.\"IsArchived\" AND daterange(a.\"EffectiveFrom\", COALESCE(a.\"EffectiveTo\" + 1, 'infinity'::date), '[)') && daterange(b.\"EffectiveFrom\", COALESCE(b.\"EffectiveTo\" + 1, 'infinity'::date), '[)');"

run_check "duplicate_regular_accruals" "critical" \
  "SELECT count(*) FROM (SELECT \"GarageId\", \"IncomeTypeId\", \"AccountingMonth\", \"Source\" FROM accruals WHERE NOT \"IsCanceled\" AND \"IrregularPaymentId\" IS NULL AND \"FeeCampaignId\" IS NULL AND \"Basis\" IS NULL GROUP BY \"GarageId\", \"IncomeTypeId\", \"AccountingMonth\", \"Source\" HAVING count(*) > 1) q;"
run_check "duplicate_irregular_accruals" "critical" \
  "SELECT count(*) FROM (SELECT \"GarageId\", \"IrregularPaymentId\", \"AccountingMonth\" FROM accruals WHERE NOT \"IsCanceled\" AND \"IrregularPaymentId\" IS NOT NULL GROUP BY \"GarageId\", \"IrregularPaymentId\", \"AccountingMonth\" HAVING count(*) > 1) q;"
run_check "duplicate_fee_campaign_accruals" "critical" \
  "SELECT count(*) FROM (SELECT \"GarageId\", \"FeeCampaignId\", \"AccountingMonth\" FROM accruals WHERE NOT \"IsCanceled\" AND \"FeeCampaignId\" IS NOT NULL GROUP BY \"GarageId\", \"FeeCampaignId\", \"AccountingMonth\" HAVING count(*) > 1) q;"
run_check "invalid_accrual_amounts" "critical" \
  "SELECT count(*) FROM accruals WHERE NOT \"IsCanceled\" AND \"Amount\" < 0;"
run_check "invalid_overdue_dates" "critical" \
  "SELECT count(*) FROM accruals WHERE NOT \"IsCanceled\" AND \"OverdueFromDate\" IS NOT NULL AND \"OverdueFromDate\" <= \"DueDate\";"
run_check "backdated_accrual_due_dates" "warning" \
  "SELECT count(*) FROM accruals WHERE NOT \"IsCanceled\" AND \"DueDate\" < \"AccountingMonth\";"

run_check "nonpositive_active_financial_operations" "critical" \
  "SELECT count(*) FROM financial_operations WHERE NOT \"IsCanceled\" AND \"Amount\" <= 0;"
run_check "income_operations_without_garage" "critical" \
  "SELECT count(*) FROM financial_operations WHERE NOT \"IsCanceled\" AND \"OperationKind\" = 'income' AND \"GarageId\" IS NULL;"
run_check "income_operations_without_type_or_allocation_evidence" "critical" \
  "SELECT count(*) FROM financial_operations f WHERE NOT f.\"IsCanceled\" AND f.\"OperationKind\" = 'income' AND f.\"IncomeTypeId\" IS NULL AND NOT EXISTS (SELECT 1 FROM accrual_payment_allocations p JOIN accruals a ON a.\"Id\" = p.\"AccrualId\" WHERE p.\"FinancialOperationId\" = f.\"Id\" AND a.\"IncomeTypeId\" IS NOT NULL);"
run_check "legacy_income_types_inferred_from_allocations" "warning" \
  "SELECT count(*) FROM financial_operations f WHERE NOT f.\"IsCanceled\" AND f.\"OperationKind\" = 'income' AND f.\"IncomeTypeId\" IS NULL AND EXISTS (SELECT 1 FROM accrual_payment_allocations p JOIN accruals a ON a.\"Id\" = p.\"AccrualId\" WHERE p.\"FinancialOperationId\" = f.\"Id\" AND a.\"IncomeTypeId\" IS NOT NULL);"
run_check "income_operations_with_expense_fields" "critical" \
  "SELECT count(*) FROM financial_operations WHERE NOT \"IsCanceled\" AND \"OperationKind\" = 'income' AND (\"SupplierId\" IS NOT NULL OR \"StaffMemberId\" IS NOT NULL OR \"ExpenseTypeId\" IS NOT NULL);"
run_check "expense_operation_field_conflicts" "critical" \
  "SELECT count(*) FROM financial_operations WHERE NOT \"IsCanceled\" AND \"OperationKind\" = 'expense' AND (\"GarageId\" IS NOT NULL OR \"IncomeTypeId\" IS NOT NULL OR \"ExpenseTypeId\" IS NULL);"
run_check "exact_duplicate_financial_operations" "warning" \
  "SELECT count(*) FROM (SELECT \"OperationKind\", \"OperationDate\", \"AccountingMonth\", \"Amount\", \"ReceiptBatchId\", \"GarageId\", \"IncomeTypeId\", \"FeeCampaignId\", \"IrregularPaymentId\", \"SupplierId\", \"StaffMemberId\", \"ExpenseTypeId\", \"ExpenseFundId\", coalesce(\"ExpensePaymentType\", ''), coalesce(\"ExpensePaymentSource\", ''), coalesce(\"CounterpartyName\", ''), coalesce(\"DocumentNumber\", ''), coalesce(\"Comment\", '') FROM financial_operations WHERE NOT \"IsCanceled\" GROUP BY \"OperationKind\", \"OperationDate\", \"AccountingMonth\", \"Amount\", \"ReceiptBatchId\", \"GarageId\", \"IncomeTypeId\", \"FeeCampaignId\", \"IrregularPaymentId\", \"SupplierId\", \"StaffMemberId\", \"ExpenseTypeId\", \"ExpenseFundId\", coalesce(\"ExpensePaymentType\", ''), coalesce(\"ExpensePaymentSource\", ''), coalesce(\"CounterpartyName\", ''), coalesce(\"DocumentNumber\", ''), coalesce(\"Comment\", '') HAVING count(*) > 1) q;"
run_check "allocations_to_invalid_operations" "critical" \
  "SELECT count(*) FROM accrual_payment_allocations p JOIN financial_operations f ON f.\"Id\" = p.\"FinancialOperationId\" JOIN accruals a ON a.\"Id\" = p.\"AccrualId\" WHERE p.\"IsActive\" AND (f.\"IsCanceled\" OR a.\"IsCanceled\" OR f.\"OperationKind\" <> 'income' OR f.\"GarageId\" IS DISTINCT FROM a.\"GarageId\");"
run_check "allocation_totals_above_operation" "critical" \
  "SELECT count(*) FROM (SELECT p.\"FinancialOperationId\" FROM accrual_payment_allocations p JOIN financial_operations f ON f.\"Id\" = p.\"FinancialOperationId\" WHERE p.\"IsActive\" GROUP BY p.\"FinancialOperationId\", f.\"Amount\" HAVING sum(p.\"Amount\") > f.\"Amount\") q;"
run_check "nonpositive_active_allocations" "critical" \
  "SELECT count(*) FROM accrual_payment_allocations WHERE \"IsActive\" AND \"Amount\" <= 0;"
run_check "allocation_income_type_mismatches" "critical" \
  "SELECT count(*) FROM accrual_payment_allocations p JOIN financial_operations f ON f.\"Id\" = p.\"FinancialOperationId\" JOIN accruals a ON a.\"Id\" = p.\"AccrualId\" WHERE p.\"IsActive\" AND NOT f.\"IsCanceled\" AND NOT a.\"IsCanceled\" AND f.\"IncomeTypeId\" IS NOT NULL AND f.\"IncomeTypeId\" <> a.\"IncomeTypeId\";"

run_check "duplicate_meter_readings" "critical" \
  "SELECT count(*) FROM (SELECT \"GarageId\", \"MeterKind\", \"AccountingMonth\" FROM meter_readings WHERE NOT \"IsCanceled\" GROUP BY \"GarageId\", \"MeterKind\", \"AccountingMonth\" HAVING count(*) > 1) q;"
run_check "invalid_meter_consumption" "critical" \
  "SELECT count(*) FROM meter_readings WHERE NOT \"IsCanceled\" AND (\"CurrentValue\" < 0 OR \"PreviousValue\" < 0 OR \"Consumption\" < 0);"
run_check "multiple_active_meter_devices" "critical" \
  "SELECT count(*) FROM (SELECT \"GarageId\", \"MeterKind\" FROM meter_devices WHERE \"RemovedOn\" IS NULL GROUP BY \"GarageId\", \"MeterKind\" HAVING count(*) > 1) q;"

run_check "invalid_fund_operation_math" "critical" \
  "SELECT count(*) FROM fund_operations WHERE NOT \"IsCanceled\" AND (\"Amount\" <= 0 OR (\"OperationKind\" = 'deposit' AND \"BalanceAfter\" <> \"BalanceBefore\" + \"Amount\") OR (\"OperationKind\" = 'withdraw' AND \"BalanceAfter\" <> \"BalanceBefore\" - \"Amount\") OR \"OperationKind\" NOT IN ('deposit', 'withdraw'));"
run_check "fund_operation_chain_breaks" "warning" \
  "SELECT count(*) FROM (SELECT \"FundId\", \"CreatedAtUtc\", \"BalanceBefore\", lag(\"CreatedAtUtc\") OVER (PARTITION BY \"FundId\" ORDER BY \"CreatedAtUtc\", \"Id\") AS previous_created_at, lag(\"BalanceAfter\") OVER (PARTITION BY \"FundId\" ORDER BY \"CreatedAtUtc\", \"Id\") AS previous_balance_after FROM fund_operations) q WHERE previous_created_at < \"CreatedAtUtc\" AND \"BalanceBefore\" <> previous_balance_after;"
run_check "fund_operation_same_timestamp_order" "warning" \
  "SELECT count(*) FROM (SELECT \"FundId\", \"CreatedAtUtc\", \"BalanceBefore\", lag(\"CreatedAtUtc\") OVER (PARTITION BY \"FundId\" ORDER BY \"CreatedAtUtc\", \"Id\") AS previous_created_at, lag(\"BalanceAfter\") OVER (PARTITION BY \"FundId\" ORDER BY \"CreatedAtUtc\", \"Id\") AS previous_balance_after FROM fund_operations) q WHERE previous_created_at = \"CreatedAtUtc\" AND \"BalanceBefore\" <> previous_balance_after;"
run_check "fund_balance_mismatch" "critical" \
  "SELECT count(*) FROM funds f JOIN LATERAL (SELECT o.\"BalanceAfter\" FROM fund_operations o WHERE o.\"FundId\" = f.\"Id\" ORDER BY o.\"CreatedAtUtc\" DESC, o.\"Id\" DESC LIMIT 1) latest ON true WHERE f.\"Balance\" <> latest.\"BalanceAfter\";"
run_check "invalid_fee_campaign_dates" "critical" \
  "SELECT count(*) FROM fee_campaigns WHERE NOT \"IsArchived\" AND \"EndsOn\" IS NOT NULL AND \"EndsOn\" < \"StartsOn\";"
run_check "fee_campaign_income_mismatch" "critical" \
  "SELECT count(*) FROM accruals a JOIN fee_campaigns c ON c.\"Id\" = a.\"FeeCampaignId\" WHERE NOT a.\"IsCanceled\" AND a.\"IncomeTypeId\" <> c.\"IncomeTypeId\";"

run_check "codex_marked_business_records" "critical" \
  "SELECT (SELECT count(*) FROM garages WHERE coalesce(\"Number\", '') ILIKE '%codex%' OR coalesce(\"Comment\", '') ILIKE '%codex%') + (SELECT count(*) FROM suppliers WHERE coalesce(\"Name\", '') ILIKE '%codex%' OR coalesce(\"Comment\", '') ILIKE '%codex%') + (SELECT count(*) FROM accruals WHERE coalesce(\"Basis\", '') ILIKE '%codex%' OR coalesce(\"Comment\", '') ILIKE '%codex%') + (SELECT count(*) FROM financial_operations WHERE coalesce(\"DocumentNumber\", '') ILIKE '%codex%' OR coalesce(\"CounterpartyName\", '') ILIKE '%codex%' OR coalesce(\"Comment\", '') ILIKE '%codex%') + (SELECT count(*) FROM meter_readings WHERE coalesce(\"Comment\", '') ILIKE '%codex%') + (SELECT count(*) FROM fee_campaigns WHERE coalesce(\"Name\", '') ILIKE '%codex%' OR coalesce(\"Goal\", '') ILIKE '%codex%' OR coalesce(\"ClosureComment\", '') ILIKE '%codex%') + (SELECT count(*) FROM fund_operations WHERE coalesce(\"Reason\", '') ILIKE '%codex%');"

log "auditStatus=completed; criticalFindings=${critical_findings}; warningFindings=${warning_findings}"
if (( critical_findings > 0 )); then
  exit 3
fi
