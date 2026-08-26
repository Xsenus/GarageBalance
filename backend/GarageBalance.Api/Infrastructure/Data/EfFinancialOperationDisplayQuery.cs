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

        if (dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await GetSqliteDataAsync(operationIds, cancellationToken);
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
                            (previous.OperationDate < operation.OperationDate ||
                                (previous.OperationDate == operation.OperationDate &&
                                    previous.CreatedAtUtc < operation.CreatedAtUtc)))
                        .Sum(previous => previous.Amount)
                    : dbContext.FinancialOperations
                        .Where(previous =>
                            !previous.IsCanceled &&
                            previous.Id != operation.Id &&
                            previous.OperationKind == FinancialOperationKinds.Expense &&
                            previous.SupplierId == operation.SupplierId &&
                            (previous.OperationDate < operation.OperationDate ||
                                (previous.OperationDate == operation.OperationDate &&
                                    previous.CreatedAtUtc < operation.CreatedAtUtc)))
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

    private async Task<FinancialOperationDisplayData> GetSqliteDataAsync(
        IReadOnlyCollection<Guid> operationIds,
        CancellationToken cancellationToken)
    {
        // SQLite cannot compare DateTimeOffset values in translated SQL. This bounded test-provider
        // fallback materializes the selected operations and their counterparties in one command;
        // PostgreSQL keeps the chronological calculation fully server-side.
        const int VisibleOperationRow = 1;
        const int PaymentRow = 2;
        const int BucketRow = 3;
        var visibleOperations = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                operationIds.Contains(operation.Id) &&
                ((operation.OperationKind == FinancialOperationKinds.Income && operation.GarageId != null) ||
                    (operation.OperationKind == FinancialOperationKinds.Expense && operation.SupplierId != null)));
        var visibleRows = visibleOperations
            .Select(operation => new
            {
                RowKind = VisibleOperationRow,
                OperationId = (Guid?)operation.Id,
                CounterpartyKind = operation.OperationKind == FinancialOperationKinds.Income ? GarageKind : SupplierKind,
                CounterpartyId = operation.OperationKind == FinancialOperationKinds.Income
                    ? operation.GarageId!.Value
                    : operation.SupplierId!.Value,
                AccountingMonth = (DateOnly?)operation.AccountingMonth,
                OperationDate = (DateOnly?)operation.OperationDate,
                CreatedAtUtc = (DateTimeOffset?)operation.CreatedAtUtc,
                Amount = 0m
            });
        var paymentRows = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                ((operation.OperationKind == FinancialOperationKinds.Income &&
                        operation.GarageId.HasValue && visibleOperations.Any(visible =>
                            visible.OperationKind == FinancialOperationKinds.Income &&
                            visible.GarageId == operation.GarageId)) ||
                    (operation.OperationKind == FinancialOperationKinds.Expense &&
                        operation.SupplierId.HasValue && visibleOperations.Any(visible =>
                            visible.OperationKind == FinancialOperationKinds.Expense &&
                            visible.SupplierId == operation.SupplierId))))
            .Select(operation => new
            {
                RowKind = PaymentRow,
                OperationId = (Guid?)operation.Id,
                CounterpartyKind = operation.OperationKind == FinancialOperationKinds.Income ? GarageKind : SupplierKind,
                CounterpartyId = operation.OperationKind == FinancialOperationKinds.Income
                    ? operation.GarageId!.Value
                    : operation.SupplierId!.Value,
                AccountingMonth = (DateOnly?)null,
                OperationDate = (DateOnly?)operation.OperationDate,
                CreatedAtUtc = (DateTimeOffset?)operation.CreatedAtUtc,
                operation.Amount
            });
        var garageBuckets = dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                visibleOperations.Any(operation => operation.GarageId == accrual.GarageId))
            .GroupBy(accrual => new { accrual.GarageId, accrual.AccountingMonth })
            .Select(group => new
            {
                RowKind = BucketRow,
                OperationId = (Guid?)null,
                CounterpartyKind = GarageKind,
                CounterpartyId = group.Key.GarageId,
                AccountingMonth = (DateOnly?)group.Key.AccountingMonth,
                OperationDate = (DateOnly?)null,
                CreatedAtUtc = (DateTimeOffset?)null,
                Amount = group.Sum(accrual => accrual.Amount)
            });
        var supplierBuckets = dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                visibleOperations.Any(operation => operation.SupplierId == accrual.SupplierId))
            .GroupBy(accrual => new { accrual.SupplierId, accrual.AccountingMonth })
            .Select(group => new
            {
                RowKind = BucketRow,
                OperationId = (Guid?)null,
                CounterpartyKind = SupplierKind,
                CounterpartyId = group.Key.SupplierId,
                AccountingMonth = (DateOnly?)group.Key.AccountingMonth,
                OperationDate = (DateOnly?)null,
                CreatedAtUtc = (DateTimeOffset?)null,
                Amount = group.Sum(accrual => accrual.Amount)
            });
        var rows = await visibleRows
            .Concat(paymentRows)
            .Concat(garageBuckets)
            .Concat(supplierBuckets)
            .ToArrayAsync(cancellationToken);
        var selectedOperations = rows.Where(row => row.RowKind == VisibleOperationRow).ToList();
        if (selectedOperations.Count == 0)
        {
            return new FinancialOperationDisplayData([], []);
        }

        var latestVisibleMonth = selectedOperations.Max(operation => operation.AccountingMonth!.Value);
        var calculationRows = rows.Where(row => row.RowKind == PaymentRow).ToList();
        var calculations = selectedOperations.Select(operation =>
        {
            var previousPaymentTotal = calculationRows
                .Where(previous =>
                    previous.OperationId != operation.OperationId &&
                    previous.CounterpartyKind == operation.CounterpartyKind &&
                    previous.CounterpartyId == operation.CounterpartyId &&
                    (previous.OperationDate < operation.OperationDate ||
                        (previous.OperationDate == operation.OperationDate &&
                            previous.CreatedAtUtc < operation.CreatedAtUtc)))
                .Sum(previous => previous.Amount);
            return new FinancialOperationCalculationData(
                operation.OperationId!.Value,
                operation.CounterpartyKind,
                operation.CounterpartyId,
                operation.AccountingMonth!.Value,
                previousPaymentTotal);
        }).ToList();
        var accrualBuckets = rows
            .Where(row => row.RowKind == BucketRow && row.AccountingMonth <= latestVisibleMonth)
            .Select(row => new FinancialOperationAccrualBucketData(
                row.CounterpartyKind,
                row.CounterpartyId,
                row.AccountingMonth!.Value,
                row.Amount))
            .ToList();

        return new FinancialOperationDisplayData(calculations, accrualBuckets);
    }
}
