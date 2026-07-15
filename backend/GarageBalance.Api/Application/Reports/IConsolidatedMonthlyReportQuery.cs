namespace GarageBalance.Api.Application.Reports;

public interface IConsolidatedMonthlyReportQuery
{
    Task<ConsolidatedMonthlyReportData> GetMonthlyDataAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        CancellationToken cancellationToken);
}

public sealed record ConsolidatedMonthlyReportData(
    IReadOnlyList<AmountCountByMonth> IncomeByMonth,
    IReadOnlyList<AmountCountByMonth> ExpenseByMonth,
    IReadOnlyList<AmountCountByMonth> AccrualByMonth,
    IReadOnlyList<CountByMonth> MeterReadingsByMonth,
    decimal GarageStartingBalanceTotal,
    IReadOnlyList<NamedAmountTotal> IncomeBreakdown,
    IReadOnlyList<NamedAmountTotal> ExpenseBreakdown);

public readonly record struct AmountCountByMonth(DateOnly Month, decimal Amount, int Count);

public readonly record struct CountByMonth(DateOnly Month, int Count);

public readonly record struct NamedAmountTotal(Guid? TypeId, string Name, decimal Amount);
