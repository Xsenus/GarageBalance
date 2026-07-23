using GarageBalance.Api.Application.Reports;

namespace GarageBalance.Api.Tests.Reports;

public sealed class ReportSortingTests
{
    public static TheoryData<ReportSortKind, string> AllowedFields => new()
    {
        { ReportSortKind.Consolidated, "accountingMonth" }, { ReportSortKind.Consolidated, "incomeTotal" }, { ReportSortKind.Consolidated, "expenseTotal" }, { ReportSortKind.Consolidated, "accrualTotal" }, { ReportSortKind.Consolidated, "balance" }, { ReportSortKind.Consolidated, "debt" }, { ReportSortKind.Consolidated, "operationCount" }, { ReportSortKind.Consolidated, "accrualCount" }, { ReportSortKind.Consolidated, "meterReadingCount" },
        { ReportSortKind.Garages, "accountingMonth" }, { ReportSortKind.Garages, "garageNumber" }, { ReportSortKind.Garages, "ownerName" }, { ReportSortKind.Garages, "incomeTypeName" }, { ReportSortKind.Garages, "accrualAmount" }, { ReportSortKind.Garages, "incomeAmount" }, { ReportSortKind.Garages, "difference" },
        { ReportSortKind.Expense, "date" }, { ReportSortKind.Expense, "accountingMonth" }, { ReportSortKind.Expense, "supplierName" }, { ReportSortKind.Expense, "expenseTypeName" }, { ReportSortKind.Expense, "accrualAmount" }, { ReportSortKind.Expense, "expenseAmount" }, { ReportSortKind.Expense, "difference" }, { ReportSortKind.Expense, "documentNumber" },
        { ReportSortKind.Income, "date" }, { ReportSortKind.Income, "accountingMonth" }, { ReportSortKind.Income, "garageNumber" }, { ReportSortKind.Income, "ownerName" }, { ReportSortKind.Income, "incomeTypeName" }, { ReportSortKind.Income, "accrualAmount" }, { ReportSortKind.Income, "incomeAmount" }, { ReportSortKind.Income, "debt" }, { ReportSortKind.Income, "documentNumber" },
        { ReportSortKind.CashPayments, "date" }, { ReportSortKind.CashPayments, "amount" }, { ReportSortKind.CashPayments, "hasReceipt" }, { ReportSortKind.CashPayments, "purpose" }, { ReportSortKind.CashPayments, "supplierName" }, { ReportSortKind.CashPayments, "expenseTypeName" }, { ReportSortKind.CashPayments, "documentNumber" },
        { ReportSortKind.BankDeposits, "date" }, { ReportSortKind.BankDeposits, "amount" }, { ReportSortKind.BankDeposits, "comment" },
        { ReportSortKind.Fees, "garageNumber" }, { ReportSortKind.Fees, "ownerName" }, { ReportSortKind.Fees, "feeName" }, { ReportSortKind.Fees, "accrued" }, { ReportSortKind.Fees, "paid" }, { ReportSortKind.Fees, "lastPaymentDate" }, { ReportSortKind.Fees, "debt" },
        { ReportSortKind.FundChanges, "date" }, { ReportSortKind.FundChanges, "fundName" }, { ReportSortKind.FundChanges, "changeName" }, { ReportSortKind.FundChanges, "amount" }, { ReportSortKind.FundChanges, "balanceBefore" }, { ReportSortKind.FundChanges, "balanceAfter" }, { ReportSortKind.FundChanges, "actorDisplayName" }, { ReportSortKind.FundChanges, "reason" }
    };

    [Theory]
    [MemberData(nameof(AllowedFields))]
    public void TryNormalize_AcceptsEveryContractFieldInBothDirections(ReportSortKind kind, string field)
    {
        Assert.True(ReportSorting.TryNormalize(kind, field, "asc", out var ascending, out var errorCode, out _));
        Assert.Equal(new ReportSort(field, false), ascending);
        Assert.Null(errorCode);

        Assert.True(ReportSorting.TryNormalize(kind, field, "desc", out var descending, out errorCode, out _));
        Assert.Equal(new ReportSort(field, true), descending);
        Assert.Null(errorCode);
    }

    [Theory]
    [InlineData(ReportSortKind.Consolidated, "accountingMonth", true)]
    [InlineData(ReportSortKind.Garages, "accountingMonth", true)]
    [InlineData(ReportSortKind.Expense, "date", true)]
    [InlineData(ReportSortKind.Income, "date", true)]
    [InlineData(ReportSortKind.CashPayments, "date", true)]
    [InlineData(ReportSortKind.BankDeposits, "date", true)]
    [InlineData(ReportSortKind.Fees, "garageNumber", false)]
    [InlineData(ReportSortKind.FundChanges, "date", true)]
    public void TryNormalize_UsesDocumentedDefault(ReportSortKind kind, string field, bool descending)
    {
        Assert.True(ReportSorting.TryNormalize(kind, null, null, out var sort, out var errorCode, out _));
        Assert.Equal(new ReportSort(field, descending), sort);
        Assert.Null(errorCode);
    }

    [Fact]
    public void TryNormalize_RejectsUnknownField()
    {
        Assert.False(ReportSorting.TryNormalize(ReportSortKind.Income, "unknown", "asc", out _, out var errorCode, out var errorMessage));
        Assert.Equal("report_sort_field_invalid", errorCode);
        Assert.Contains("unknown", errorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ASC")]
    [InlineData("sideways")]
    public void TryNormalize_RejectsNonContractDirection(string direction)
    {
        Assert.False(ReportSorting.TryNormalize(ReportSortKind.Income, "date", direction, out _, out var errorCode, out _));
        Assert.Equal("report_sort_direction_invalid", errorCode);
    }
}
