namespace GarageBalance.Api.Tests.Audit;

public sealed class FinancialCorrectionAuditCoverageTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly Lazy<string> ProductionText = new(() => ReadFiles(
        Path.Combine(RepositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance"),
        Path.Combine(RepositoryRoot, "backend", "GarageBalance.Api", "Application", "Funds")));
    private static readonly Lazy<string> TestText = new(() => ReadFiles(
        Path.Combine(RepositoryRoot, "backend", "GarageBalance.Api.Tests", "Finance"),
        Path.Combine(RepositoryRoot, "backend", "GarageBalance.Api.Tests", "Funds")));
    private static readonly Lazy<string> CoverageDocument = new(() =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "audit-event-coverage.md")));

    public static TheoryData<string> FinancialCorrectionActions => new()
    {
        "finance.income_updated",
        "finance.expense_updated",
        "finance.operation_canceled",
        "finance.operation_restored",
        "finance.accrual_updated",
        "finance.accrual_canceled",
        "finance.accrual_restored",
        "finance.debt_transfer_updated",
        "finance.supplier_accrual_updated",
        "finance.supplier_accrual_canceled",
        "finance.supplier_accrual_restored",
        "finance.meter_reading_updated",
        "finance.meter_reading_historical_updated",
        "finance.accrual_updated_from_meter_reading",
        "finance.meter_reading_canceled",
        "finance.meter_reading_restored",
        "fund.operation_updated",
        "fund.operation_canceled",
        "fund.operation_restored",
        "fund.income_assignment_updated",
        "fund.income_assignment_canceled",
        "fund.income_assignment_restored"
    };

    [Theory]
    [MemberData(nameof(FinancialCorrectionActions))]
    public void FinancialCorrectionAction_IsWrittenDocumentedAndCovered(string action)
    {
        Assert.Contains($"\"{action}\"", ProductionText.Value, StringComparison.Ordinal);
        Assert.Contains($"\"{action}\"", TestText.Value, StringComparison.Ordinal);
        Assert.Contains($"`{action}`", CoverageDocument.Value, StringComparison.Ordinal);
    }

    private static string ReadFiles(params string[] directories)
    {
        return string.Join(
            Environment.NewLine,
            directories.SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
                .Select(File.ReadAllText));
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
