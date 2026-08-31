using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Common;

internal static class PostgreSqlLegacyModelCompatibility
{
    private static readonly (string Add, string Remove)[] CurrentModelColumnStatements =
    [
        ("ALTER TABLE IF EXISTS tariffs ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS tariffs DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS suppliers ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS suppliers DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS garages ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS garages DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS garages ADD COLUMN IF NOT EXISTS \"StartingOverdueDebt\" numeric NULL", "ALTER TABLE IF EXISTS garages DROP COLUMN IF EXISTS \"StartingOverdueDebt\""),
        ("ALTER TABLE IF EXISTS suppliers ADD COLUMN IF NOT EXISTS \"StartingDebt\" numeric NULL", "ALTER TABLE IF EXISTS suppliers DROP COLUMN IF EXISTS \"StartingDebt\""),
        ("ALTER TABLE IF EXISTS funds ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS funds DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS charge_service_settings ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS charge_service_settings DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS application_settings ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS application_settings DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS app_users ADD COLUMN IF NOT EXISTS \"Version\" uuid NOT NULL DEFAULT gen_random_uuid()", "ALTER TABLE IF EXISTS app_users DROP COLUMN IF EXISTS \"Version\""),
        ("ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS \"FeeCampaignId\" uuid NULL", "ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS \"FeeCampaignId\""),
        ("ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS \"IrregularPaymentId\" uuid NULL", "ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS \"IrregularPaymentId\""),
        ("ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS \"CounterpartyName\" character varying(200) NULL", "ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS \"CounterpartyName\""),
        ("ALTER TABLE IF EXISTS financial_operations ADD COLUMN IF NOT EXISTS \"NegativeFundBalanceConfirmed\" boolean NOT NULL DEFAULT FALSE", "ALTER TABLE IF EXISTS financial_operations DROP COLUMN IF EXISTS \"NegativeFundBalanceConfirmed\""),
        ("ALTER TABLE IF EXISTS charge_service_settings ADD COLUMN IF NOT EXISTS \"MeterKind\" character varying(40) NULL", "ALTER TABLE IF EXISTS charge_service_settings DROP COLUMN IF EXISTS \"MeterKind\"")
    ];

    public static async Task AddCurrentVersionColumnsAsync(GarageBalanceDbContext context)
    {
        foreach (var statement in CurrentModelColumnStatements)
        {
            await context.Database.ExecuteSqlRawAsync(statement.Add);
        }
    }

    public static async Task RemoveCurrentVersionColumnsAsync(GarageBalanceDbContext context)
    {
        foreach (var statement in CurrentModelColumnStatements)
        {
            await context.Database.ExecuteSqlRawAsync(statement.Remove);
        }
    }
}
