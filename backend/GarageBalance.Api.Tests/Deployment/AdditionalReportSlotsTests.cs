namespace GarageBalance.Api.Tests.Deployment;

public sealed class AdditionalReportSlotsTests
{
    [Fact]
    public void AdditionalReportSlotsReserveThreeReportsWithContractsAndAcceptance()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "additional-report-slots.md"));

        Assert.Contains("Задолженность И Переплаты", document, StringComparison.Ordinal);
        Assert.Contains("Счетчики И Расход", document, StringComparison.Ordinal);
        Assert.Contains("Поставщики И Обязательства", document, StringComparison.Ordinal);
        Assert.Contains("reports.read", document, StringComparison.Ordinal);
        Assert.Contains("docs/new-report-checklist.md", document, StringComparison.Ordinal);
        Assert.Contains("docs/monthly-cycle-checklist.md", document, StringComparison.Ordinal);
        Assert.Contains("backend/GarageBalance.Api/AppReleases/releases.json", document, StringComparison.Ordinal);
        Assert.Contains("meter_water", document, StringComparison.Ordinal);
        Assert.Contains("meter_electricity", document, StringComparison.Ordinal);
        Assert.Contains("all", document, StringComparison.Ordinal);
        Assert.Contains("accruals", document, StringComparison.Ordinal);
        Assert.Contains("payments", document, StringComparison.Ordinal);
        Assert.Contains("ReportsController", document, StringComparison.Ordinal);
        Assert.Contains("ReportService", document, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL aggregation", document, StringComparison.Ordinal);
        Assert.Contains("server pagination", document, StringComparison.Ordinal);
        Assert.Contains("XLSX/PDF", document, StringComparison.Ordinal);
        Assert.Contains("UTF-8/no BOM", document, StringComparison.Ordinal);
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
