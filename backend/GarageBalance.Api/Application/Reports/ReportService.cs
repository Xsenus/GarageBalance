using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Reports;

public sealed class ReportService(GarageBalanceDbContext dbContext) : IReportService
{
    public async Task<ReportResult<ConsolidatedReportDto>> GetConsolidatedReportAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken)
    {
        var periodFrom = NormalizeMonth(request.MonthFrom ?? DateOnly.FromDateTime(DateTime.Today));
        var periodTo = NormalizeMonth(request.MonthTo ?? periodFrom);
        if (periodTo < periodFrom)
        {
            return ReportResult<ConsolidatedReportDto>.Failure("period_invalid", "Дата окончания отчета не может быть раньше даты начала.");
        }

        var operations = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.AccountingMonth >= periodFrom && operation.AccountingMonth <= periodTo);
        var accruals = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.AccountingMonth >= periodFrom && accrual.AccountingMonth <= periodTo);
        var meterReadings = dbContext.MeterReadings.AsNoTracking()
            .Where(reading => !reading.IsCanceled && reading.AccountingMonth >= periodFrom && reading.AccountingMonth <= periodTo);

        var incomeByMonth = await operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Income)
            .GroupBy(operation => operation.AccountingMonth)
            .Select(group => new AmountCountByMonth(group.Key, group.Sum(item => item.Amount), group.Count()))
            .ToListAsync(cancellationToken);
        var expenseByMonth = await operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Expense)
            .GroupBy(operation => operation.AccountingMonth)
            .Select(group => new AmountCountByMonth(group.Key, group.Sum(item => item.Amount), group.Count()))
            .ToListAsync(cancellationToken);
        var accrualByMonth = await accruals
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new AmountCountByMonth(group.Key, group.Sum(item => item.Amount), group.Count()))
            .ToListAsync(cancellationToken);
        var readingsByMonth = await meterReadings
            .GroupBy(reading => reading.AccountingMonth)
            .Select(group => new CountByMonth(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        var months = EnumerateMonths(periodFrom, periodTo).ToList();
        var monthlyRows = months
            .Select(month =>
            {
                var income = incomeByMonth.SingleOrDefault(row => row.Month == month);
                var expense = expenseByMonth.SingleOrDefault(row => row.Month == month);
                var accrual = accrualByMonth.SingleOrDefault(row => row.Month == month);
                var readings = readingsByMonth.SingleOrDefault(row => row.Month == month);
                return new MonthlyReportRowDto(
                    month,
                    income.Amount,
                    expense.Amount,
                    accrual.Amount,
                    income.Amount - expense.Amount,
                    accrual.Amount - income.Amount,
                    income.Count + expense.Count,
                    accrual.Count,
                    readings.Count);
            })
            .ToList();

        var garageRows = await BuildGarageRowsAsync(request.Search, periodFrom, periodTo, cancellationToken);
        var report = new ConsolidatedReportDto(
            periodFrom,
            periodTo,
            monthlyRows.Sum(row => row.IncomeTotal),
            monthlyRows.Sum(row => row.ExpenseTotal),
            monthlyRows.Sum(row => row.AccrualTotal),
            monthlyRows.Sum(row => row.Balance),
            monthlyRows.Sum(row => row.Debt),
            monthlyRows.Sum(row => row.OperationCount),
            monthlyRows.Sum(row => row.AccrualCount),
            monthlyRows.Sum(row => row.MeterReadingCount),
            monthlyRows,
            garageRows);

        return ReportResult<ConsolidatedReportDto>.Success(report);
    }

    private async Task<IReadOnlyList<GarageReportRowDto>> BuildGarageRowsAsync(string? search, DateOnly periodFrom, DateOnly periodTo, CancellationToken cancellationToken)
    {
        var garagesQuery = dbContext.Garages.AsNoTracking()
            .Include(garage => garage.Owner)
            .Where(garage => !garage.IsArchived);

        var garages = await garagesQuery
            .OrderBy(garage => garage.Number)
            .ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim();
            garages = garages
                .Where(garage =>
                    garage.Number.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    (garage.Owner?.FullName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        var garageIds = garages.Select(garage => garage.Id).ToList();

        var incomeByGarage = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId != null &&
                garageIds.Contains(operation.GarageId.Value) &&
                operation.AccountingMonth >= periodFrom &&
                operation.AccountingMonth <= periodTo)
            .GroupBy(operation => operation.GarageId!.Value)
            .Select(group => new AmountByGarage(group.Key, group.Sum(item => item.Amount)))
            .ToListAsync(cancellationToken);
        var accrualByGarage = await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                garageIds.Contains(accrual.GarageId) &&
                accrual.AccountingMonth >= periodFrom &&
                accrual.AccountingMonth <= periodTo)
            .GroupBy(accrual => accrual.GarageId)
            .Select(group => new AmountByGarage(group.Key, group.Sum(item => item.Amount)))
            .ToListAsync(cancellationToken);
        var readingsByGarage = await dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                garageIds.Contains(reading.GarageId) &&
                reading.AccountingMonth >= periodFrom &&
                reading.AccountingMonth <= periodTo)
            .GroupBy(reading => reading.GarageId)
            .Select(group => new CountByGarage(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        return garages
            .Select(garage =>
            {
                var income = incomeByGarage.SingleOrDefault(row => row.GarageId == garage.Id).Amount;
                var accrual = accrualByGarage.SingleOrDefault(row => row.GarageId == garage.Id).Amount;
                var readings = readingsByGarage.SingleOrDefault(row => row.GarageId == garage.Id).Count;
                return new GarageReportRowDto(garage.Id, garage.Number, garage.Owner?.FullName, income, accrual, accrual - income, readings);
            })
            .Where(row => row.IncomeTotal != 0 || row.AccrualTotal != 0 || row.MeterReadingCount != 0)
            .ToList();
    }

    private static IEnumerable<DateOnly> EnumerateMonths(DateOnly periodFrom, DateOnly periodTo)
    {
        for (var month = periodFrom; month <= periodTo; month = month.AddMonths(1))
        {
            yield return month;
        }
    }

    private static DateOnly NormalizeMonth(DateOnly value)
    {
        return new DateOnly(value.Year, value.Month, 1);
    }

    private readonly record struct AmountCountByMonth(DateOnly Month, decimal Amount, int Count);
    private readonly record struct CountByMonth(DateOnly Month, int Count);
    private readonly record struct AmountByGarage(Guid GarageId, decimal Amount);
    private readonly record struct CountByGarage(Guid GarageId, int Count);
}
