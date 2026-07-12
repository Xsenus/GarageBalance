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
    decimal GarageStartingBalanceTotal);

public readonly record struct AmountCountByMonth(DateOnly Month, decimal Amount, int Count);

public readonly record struct CountByMonth(DateOnly Month, int Count);
