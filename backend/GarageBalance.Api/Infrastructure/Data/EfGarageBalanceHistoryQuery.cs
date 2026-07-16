using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageBalanceHistoryQuery(GarageBalanceDbContext dbContext) : IGarageBalanceHistoryQuery
{
    private const int PreviousAccrualCategory = 1;
    private const int PreviousIncomeCategory = 2;
    private const int AccrualBucketCategory = 3;
    private const int IncomeBucketCategory = 4;

    public async Task<GarageBalanceHistoryData> GetAsync(
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
                Amount = group.Sum(accrual => accrual.Amount)
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
                Amount = group.Sum(operation => operation.Amount)
            });
        var accrualBucketQuery = dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.GarageId == garageId &&
                accrual.AccountingMonth >= monthFrom &&
                accrual.AccountingMonth <= monthTo)
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new
            {
                Category = AccrualBucketCategory,
                AccountingMonth = (DateOnly?)group.Key,
                Amount = group.Sum(accrual => accrual.Amount)
            });
        var incomeBucketQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.AccountingMonth >= monthFrom &&
                operation.AccountingMonth <= monthTo)
            .GroupBy(operation => operation.AccountingMonth)
            .Select(group => new
            {
                Category = IncomeBucketCategory,
                AccountingMonth = (DateOnly?)group.Key,
                Amount = group.Sum(operation => operation.Amount)
            });

        var rows = await previousAccrualQuery
            .Concat(previousIncomeQuery)
            .Concat(accrualBucketQuery)
            .Concat(incomeBucketQuery)
            .ToListAsync(cancellationToken);

        return new GarageBalanceHistoryData(
            rows.Where(row => row.Category == PreviousAccrualCategory).Sum(row => row.Amount),
            rows.Where(row => row.Category == PreviousIncomeCategory).Sum(row => row.Amount),
            rows.Where(row => row.Category == AccrualBucketCategory)
                .Select(row => new GarageBalanceHistoryBucketData(row.AccountingMonth!.Value, row.Amount))
                .ToList(),
            rows.Where(row => row.Category == IncomeBucketCategory)
                .Select(row => new GarageBalanceHistoryBucketData(row.AccountingMonth!.Value, row.Amount))
                .ToList());
    }
}
