using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFinancialOperationDisplayQuery(GarageBalanceDbContext dbContext) : IFinancialOperationDisplayQuery
{
    private const int CalculationRow = 1;
    private const int AccrualBucketRow = 2;
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

        var visibleOperations = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                operationIds.Contains(operation.Id) &&
                ((operation.OperationKind == FinancialOperationKinds.Income && operation.GarageId != null) ||
                    (operation.OperationKind == FinancialOperationKinds.Expense && operation.SupplierId != null)));
        var latestVisibleMonth = visibleOperations
            .Select(operation => (DateOnly?)operation.AccountingMonth)
            .Max();

        var calculationRows = visibleOperations
            .Select(operation => new
            {
                RowKind = CalculationRow,
                OperationId = (Guid?)operation.Id,
                CounterpartyKind = operation.OperationKind == FinancialOperationKinds.Income ? GarageKind : SupplierKind,
                CounterpartyId = operation.OperationKind == FinancialOperationKinds.Income
                    ? operation.GarageId!.Value
                    : operation.SupplierId!.Value,
                operation.AccountingMonth,
                Amount = operation.OperationKind == FinancialOperationKinds.Income
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
                        .Sum(previous => previous.Amount)
            });
        var garageBucketRows = dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.AccountingMonth <= latestVisibleMonth &&
                visibleOperations.Any(operation => operation.GarageId == accrual.GarageId))
            .GroupBy(accrual => new { accrual.GarageId, accrual.AccountingMonth })
            .Select(group => new
            {
                RowKind = AccrualBucketRow,
                OperationId = (Guid?)null,
                CounterpartyKind = GarageKind,
                CounterpartyId = group.Key.GarageId,
                group.Key.AccountingMonth,
                Amount = group.Sum(accrual => accrual.Amount)
            });
        var supplierBucketRows = dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.AccountingMonth <= latestVisibleMonth &&
                visibleOperations.Any(operation => operation.SupplierId == accrual.SupplierId))
            .GroupBy(accrual => new { accrual.SupplierId, accrual.AccountingMonth })
            .Select(group => new
            {
                RowKind = AccrualBucketRow,
                OperationId = (Guid?)null,
                CounterpartyKind = SupplierKind,
                CounterpartyId = group.Key.SupplierId,
                group.Key.AccountingMonth,
                Amount = group.Sum(accrual => accrual.Amount)
            });

        var rows = await calculationRows
            .Concat(garageBucketRows)
            .Concat(supplierBucketRows)
            .ToListAsync(cancellationToken);

        return new FinancialOperationDisplayData(
            rows.Where(row => row.RowKind == CalculationRow)
                .Select(row => new FinancialOperationCalculationData(
                    row.OperationId!.Value,
                    row.CounterpartyKind,
                    row.CounterpartyId,
                    row.AccountingMonth,
                    row.Amount))
                .ToList(),
            rows.Where(row => row.RowKind == AccrualBucketRow)
                .Select(row => new FinancialOperationAccrualBucketData(
                    row.CounterpartyKind,
                    row.CounterpartyId,
                    row.AccountingMonth,
                    row.Amount))
                .ToList());
    }
}
