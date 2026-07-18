using System.Text.RegularExpressions;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class ReportSortingContractTests
{
    [Fact]
    public void ActiveContractDefinesAllReportAllowlistsAndUniformValidationRules()
    {
        var repositoryRoot = FindRepositoryRoot();
        var contract = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "report-sorting-contract.md"));

        Assert.Contains("Статус: утвержден для реализации Этапа 6", contract, StringComparison.Ordinal);
        Assert.Contains("`sortBy`", contract, StringComparison.Ordinal);
        Assert.Contains("`sortDirection`", contract, StringComparison.Ordinal);
        Assert.Contains("`asc` или `desc`", contract, StringComparison.Ordinal);
        Assert.Contains("report_sort_field_invalid", contract, StringComparison.Ordinal);
        Assert.Contains("report_sort_direction_invalid", contract, StringComparison.Ordinal);
        Assert.Contains("до `offset/limit`", contract, StringComparison.Ordinal);
        Assert.Contains("Экран, XLSX и PDF", contract, StringComparison.Ordinal);
        Assert.Contains("стабильный tie-breaker", contract, StringComparison.Ordinal);

        AssertReportAllowlist(contract, "/api/reports/consolidated", "accountingMonth", "incomeTotal", "expenseTotal", "accrualTotal", "balance", "debt", "operationCount", "accrualCount", "meterReadingCount");
        AssertReportAllowlist(contract, "/api/reports/garages", "accountingMonth", "garageNumber", "ownerName", "incomeTypeName", "accrualAmount", "incomeAmount", "difference");
        AssertReportAllowlist(contract, "/api/reports/expense", "date", "accountingMonth", "supplierName", "expenseTypeName", "accrualAmount", "expenseAmount", "difference", "documentNumber");
        AssertReportAllowlist(contract, "/api/reports/income", "date", "accountingMonth", "garageNumber", "ownerName", "incomeTypeName", "accrualAmount", "incomeAmount", "debt", "documentNumber");
        AssertReportAllowlist(contract, "/api/reports/cash-payments", "date", "amount", "hasReceipt", "purpose", "supplierName", "expenseTypeName", "documentNumber");
        AssertReportAllowlist(contract, "/api/reports/bank-deposits", "date", "amount", "fundName", "comment");
        AssertReportAllowlist(contract, "/api/reports/fees", "garageNumber", "ownerName", "feeName", "accrued", "paid", "lastPaymentDate", "debt");
        AssertReportAllowlist(contract, "/api/reports/fund-changes", "date", "fundName", "changeName", "amount", "balanceBefore", "balanceAfter", "actorDisplayName", "reason");

        Assert.DoesNotContain("[заполнить]", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("[decision]", contract, StringComparison.Ordinal);
    }

    private static void AssertReportAllowlist(string contract, string route, params string[] fields)
    {
        var routeStart = contract.IndexOf(route, StringComparison.Ordinal);
        Assert.True(routeStart >= 0, $"Маршрут {route} отсутствует в контракте сортировки.");
        var nextSection = contract.IndexOf("\n### ", routeStart, StringComparison.Ordinal);
        var section = nextSection < 0 ? contract[routeStart..] : contract[routeStart..nextSection];
        var allowlistLine = section
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("Allowlist:", StringComparison.Ordinal));
        var actualFields = Regex.Matches(allowlistLine, "`([^`]+)`")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(fields, actualFields);
        Assert.Contains("По умолчанию:", section, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Корень репозитория не найден.");
    }
}
