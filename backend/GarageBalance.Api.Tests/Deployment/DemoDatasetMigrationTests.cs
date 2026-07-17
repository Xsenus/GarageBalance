namespace GarageBalance.Api.Tests.Deployment;

public sealed class DemoDatasetMigrationTests
{
    private const string MigrationFileName = "20260716171911_SeedStagingDemoDataset.cs";
    private const string BalanceMigrationFileName = "20260717021022_BalanceStagingDemoGarages.cs";
    private const string PaymentTimesMigrationFileName = "20260717024639_DiversifyStagingDemoPaymentTimes.cs";

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

    [Fact]
    public void BalanceMigration_IsRestrictedToSeededStagingDataset()
    {
        var migration = ReadMigration(BalanceMigrationFileName);

        Assert.Contains("current_database() <> 'garagebalance_staging'", migration, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging-demo-dataset-v1", migration, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging-demo-balances-v2", migration, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT DO NOTHING", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE ", migration[..migration.IndexOf("protected override void Down", StringComparison.Ordinal)], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BalanceMigration_ProvidesDebtPaidAndAdvanceGarageExamples()
    {
        var migration = ReadMigration(BalanceMigrationFileName);

        Assert.Contains("generate_series(107, 120)", migration, StringComparison.Ordinal);
        Assert.Contains("garage_no BETWEEN 107 AND 113 THEN 0.00", migration, StringComparison.Ordinal);
        Assert.Contains("garage_no - 114", migration, StringComparison.Ordinal);
        Assert.Contains("'debtGarageNumbers', '101-106'", migration, StringComparison.Ordinal);
        Assert.Contains("'paidGarageNumbers', '107-113'", migration, StringComparison.Ordinal);
        Assert.Contains("'advanceGarageNumbers', '114-120'", migration, StringComparison.Ordinal);
        Assert.Contains("Демонстрационная полная оплата задолженности", migration, StringComparison.Ordinal);
        Assert.Contains("Демонстрационная оплата с авансом", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentTimesMigration_OnlyChangesGeneratedStagingPayments()
    {
        var migration = ReadMigration(PaymentTimesMigrationFileName);
        var upSection = migration[..migration.IndexOf("protected override void Down", StringComparison.Ordinal)];

        Assert.Contains("current_database() <> 'garagebalance_staging'", upSection, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging-demo-dataset-v1", upSection, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging-demo-payment-times-v3", upSection, StringComparison.Ordinal);
        Assert.Contains("garagebalance-demo-income-", upSection, StringComparison.Ordinal);
        Assert.Contains("garagebalance-demo-balance-adjustment-", upSection, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentNumber", upSection, StringComparison.Ordinal);
        Assert.DoesNotContain("SET \"Amount\"", upSection, StringComparison.Ordinal);
        Assert.DoesNotContain("SET \"OperationDate\"", upSection, StringComparison.Ordinal);
        Assert.DoesNotContain("SET \"AccountingMonth\"", upSection, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentTimesMigration_UsesDeterministicBusinessHours()
    {
        var migration = ReadMigration(PaymentTimesMigrationFileName);

        Assert.Contains("make_timestamptz", migration, StringComparison.Ordinal);
        Assert.Contains("8 + mod", migration, StringComparison.Ordinal);
        Assert.Contains("12) * 5", migration, StringComparison.Ordinal);
        Assert.Contains("'Asia/Novosibirsk'", migration, StringComparison.Ordinal);
        Assert.Contains("'localTimeFrom', '08:00'", migration, StringComparison.Ordinal);
        Assert.Contains("'localTimeTo', '19:55'", migration, StringComparison.Ordinal);
    }

    private static string ReadMigration(string migrationFileName = MigrationFileName)
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
            migrationFileName));
    }
}
