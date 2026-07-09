namespace GarageBalance.Api.Tests.Deployment;

public sealed class DataModelErdTests
{
    [Fact]
    public void DataModelErdCoversCurrentCoreTablesRelationsIndexesAndRules()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "data-model-erd.md"));

        Assert.Contains("GarageBalanceDbContext", document, StringComparison.Ordinal);
        Assert.Contains("owners ||--o{ garages", document, StringComparison.Ordinal);
        Assert.Contains("supplier_groups ||--o{ suppliers", document, StringComparison.Ordinal);
        Assert.Contains("suppliers ||--o{ supplier_contacts", document, StringComparison.Ordinal);
        Assert.Contains("staff_departments ||--o{ staff_members", document, StringComparison.Ordinal);
        Assert.Contains("garages ||--o{ accruals", document, StringComparison.Ordinal);
        Assert.Contains("garages ||--o{ meter_readings", document, StringComparison.Ordinal);
        Assert.Contains("tariffs ||--o{ accruals", document, StringComparison.Ordinal);
        Assert.Contains("income_types ||--o{ charge_service_settings", document, StringComparison.Ordinal);
        Assert.Contains("tariffs ||--o{ charge_service_settings", document, StringComparison.Ordinal);
        Assert.Contains("financial_operations", document, StringComparison.Ordinal);
        Assert.Contains("supplier_accruals", document, StringComparison.Ordinal);
        Assert.Contains("staff_members ||--o{ financial_operations", document, StringComparison.Ordinal);
        Assert.Contains("funds ||--o{ fund_operations", document, StringComparison.Ordinal);
        Assert.Contains("app_users ||--o{ app_user_roles", document, StringComparison.Ordinal);
        Assert.Contains("app_roles ||--o{ app_user_roles", document, StringComparison.Ordinal);
        Assert.Contains("audit_events", document, StringComparison.Ordinal);
        Assert.Contains("access_import_runs", document, StringComparison.Ordinal);
        Assert.Contains("access_import_row_fingerprints", document, StringComparison.Ordinal);
        Assert.Contains("access_import_quarantine_items", document, StringComparison.Ordinal);
        Assert.Contains("access_import_run_log_entries", document, StringComparison.Ordinal);
        Assert.Contains("integration_secret_settings", document, StringComparison.Ordinal);
        Assert.Contains("supplier_contacts", document, StringComparison.Ordinal);
        Assert.Contains("staff_departments", document, StringComparison.Ordinal);
        Assert.Contains("staff_members", document, StringComparison.Ordinal);
        Assert.Contains("income_types", document, StringComparison.Ordinal);
        Assert.Contains("expense_types", document, StringComparison.Ordinal);
        Assert.Contains("tariffs", document, StringComparison.Ordinal);
        Assert.Contains("charge_service_settings", document, StringComparison.Ordinal);
        Assert.Contains("irregular_payments", document, StringComparison.Ordinal);
        Assert.Contains("fee_campaigns", document, StringComparison.Ordinal);
        Assert.Contains("fee_campaign_garages", document, StringComparison.Ordinal);
        Assert.Contains("funds", document, StringComparison.Ordinal);
        Assert.Contains("fund_operations", document, StringComparison.Ordinal);
        Assert.Contains("form_states", document, StringComparison.Ordinal);
        Assert.Contains("income_types ||--o{ fee_campaigns", document, StringComparison.Ordinal);
        Assert.Contains("fee_campaigns ||--o{ fee_campaign_garages", document, StringComparison.Ordinal);
        Assert.Contains("garages ||--o{ fee_campaign_garages", document, StringComparison.Ordinal);
        Assert.Contains("IncomeTypeId", document, StringComparison.Ordinal);
        Assert.Contains("TariffId", document, StringComparison.Ordinal);
        Assert.Contains("StaffMemberId", document, StringComparison.Ordinal);
        Assert.Contains("FeeCampaignId + GarageId", document, StringComparison.Ordinal);
        Assert.Contains("ContributionAmount", document, StringComparison.Ordinal);
        Assert.Contains("TargetAmount", document, StringComparison.Ordinal);
        Assert.Contains("StartsOn", document, StringComparison.Ordinal);
        Assert.Contains("OverdueGraceDays", document, StringComparison.Ordinal);
        Assert.Contains("PeriodicityMonths", document, StringComparison.Ordinal);
        Assert.Contains("HasTieredTariff", document, StringComparison.Ordinal);
        Assert.Contains("IsActive", document, StringComparison.Ordinal);
        Assert.Contains("BalanceBefore", document, StringComparison.Ordinal);
        Assert.Contains("BalanceAfter", document, StringComparison.Ordinal);
        Assert.Contains("Scope", document, StringComparison.Ordinal);
        Assert.Contains("Name` при `IsArchived = false", document, StringComparison.Ordinal);
        Assert.Contains("Name + EffectiveFrom", document, StringComparison.Ordinal);
        Assert.Contains("GarageId + IncomeTypeId + AccountingMonth + Source", document, StringComparison.Ordinal);
        Assert.Contains("SupplierId + ExpenseTypeId + AccountingMonth + Source + DocumentNumber", document, StringComparison.Ordinal);
        Assert.Contains("GarageId + MeterKind + AccountingMonth", document, StringComparison.Ordinal);
        Assert.Contains("SourceSystem + EntityType + ExternalId", document, StringComparison.Ordinal);
        Assert.Contains("RowSnapshotJson", document, StringComparison.Ordinal);
        Assert.Contains("ReasonCode", document, StringComparison.Ordinal);
        Assert.Contains("StepCode", document, StringComparison.Ordinal);
        Assert.Contains("DetailsJson", document, StringComparison.Ordinal);
        Assert.Contains("Section + ActionKind + CreatedAtUtc", document, StringComparison.Ordinal);
        Assert.Contains("EntityDisplayName", document, StringComparison.Ordinal);
        Assert.Contains("RelatedGarageNumber", document, StringComparison.Ordinal);
        Assert.Contains("MetadataJson", document, StringComparison.Ordinal);
        Assert.Contains("NormalizedProvider + NormalizedSettingKey", document, StringComparison.Ordinal);
        Assert.Contains("DeleteBehavior.SetNull", document, StringComparison.Ordinal);
        Assert.Contains("DeleteBehavior.Restrict", document, StringComparison.Ordinal);
        Assert.Contains("EF Core migration", document, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL aggregation", document, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
