using System.Collections.Concurrent;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Common;

internal static class PostgreSqlLegacyModelCompatibility
{
    private static readonly ConcurrentDictionary<string, string[]> AddedColumnRemovals = new(StringComparer.Ordinal);
    private static readonly (string Add, string Remove)[] CurrentModelColumnStatements =
    [
        ("ALTER TABLE IF EXISTS meter_devices ADD COLUMN IF NOT EXISTS \"CanceledAtUtc\" timestamp with time zone NULL", "ALTER TABLE IF EXISTS meter_devices DROP COLUMN IF EXISTS \"CanceledAtUtc\""),
        ("ALTER TABLE IF EXISTS meter_devices ADD COLUMN IF NOT EXISTS \"CancellationReason\" character varying(500) NULL", "ALTER TABLE IF EXISTS meter_devices DROP COLUMN IF EXISTS \"CancellationReason\""),
        ("ALTER TABLE IF EXISTS meter_devices ADD COLUMN IF NOT EXISTS \"HasReplacementSnapshot\" boolean NOT NULL DEFAULT FALSE", "ALTER TABLE IF EXISTS meter_devices DROP COLUMN IF EXISTS \"HasReplacementSnapshot\""),
        ("ALTER TABLE IF EXISTS meter_devices ADD COLUMN IF NOT EXISTS \"IsCanceled\" boolean NOT NULL DEFAULT FALSE", "ALTER TABLE IF EXISTS meter_devices DROP COLUMN IF EXISTS \"IsCanceled\""),
        ("ALTER TABLE IF EXISTS meter_devices ADD COLUMN IF NOT EXISTS \"PreviousReadingSnapshotJson\" jsonb NULL", "ALTER TABLE IF EXISTS meter_devices DROP COLUMN IF EXISTS \"PreviousReadingSnapshotJson\""),
        ("ALTER TABLE IF EXISTS meter_devices ADD COLUMN IF NOT EXISTS \"ReplacedDeviceId\" uuid NULL", "ALTER TABLE IF EXISTS meter_devices DROP COLUMN IF EXISTS \"ReplacedDeviceId\""),
        ("ALTER TABLE IF EXISTS meter_devices ADD COLUMN IF NOT EXISTS \"ReplacementAccountingMonth\" date NULL", "ALTER TABLE IF EXISTS meter_devices DROP COLUMN IF EXISTS \"ReplacementAccountingMonth\""),
        ("ALTER TABLE IF EXISTS meter_devices ADD COLUMN IF NOT EXISTS \"ReplacementReadingId\" uuid NULL", "ALTER TABLE IF EXISTS meter_devices DROP COLUMN IF EXISTS \"ReplacementReadingId\""),
        ("ALTER TABLE IF EXISTS meter_devices ADD COLUMN IF NOT EXISTS \"ReplacementReason\" character varying(500) NULL", "ALTER TABLE IF EXISTS meter_devices DROP COLUMN IF EXISTS \"ReplacementReason\""),
        ("ALTER TABLE IF EXISTS garages ADD COLUMN IF NOT EXISTS \"RegisteredOn\" date NULL", "ALTER TABLE IF EXISTS garages DROP COLUMN IF EXISTS \"RegisteredOn\""),
        ("ALTER TABLE IF EXISTS garages ADD COLUMN IF NOT EXISTS \"InitialMeterReadingMonth\" date NULL", "ALTER TABLE IF EXISTS garages DROP COLUMN IF EXISTS \"InitialMeterReadingMonth\""),
        ("ALTER TABLE IF EXISTS tariffs ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS tariffs DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS suppliers ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS suppliers DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS garages ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS garages DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS garages ADD COLUMN IF NOT EXISTS \"StartingOverdueDebt\" numeric NULL", "ALTER TABLE IF EXISTS garages DROP COLUMN IF EXISTS \"StartingOverdueDebt\""),
        ("ALTER TABLE IF EXISTS suppliers ADD COLUMN IF NOT EXISTS \"StartingDebt\" numeric NULL", "ALTER TABLE IF EXISTS suppliers DROP COLUMN IF EXISTS \"StartingDebt\""),
        ("ALTER TABLE IF EXISTS suppliers ADD COLUMN IF NOT EXISTS \"SupplierServiceId\" uuid NULL", "ALTER TABLE IF EXISTS suppliers DROP COLUMN IF EXISTS \"SupplierServiceId\""),
        ("ALTER TABLE IF EXISTS funds ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS funds DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS charge_service_settings ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS charge_service_settings DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS application_settings ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS application_settings DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS app_users ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS app_users DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS \"FeeCampaignId\" uuid NULL", "ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS \"FeeCampaignId\""),
        ("ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS \"IrregularPaymentId\" uuid NULL", "ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS \"IrregularPaymentId\""),
        ("ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS \"CounterpartyName\" character varying(200) NULL", "ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS \"CounterpartyName\""),
        ("ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS \"NegativeFundBalanceConfirmed\" boolean NOT NULL DEFAULT FALSE", "ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS \"NegativeFundBalanceConfirmed\""),
        ("ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS \"TargetAccrualId\" uuid NULL", "ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS \"TargetAccrualId\""),
        ("ALTER TABLE IF EXISTS charge_service_settings ADD COLUMN IF NOT EXISTS \"MeterKind\" character varying(40) NULL", "ALTER TABLE IF EXISTS charge_service_settings DROP COLUMN IF EXISTS \"MeterKind\""),
        ("ALTER TABLE IF EXISTS charge_service_settings ADD COLUMN IF NOT EXISTS \"AppliesToSelectedGarages\" boolean NOT NULL DEFAULT FALSE", "ALTER TABLE IF EXISTS charge_service_settings DROP COLUMN IF EXISTS \"AppliesToSelectedGarages\""),
        ("ALTER TABLE IF EXISTS charge_service_settings ADD COLUMN IF NOT EXISTS \"GarageIds\" uuid[] NOT NULL DEFAULT ARRAY[]::uuid[]", "ALTER TABLE IF EXISTS charge_service_settings DROP COLUMN IF EXISTS \"GarageIds\"")
    ];

    public static async Task AddCurrentVersionColumnsAsync(GarageBalanceDbContext context)
    {
        var connection = context.Database.GetConnectionString()!;
        var existingColumns = (await context.Database.SqlQueryRaw<string>(
            """
            SELECT 'ALTER TABLE IF EXISTS ' || table_name || ' DROP COLUMN IF EXISTS "' || column_name || '"' AS "Value"
            FROM information_schema.columns WHERE table_schema = 'public'
            """).ToArrayAsync()).ToHashSet(StringComparer.Ordinal);
        // Restore only columns added by this compatibility layer, never columns
        // already present at the historical migration under test.
        AddedColumnRemovals.TryAdd(connection, CurrentModelColumnStatements
            .Where(statement => !existingColumns.Contains(statement.Remove))
            .Select(statement => statement.Remove).ToArray());
        foreach (var statement in CurrentModelColumnStatements)
        {
            await context.Database.ExecuteSqlRawAsync(statement.Add);
        }
    }

    public static async Task RemoveCurrentVersionColumnsAsync(GarageBalanceDbContext context)
    {
        if (!AddedColumnRemovals.TryRemove(context.Database.GetConnectionString()!, out var removals)) return;
        foreach (var statement in removals)
        {
            await context.Database.ExecuteSqlRawAsync(statement);
        }
    }

    public static Task AddFinancialOperationVersionAsync(GarageBalanceDbContext context) =>
        context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS "Version" uuid NOT NULL DEFAULT gen_random_uuid();
            ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS "TargetAccrualId" uuid NULL;
            """);

    public static Task AddGarageInitialMeterMonthAsync(GarageBalanceDbContext context) =>
        context.Database.ExecuteSqlRawAsync("ALTER TABLE IF EXISTS garages ADD COLUMN IF NOT EXISTS \"InitialMeterReadingMonth\" date NULL, ADD COLUMN IF NOT EXISTS \"RegisteredOn\" date NULL");

    public static Task RemoveGarageInitialMeterMonthAsync(GarageBalanceDbContext context) =>
        context.Database.ExecuteSqlRawAsync("ALTER TABLE IF EXISTS garages DROP COLUMN IF EXISTS \"InitialMeterReadingMonth\", DROP COLUMN IF EXISTS \"RegisteredOn\"");

    public static Task RemoveFinancialOperationVersionAsync(GarageBalanceDbContext context) =>
        context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS "TargetAccrualId";
            ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS "Version";
            """);

    public static Task AddFinancialOperationTargetAccrualAsync(GarageBalanceDbContext context) =>
        context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS \"TargetAccrualId\" uuid NULL");

    public static Task RemoveFinancialOperationTargetAccrualAsync(GarageBalanceDbContext context) =>
        context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS \"TargetAccrualId\"");
}
