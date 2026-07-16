using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFinancialOperationDisplayQuery(GarageBalanceDbContext dbContext) : IFinancialOperationDisplayQuery
{
    private const string GarageKind = "garage";
    private const string SupplierKind = "supplier";

    public async Task<FinancialOperationDisplayData> GetAsync(
        IReadOnlyCollection<Guid> operationIds,
        CancellationToken cancellationToken)
    {
        if (operationIds.Count == 0)
        {
            return new FinancialOperationDisplayData([], []);
        }

        var calculations = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                operationIds.Contains(operation.Id) &&
                ((operation.OperationKind == FinancialOperationKinds.Income && operation.GarageId != null) ||
                    (operation.OperationKind == FinancialOperationKinds.Expense && operation.SupplierId != null)))
            .Select(operation => new FinancialOperationCalculationData(
                operation.Id,
                operation.OperationKind == FinancialOperationKinds.Income ? GarageKind : SupplierKind,
                operation.OperationKind == FinancialOperationKinds.Income
                    ? operation.GarageId!.Value
                    : operation.SupplierId!.Value,
                operation.AccountingMonth,
                operation.OperationKind == FinancialOperationKinds.Income
                    ? dbContext.FinancialOperations
                        .Where(previous =>
                            !previous.IsCanceled &&
                            previous.Id != operation.Id &&
                            previous.OperationKind == FinancialOperationKinds.Income &&
                            previous.GarageId == operation.GarageId &&
                            previous.OperationDate < operation.OperationDate)
                        .Sum(previous => previous.Amount)
                    : dbContext.FinancialOperations
                        .Where(previous =>
                            !previous.IsCanceled &&
                            previous.Id != operation.Id &&
                            previous.OperationKind == FinancialOperationKinds.Expense &&
                            previous.SupplierId == operation.SupplierId &&
                            previous.OperationDate < operation.OperationDate)
                        .Sum(previous => previous.Amount)))
            .ToListAsync(cancellationToken);

        if (calculations.Count == 0)
        {
            return new FinancialOperationDisplayData(calculations, []);
        }

        var garageIds = calculations
            .Where(item => item.CounterpartyKind == GarageKind)
            .Select(item => item.CounterpartyId)
            .Distinct()
            .ToArray();
        var supplierIds = calculations
            .Where(item => item.CounterpartyKind == SupplierKind)
            .Select(item => item.CounterpartyId)
            .Distinct()
            .ToArray();
        var latestMonth = calculations.Max(item => item.AccountingMonth);

        var garageBuckets = dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                garageIds.Contains(accrual.GarageId) &&
                accrual.AccountingMonth <= latestMonth)
            .GroupBy(accrual => new { accrual.GarageId, accrual.AccountingMonth })
            .Select(group => new
            {
                CounterpartyKind = GarageKind,
                CounterpartyId = group.Key.GarageId,
                group.Key.AccountingMonth,
                Amount = group.Sum(accrual => accrual.Amount)
            });
        var supplierBuckets = dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                supplierIds.Contains(accrual.SupplierId) &&
                accrual.AccountingMonth <= latestMonth)
            .GroupBy(accrual => new { accrual.SupplierId, accrual.AccountingMonth })
            .Select(group => new
            {
                CounterpartyKind = SupplierKind,
                CounterpartyId = group.Key.SupplierId,
                group.Key.AccountingMonth,
                Amount = group.Sum(accrual => accrual.Amount)
            });
        var bucketRows = await garageBuckets
            .Concat(supplierBuckets)
            .ToListAsync(cancellationToken);

        return new FinancialOperationDisplayData(
            calculations,
            bucketRows.Select(row => new FinancialOperationAccrualBucketData(
                    row.CounterpartyKind,
                    row.CounterpartyId,
                    row.AccountingMonth,
                    row.Amount))
                .ToList());
    }
}
