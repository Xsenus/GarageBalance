namespace GarageBalance.Api.Application.Reports;

public interface IConsolidatedMonthlyReportQuery
{
    Task<ConsolidatedMonthlyReportData> GetMonthlyDataAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        CancellationToken cancellationToken);

    Task<ConsolidatedMonthlyReportData> GetMonthlyDataAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        ReportSort sort,
        int offset,
        int? limit,
        CancellationToken cancellationToken);
}

public sealed record ConsolidatedMonthlyReportData(
    IReadOnlyList<AmountCountByMonth> IncomeByMonth,
    IReadOnlyList<AmountCountByMonth> ExpenseByMonth,
    IReadOnlyList<AmountCountByMonth> AccrualByMonth,
    IReadOnlyList<CountByMonth> MeterReadingsByMonth,
    decimal GarageStartingBalanceTotal,
    IReadOnlyList<NamedAmountTotal> IncomeBreakdown,
    IReadOnlyList<NamedAmountTotal> ExpenseBreakdown,
    IReadOnlyList<MonthlyNamedAmountTotal> IncomeBreakdownByMonth,
    IReadOnlyList<MonthlyNamedAmountTotal> ExpenseBreakdownByMonth,
    IReadOnlyList<MonthlyReportQueryRow> MonthlyRows,
    int MonthlyRowCount);

public sealed record MonthlyNamedAmountTotal(
    DateOnly AccountingMonth,
    Guid? TypeId,
    string Name,
    decimal Amount);

public sealed record MonthlyReportQueryRow(
    DateOnly AccountingMonth,
    decimal IncomeTotal,
    decimal ExpenseTotal,
    decimal AccrualTotal,
    decimal Balance,
    decimal Debt,
    int OperationCount,
    int AccrualCount,
    int MeterReadingCount,
    decimal BankBalanceOpening,
    decimal BankBalanceClosing);

public readonly record struct AmountCountByMonth(DateOnly Month, decimal Amount, int Count);

public readonly record struct CountByMonth(DateOnly Month, int Count);

public readonly record struct NamedAmountTotal(Guid? TypeId, string Name, decimal Amount);
