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
        Assert.Contains("garages ||--o{ accruals", document, StringComparison.Ordinal);
        Assert.Contains("garages ||--o{ meter_readings", document, StringComparison.Ordinal);
        Assert.Contains("financial_operations", document, StringComparison.Ordinal);
        Assert.Contains("supplier_accruals", document, StringComparison.Ordinal);
        Assert.Contains("app_users ||--o{ app_user_roles", document, StringComparison.Ordinal);
        Assert.Contains("app_roles ||--o{ app_user_roles", document, StringComparison.Ordinal);
        Assert.Contains("audit_events", document, StringComparison.Ordinal);
        Assert.Contains("access_import_runs", document, StringComparison.Ordinal);
        Assert.Contains("access_import_row_fingerprints", document, StringComparison.Ordinal);
        Assert.Contains("access_import_quarantine_items", document, StringComparison.Ordinal);
        Assert.Contains("integration_secret_settings", document, StringComparison.Ordinal);
        Assert.Contains("income_types", document, StringComparison.Ordinal);
        Assert.Contains("expense_types", document, StringComparison.Ordinal);
        Assert.Contains("tariffs", document, StringComparison.Ordinal);
        Assert.Contains("Name + EffectiveFrom", document, StringComparison.Ordinal);
        Assert.Contains("GarageId + IncomeTypeId + AccountingMonth + Source", document, StringComparison.Ordinal);
        Assert.Contains("SupplierId + ExpenseTypeId + AccountingMonth + Source + DocumentNumber", document, StringComparison.Ordinal);
        Assert.Contains("GarageId + MeterKind + AccountingMonth", document, StringComparison.Ordinal);
        Assert.Contains("SourceSystem + EntityType + ExternalId", document, StringComparison.Ordinal);
        Assert.Contains("RowSnapshotJson", document, StringComparison.Ordinal);
        Assert.Contains("ReasonCode", document, StringComparison.Ordinal);
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
