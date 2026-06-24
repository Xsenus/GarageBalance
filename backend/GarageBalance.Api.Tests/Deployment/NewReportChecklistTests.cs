namespace GarageBalance.Api.Tests.Deployment;

public sealed class NewReportChecklistTests
{
    [Fact]
    public void NewReportChecklistCoversContractsPerformanceExportsTestsAndReleaseNotes()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "new-report-checklist.md"));

        Assert.Contains("консолидированный отчет", document, StringComparison.Ordinal);
        Assert.Contains("отчет по поступлениям", document, StringComparison.Ordinal);
        Assert.Contains("отчет по выплатам", document, StringComparison.Ordinal);
        Assert.Contains("ДД.ММ.ГГГГ", document, StringComparison.Ordinal);
        Assert.Contains("ММ.ГГГГ", document, StringComparison.Ordinal);
        Assert.Contains("ReportService", document, StringComparison.Ordinal);
        Assert.Contains("ReportsController", document, StringComparison.Ordinal);
        Assert.Contains("reports.read", document, StringComparison.Ordinal);
        Assert.Contains("ProblemDetails", document, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL", document, StringComparison.Ordinal);
        Assert.Contains("limit", document, StringComparison.Ordinal);
        Assert.Contains("rowCount", document, StringComparison.Ordinal);
        Assert.Contains("server pagination", document, StringComparison.Ordinal);
        Assert.Contains("XLSX/PDF", document, StringComparison.Ordinal);
        Assert.Contains("garagebalance-{reportType}-{yyyyMMdd}-{yyyyMMdd}.{xlsx|pdf}", document, StringComparison.Ordinal);
        Assert.Contains("sessionStorage", document, StringComparison.Ordinal);
        Assert.Contains("dotnet test", document, StringComparison.Ordinal);
        Assert.Contains("npm run test -- --runInBand", document, StringComparison.Ordinal);
        Assert.Contains("dotnet format --verify-no-changes", document, StringComparison.Ordinal);
        Assert.Contains("releases.json", document, StringComparison.Ordinal);
        Assert.Contains("docs/project-roadmap.md", document, StringComparison.Ordinal);
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
