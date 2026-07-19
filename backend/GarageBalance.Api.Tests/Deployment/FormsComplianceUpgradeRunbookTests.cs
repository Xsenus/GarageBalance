namespace GarageBalance.Api.Tests.Deployment;

public sealed class FormsComplianceUpgradeRunbookTests
{
    private static readonly string[] RequiredMigrations =
    [
        "20260717042446_AccrualDueDateSnapshots",
        "20260717044239_AccrualPaymentAllocations",
        "20260717050320_AccrualPaymentAllocationAmountConstraint",
        "20260717073701_HistoricalAccrualDueDateReconciliation",
        "20260717124326_DistinguishCashToBankFundTransfers",
        "20260717140011_MeterReadingOptimisticConcurrency",
        "20260718135608_AnnualAccrualAccountingYear",
        "20260718160621_AddStableIncomeDestinations",
        "20260718163348_RouteIrregularAccrualsToOtherPayments",
        "20260718172521_RouteFeeCampaignAccrualsToOtherIncome",
        "20260718183543_LinkIncomeFundAssignments"
    ];

    [Fact]
    public void RunbookCoversActualMigrationsBackupReconciliationAndSafeRollback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "forms-compliance-upgrade-runbook.md"));
        var roadmap = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "forms-compliance-fixes-roadmap.md"));
        var migrationRoot = Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "Migrations");

        foreach (var migration in RequiredMigrations)
        {
            Assert.True(File.Exists(Path.Combine(migrationRoot, $"{migration}.cs")), $"Migration {migration} was not found.");
            Assert.Contains(migration, document, StringComparison.Ordinal);
        }

        Assert.Contains("backup-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("restore-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("garagebalance_restore_check", document, StringComparison.Ordinal);
        Assert.Contains("generate-migration-script.ps1", document, StringComparison.Ordinal);
        Assert.Contains("invalid_allocations", document, StringComparison.Ordinal);
        Assert.Contains("invalid_annual_years", document, StringComparison.Ordinal);
        Assert.Contains("invalid_fund_assignments", document, StringComparison.Ordinal);
        Assert.Contains("invalid_fund_balances", document, StringComparison.Ordinal);
        Assert.Contains("DueDateNeedsReview", document, StringComparison.Ordinal);
        Assert.Contains("Не выполнять `dotnet-ef database update <СТАРАЯ_MIGRATION>`", document, StringComparison.Ordinal);
        Assert.Contains("не редактировать `__EFMigrationsHistory`", document, StringComparison.Ordinal);
        Assert.Contains("отдельному подтверждению администратора", document, StringComparison.Ordinal);
        Assert.Contains(
            "- [x] Описать migration/backfill, резервное копирование, rollback и сверку после обновления.",
            roadmap,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GarageBalance.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
