namespace GarageBalance.Api.Tests.Deployment;

public sealed class DemoDatasetMigrationTests
{
    private const string MigrationFileName = "20260716171911_SeedStagingDemoDataset.cs";

    [Fact]
    public void DemoDatasetMigration_IsRestrictedToStagingAndIdempotent()
    {
        var migration = ReadMigration();

        Assert.Contains("current_database() <> 'garagebalance_staging'", migration, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging-demo-dataset-v1", migration, StringComparison.Ordinal);
        Assert.Contains("IF EXISTS (SELECT 1 FROM audit_events", migration, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", migration, StringComparison.Ordinal);
        Assert.Contains("demo.dataset.seeded", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void DemoDatasetMigration_CoversRequestedConnectedBusinessData()
    {
        var migration = ReadMigration();

        string[] requiredTables =
        [
            "owners",
            "garages",
            "supplier_groups",
            "suppliers",
            "supplier_contacts",
            "staff_departments",
            "staff_members",
            "meter_readings",
            "accruals",
            "supplier_accruals",
            "financial_operations"
        ];

        foreach (var table in requiredTables)
        {
            Assert.Contains($"INSERT INTO {table}", migration, StringComparison.Ordinal);
        }

        Assert.Contains("generate_series(101, 120)", migration, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 20)", migration, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 7)", migration, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 5)", migration, StringComparison.Ordinal);
        Assert.Contains("DATE '2021-01-01'", migration, StringComparison.Ordinal);
        Assert.Contains("DATE '2026-07-01'", migration, StringComparison.Ordinal);
        Assert.Contains("'water'::text AS meter_kind", migration, StringComparison.Ordinal);
        Assert.Contains("'electricity'::text", migration, StringComparison.Ordinal);
        Assert.Contains("'regular'", migration, StringComparison.Ordinal);
        Assert.Contains("'income'", migration, StringComparison.Ordinal);
        Assert.Contains("'expense'", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void DemoDatasetMigration_UsesClearlyFictionalPersonalAndContactData()
    {
        var migration = ReadMigration();

        Assert.Contains("Иванов", migration, StringComparison.Ordinal);
        Assert.Contains("Петров Пётр Петрович", migration, StringComparison.Ordinal);
        Assert.Contains("Демонстрационная", migration, StringComparison.Ordinal);
        Assert.Contains("example.test", migration, StringComparison.Ordinal);
        Assert.Contains("+7 (900) 000-", migration, StringComparison.Ordinal);
        Assert.Contains("containsRealPersonalData', false", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("app_users", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DemoDatasetMigration_DoesNotMutateExistingOperationalRowsDuringApply()
    {
        var migration = ReadMigration();
        var upSection = migration[..migration.IndexOf("protected override void Down", StringComparison.Ordinal)];

        Assert.DoesNotContain("UPDATE ", upSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", upSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", upSection, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Демонстрационное", upSection, StringComparison.Ordinal);
    }

    private static string ReadMigration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(
            directory!.FullName,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "Migrations",
            MigrationFileName));
    }
}
