using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageIncomeWorksheetQuery(GarageBalanceDbContext dbContext) : IGarageIncomeWorksheetQuery
{
    private const int PreviousAccrualCategory = 1;
    private const int PreviousIncomeCategory = 2;
    private const int AccrualBucketCategory = 3;
    private const int IncomeBucketCategory = 4;
    private const int MeterReadingCategory = 5;

    public async Task<GarageIncomeWorksheetData> GetAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken)
    {
        var previousAccrualQuery = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth < monthFrom)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = PreviousAccrualCategory,
                AccountingMonth = (DateOnly?)null,
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = (string?)null,
                IncomeTypeCode = (string?)null,
                Amount = group.Sum(accrual => accrual.Amount),
                MeterKind = (string?)null,
                ReadingDate = (DateOnly?)null,
                CurrentValue = (decimal?)null,
                Consumption = (decimal?)null,
                UpdatedAtUtc = (DateTimeOffset?)null
            });
        var previousIncomeQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.AccountingMonth < monthFrom)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = PreviousIncomeCategory,
                AccountingMonth = (DateOnly?)null,
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = (string?)null,
                IncomeTypeCode = (string?)null,
                Amount = group.Sum(operation => operation.Amount),
                MeterKind = (string?)null,
                ReadingDate = (DateOnly?)null,
                CurrentValue = (decimal?)null,
                Consumption = (decimal?)null,
                UpdatedAtUtc = (DateTimeOffset?)null
            });
        var accrualBucketQuery = dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.GarageId == garageId &&
                accrual.AccountingMonth >= monthFrom &&
                accrual.AccountingMonth <= monthTo)
            .GroupBy(accrual => new
            {
                accrual.AccountingMonth,
                accrual.IncomeTypeId,
                accrual.IncomeType.Name,
                accrual.IncomeType.Code
            })
            .Select(group => new
            {
                Category = AccrualBucketCategory,
                AccountingMonth = (DateOnly?)group.Key.AccountingMonth,
                IncomeTypeId = (Guid?)group.Key.IncomeTypeId,
                IncomeTypeName = (string?)group.Key.Name,
                IncomeTypeCode = group.Key.Code,
                Amount = group.Sum(accrual => accrual.Amount),
                MeterKind = (string?)null,
                ReadingDate = (DateOnly?)null,
                CurrentValue = (decimal?)null,
                Consumption = (decimal?)null,
                UpdatedAtUtc = (DateTimeOffset?)null
            });
        var incomeBucketQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.IncomeTypeId != null &&
                operation.IncomeType != null &&
                operation.AccountingMonth >= monthFrom &&
                operation.AccountingMonth <= monthTo)
            .GroupBy(operation => new
            {
                operation.AccountingMonth,
                operation.IncomeTypeId,
                operation.IncomeType!.Name,
                operation.IncomeType.Code
            })
            .Select(group => new
            {
                Category = IncomeBucketCategory,
                AccountingMonth = (DateOnly?)group.Key.AccountingMonth,
                IncomeTypeId = group.Key.IncomeTypeId,
                IncomeTypeName = (string?)group.Key.Name,
                IncomeTypeCode = group.Key.Code,
                Amount = group.Sum(operation => operation.Amount),
                MeterKind = (string?)null,
                ReadingDate = (DateOnly?)null,
                CurrentValue = (decimal?)null,
                Consumption = (decimal?)null,
                UpdatedAtUtc = (DateTimeOffset?)null
            });
        var meterReadingQuery = dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                reading.GarageId == garageId &&
                reading.AccountingMonth >= monthFrom &&
                reading.AccountingMonth <= monthTo)
            .Select(reading => new
            {
                Category = MeterReadingCategory,
                AccountingMonth = (DateOnly?)reading.AccountingMonth,
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = (string?)null,
                IncomeTypeCode = (string?)null,
                Amount = 0m,
                MeterKind = (string?)reading.MeterKind,
                ReadingDate = (DateOnly?)reading.ReadingDate,
                CurrentValue = (decimal?)reading.CurrentValue,
                Consumption = (decimal?)reading.Consumption,
                UpdatedAtUtc = (DateTimeOffset?)reading.UpdatedAtUtc
            });

        var rows = await previousAccrualQuery
            .Concat(previousIncomeQuery)
            .Concat(accrualBucketQuery)
            .Concat(incomeBucketQuery)
            .Concat(meterReadingQuery)
            .ToListAsync(cancellationToken);

        return new GarageIncomeWorksheetData(
            rows.Where(row => row.Category == PreviousAccrualCategory).Sum(row => row.Amount),
            rows.Where(row => row.Category == PreviousIncomeCategory).Sum(row => row.Amount),
            rows.Where(row => row.Category == AccrualBucketCategory)
                .Select(row => new GarageIncomeWorksheetBucketData(
                    row.AccountingMonth!.Value,
                    row.IncomeTypeId!.Value,
                    row.IncomeTypeName!,
                    row.IncomeTypeCode,
                    row.Amount))
                .ToList(),
            rows.Where(row => row.Category == IncomeBucketCategory)
                .Select(row => new GarageIncomeWorksheetBucketData(
                    row.AccountingMonth!.Value,
                    row.IncomeTypeId!.Value,
                    row.IncomeTypeName!,
                    row.IncomeTypeCode,
                    row.Amount))
                .ToList(),
            rows.Where(row => row.Category == MeterReadingCategory)
                .Select(row => new GarageIncomeWorksheetMeterData(
                    row.AccountingMonth!.Value,
                    row.MeterKind!,
                    row.ReadingDate!.Value,
                    row.CurrentValue!.Value,
                    row.Consumption!.Value,
                    row.UpdatedAtUtc!.Value))
                .ToList());
    }
}
