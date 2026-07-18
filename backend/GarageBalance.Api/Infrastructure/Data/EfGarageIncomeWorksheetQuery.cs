using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageIncomeWorksheetQuery(GarageBalanceDbContext dbContext) : IGarageIncomeWorksheetQuery
{
    private const int GarageCategory = 0;
    private const int PreviousAccrualCategory = 1;
    private const int PreviousIncomeCategory = 2;
    private const int AccrualBucketCategory = 3;
    private const int IncomeBucketCategory = 4;
    private const int MeterReadingCategory = 5;
    private const int MeterIncomeTypeCategory = 6;

    public async Task<GarageIncomeWorksheetData?> GetAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken)
    {
        var garageQuery = dbContext.Garages.AsNoTracking()
            .Where(garage => garage.Id == garageId && !garage.IsArchived)
            .Select(garage => new
            {
                Category = GarageCategory,
                GarageId = (Guid?)garage.Id,
                GarageNumber = (string?)garage.Number,
                OwnerLastName = garage.Owner == null ? null : garage.Owner.LastName,
                OwnerFirstName = garage.Owner == null ? null : garage.Owner.FirstName,
                OwnerMiddleName = garage.Owner == null ? null : garage.Owner.MiddleName,
                AccountingMonth = (DateOnly?)null,
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = (string?)null,
                IncomeTypeCode = (string?)null,
                Amount = garage.StartingBalance,
                MeterReadingId = (Guid?)null,
                MeterReadingVersion = (Guid?)null,
                MeterKind = (string?)null,
                ReadingDate = (DateOnly?)null,
                CurrentValue = (decimal?)null,
                Consumption = (decimal?)null,
                UpdatedAtUtc = (DateTimeOffset?)null
            });
        var previousAccrualQuery = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth < monthFrom)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = PreviousAccrualCategory,
                GarageId = (Guid?)null,
                GarageNumber = (string?)null,
                OwnerLastName = (string?)null,
                OwnerFirstName = (string?)null,
                OwnerMiddleName = (string?)null,
                AccountingMonth = (DateOnly?)null,
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = (string?)null,
                IncomeTypeCode = (string?)null,
                Amount = group.Sum(accrual => accrual.Amount),
                MeterReadingId = (Guid?)null,
                MeterReadingVersion = (Guid?)null,
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
                GarageId = (Guid?)null,
                GarageNumber = (string?)null,
                OwnerLastName = (string?)null,
                OwnerFirstName = (string?)null,
                OwnerMiddleName = (string?)null,
                AccountingMonth = (DateOnly?)null,
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = (string?)null,
                IncomeTypeCode = (string?)null,
                Amount = group.Sum(operation => operation.Amount),
                MeterReadingId = (Guid?)null,
                MeterReadingVersion = (Guid?)null,
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
                GarageId = (Guid?)null,
                GarageNumber = (string?)null,
                OwnerLastName = (string?)null,
                OwnerFirstName = (string?)null,
                OwnerMiddleName = (string?)null,
                AccountingMonth = (DateOnly?)group.Key.AccountingMonth,
                IncomeTypeId = (Guid?)group.Key.IncomeTypeId,
                IncomeTypeName = (string?)group.Key.Name,
                IncomeTypeCode = group.Key.Code,
                Amount = group.Sum(accrual => accrual.Amount),
                MeterReadingId = (Guid?)null,
                MeterReadingVersion = (Guid?)null,
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
                GarageId = (Guid?)null,
                GarageNumber = (string?)null,
                OwnerLastName = (string?)null,
                OwnerFirstName = (string?)null,
                OwnerMiddleName = (string?)null,
                AccountingMonth = (DateOnly?)group.Key.AccountingMonth,
                IncomeTypeId = group.Key.IncomeTypeId,
                IncomeTypeName = (string?)group.Key.Name,
                IncomeTypeCode = group.Key.Code,
                Amount = group.Sum(operation => operation.Amount),
                MeterReadingId = (Guid?)null,
                MeterReadingVersion = (Guid?)null,
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
                GarageId = (Guid?)null,
                GarageNumber = (string?)null,
                OwnerLastName = (string?)null,
                OwnerFirstName = (string?)null,
                OwnerMiddleName = (string?)null,
                AccountingMonth = (DateOnly?)reading.AccountingMonth,
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = (string?)null,
                IncomeTypeCode = (string?)null,
                Amount = 0m,
                MeterReadingId = (Guid?)reading.Id,
                MeterReadingVersion = (Guid?)reading.Version,
                MeterKind = (string?)reading.MeterKind,
                ReadingDate = (DateOnly?)reading.ReadingDate,
                CurrentValue = (decimal?)reading.CurrentValue,
                Consumption = (decimal?)reading.Consumption,
                UpdatedAtUtc = (DateTimeOffset?)reading.UpdatedAtUtc
            });
        var meterIncomeTypeQuery = dbContext.IncomeTypes.AsNoTracking()
            .Where(incomeType =>
                !incomeType.IsArchived &&
                (incomeType.Code == MeterKinds.Water || incomeType.Code == MeterKinds.Electricity))
            .Select(incomeType => new
            {
                Category = MeterIncomeTypeCategory,
                GarageId = (Guid?)null,
                GarageNumber = (string?)null,
                OwnerLastName = (string?)null,
                OwnerFirstName = (string?)null,
                OwnerMiddleName = (string?)null,
                AccountingMonth = (DateOnly?)null,
                IncomeTypeId = (Guid?)incomeType.Id,
                IncomeTypeName = (string?)incomeType.Name,
                IncomeTypeCode = incomeType.Code,
                Amount = 0m,
                MeterReadingId = (Guid?)null,
                MeterReadingVersion = (Guid?)null,
                MeterKind = (string?)null,
                ReadingDate = (DateOnly?)null,
                CurrentValue = (decimal?)null,
                Consumption = (decimal?)null,
                UpdatedAtUtc = (DateTimeOffset?)null
            });

        var rows = await garageQuery
            .Concat(previousAccrualQuery)
            .Concat(previousIncomeQuery)
            .Concat(accrualBucketQuery)
            .Concat(incomeBucketQuery)
            .Concat(meterReadingQuery)
            .Concat(meterIncomeTypeQuery)
            .ToListAsync(cancellationToken);
        var garage = rows.SingleOrDefault(row => row.Category == GarageCategory);
        if (garage is null)
        {
            return null;
        }

        var ownerName = string.Join(' ', new[] { garage.OwnerLastName, garage.OwnerFirstName, garage.OwnerMiddleName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        return new GarageIncomeWorksheetData(
            garage.GarageId!.Value,
            garage.GarageNumber!,
            string.IsNullOrWhiteSpace(ownerName) ? null : ownerName,
            garage.Amount,
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
            rows.Where(row => row.Category == MeterIncomeTypeCategory)
                .Select(row => new GarageIncomeWorksheetMeterTypeData(
                    row.IncomeTypeId!.Value,
                    row.IncomeTypeName!,
                    row.IncomeTypeCode!))
                .ToList(),
            rows.Where(row => row.Category == MeterReadingCategory)
                .Select(row => new GarageIncomeWorksheetMeterData(
                    row.MeterReadingId!.Value,
                    row.MeterReadingVersion!.Value,
                    row.AccountingMonth!.Value,
                    row.MeterKind!,
                    row.ReadingDate!.Value,
                    row.CurrentValue!.Value,
                    row.Consumption!.Value,
                    row.UpdatedAtUtc!.Value))
                .ToList());
    }
}
