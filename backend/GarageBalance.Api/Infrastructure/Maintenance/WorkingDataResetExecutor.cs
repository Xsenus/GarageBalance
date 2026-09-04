using System.Data.Common;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GarageBalance.Api.Infrastructure.Maintenance;

public sealed class WorkingDataResetExecutor(GarageBalanceDbContext context)
{
    private static readonly string[] PreservedTables =
    [
        "app_users",
        "app_roles",
        "app_user_roles",
        "income_types",
        "expense_types",
        "tariffs",
        "measurement_units",
        "charge_service_settings",
        "charge_service_tariff_versions",
        "irregular_payments",
        "funds",
        "application_settings",
        "integration_secret_settings",
        "app_releases"
    ];

    private static readonly string[] WorkingDataTables =
    [
        "form_states",
        "access_import_created_records",
        "access_import_quarantine_items",
        "access_import_row_fingerprints",
        "access_import_run_log_entries",
        "access_import_runs",
        "garage_report_quick_list_garages",
        "garage_report_quick_lists",
        "accrual_payment_allocations",
        "fund_operations",
        "financial_operations",
        "supplier_accruals",
        "staff_salary_adjustments",
        "staff_salary_rate_periods",
        "staff_employment_periods",
        "accruals",
        "meter_readings",
        "meter_devices",
        "cash_bank_transfers",
        "cash_bank_balance_operations",
        "opening_balance_adjustments",
        "fee_campaign_garages",
        "fee_campaigns",
        "supplier_contacts",
        "suppliers",
        "supplier_groups",
        "staff_members",
        "staff_departments",
        "garages",
        "owners",
        "audit_events"
    ];

    public async Task<WorkingDataResetResult> ResetAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(1732042601);",
            cancellationToken);

        var preservedBefore = await ReadPreservedDataSnapshotAsync(cancellationToken);
        var clearedRowCount = await ReadWorkingDataRowCountAsync(cancellationToken);
        await ClearAsync(cancellationToken);
        var preservedAfter = await ReadPreservedDataSnapshotAsync(cancellationToken);
        var remainingRows = await ReadWorkingDataRowCountAsync(cancellationToken);
        var fundBalance = await context.Funds.SumAsync(item => (decimal?)item.Balance, cancellationToken) ?? 0m;
        var generalPoolBalance = await ReadGeneralPoolBalanceAsync(cancellationToken);
        var auditEventCount = await context.AuditEvents.CountAsync(cancellationToken);

        var result = new WorkingDataResetResult(
            preservedBefore == preservedAfter
                && remainingRows == 0
                && fundBalance == 0m
                && generalPoolBalance == 0m
                && auditEventCount == 0,
            preservedBefore,
            preservedAfter,
            clearedRowCount,
            fundBalance,
            generalPoolBalance,
            auditEventCount);
        if (!result.IsClean)
        {
            throw new InvalidOperationException("Staging working data reset verification failed.");
        }

        await transaction.CommitAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return result;
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            TRUNCATE TABLE
                form_states,
                access_import_created_records,
                access_import_quarantine_items,
                access_import_row_fingerprints,
                access_import_run_log_entries,
                access_import_runs,
                garage_report_quick_list_garages,
                garage_report_quick_lists,
                accrual_payment_allocations,
                fund_operations,
                financial_operations,
                supplier_accruals,
                staff_salary_adjustments,
                staff_salary_rate_periods,
                staff_employment_periods,
                accruals,
                meter_readings,
                meter_devices,
                cash_bank_transfers,
                cash_bank_balance_operations,
                opening_balance_adjustments,
                fee_campaign_garages,
                fee_campaigns,
                supplier_contacts,
                suppliers,
                supplier_groups,
                staff_members,
                staff_departments,
                garages,
                owners
            CASCADE;
            TRUNCATE TABLE audit_events;
            """;
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        await context.Funds.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(item => item.Balance, 0m)
                .SetProperty(item => item.UpdatedAtUtc, item => item.CreatedAtUtc)
                .SetProperty(item => item.Version, Guid.NewGuid()),
            cancellationToken);
        context.ChangeTracker.Clear();
    }

    private async Task<PreservedDataSnapshot> ReadPreservedDataSnapshotAsync(CancellationToken cancellationToken)
    {
        var counts = await QueryCountsAsync(PreservedTables, cancellationToken);
        return new PreservedDataSnapshot(
            counts["app_users"],
            counts["app_roles"],
            counts["app_user_roles"],
            counts["income_types"],
            counts["expense_types"],
            counts["tariffs"],
            counts["measurement_units"],
            counts["charge_service_settings"],
            counts["charge_service_tariff_versions"],
            counts["irregular_payments"],
            counts["funds"],
            counts["application_settings"],
            counts["integration_secret_settings"],
            counts["app_releases"]);
    }

    private async Task<long> ReadWorkingDataRowCountAsync(CancellationToken cancellationToken)
    {
        var counts = await QueryCountsAsync(WorkingDataTables, cancellationToken);
        return counts.Values.Sum();
    }

    private async Task<decimal> ReadGeneralPoolBalanceAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                COALESCE((SELECT SUM(CASE WHEN "Direction" = 'increase' THEN "Amount" ELSE -"Amount" END)
                    FROM cash_bank_balance_operations), 0) +
                COALESCE((SELECT SUM(CASE WHEN "OperationKind" = 'income' THEN "Amount" ELSE -"Amount" END)
                    FROM financial_operations WHERE NOT "IsCanceled"), 0)
            """;
        return await ExecuteScalarAsync<decimal>(sql, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, long>> QueryCountsAsync(
        IReadOnlyList<string> tables,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            result[table] = await ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {table};", cancellationToken);
        }

        return result;
    }

    private async Task<T> ExecuteScalarAsync<T>(string sql, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return (T)Convert.ChangeType(value!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed record PreservedDataSnapshot(
    long Users,
    long Roles,
    long UserRoles,
    long IncomeTypes,
    long ExpenseTypes,
    long Tariffs,
    long MeasurementUnits,
    long ChargeServiceSettings,
    long ChargeServiceTariffVersions,
    long IrregularPayments,
    long Funds,
    long ApplicationSettings,
    long IntegrationSecretSettings,
    long AppReleases);

public sealed record WorkingDataResetResult(
    bool IsClean,
    PreservedDataSnapshot PreservedBefore,
    PreservedDataSnapshot PreservedAfter,
    long ClearedRowCount,
    decimal FundBalance,
    decimal GeneralPoolBalance,
    long AuditEventCount);
