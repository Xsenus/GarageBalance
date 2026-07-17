using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfAccrualPaymentAllocationRepository(GarageBalanceDbContext dbContext)
    : IAccrualPaymentAllocationRepository
{
    private const int AccrualRowKind = 1;
    private const int PaymentRowKind = 2;
    private const int AllocationRowKind = 3;

    public async Task<AccrualPaymentAllocationRebuildResult> RebuildAsync(
        IReadOnlyCollection<AccrualPaymentAllocationKey> keys,
        CancellationToken cancellationToken)
    {
        var distinctKeys = keys.Distinct().ToArray();
        if (distinctKeys.Length == 0)
        {
            return new AccrualPaymentAllocationRebuildResult(0, 0, 0);
        }

        var garageIds = distinctKeys.Select(key => key.GarageId).Distinct().ToArray();
        var incomeTypeIds = distinctKeys.Select(key => key.IncomeTypeId).Distinct().ToArray();
        var keySet = distinctKeys.ToHashSet();
        var rows = await BuildLedgerQuery(garageIds, incomeTypeIds).ToListAsync(cancellationToken);

        OverlayTrackedAccruals(rows, garageIds, incomeTypeIds);
        OverlayTrackedPayments(rows, garageIds, incomeTypeIds);
        var previousActiveAllocationCount = rows.Count(row =>
            row.Kind == AllocationRowKind && keySet.Contains(row.Key));
        var activeAllocationCount = 0;

        foreach (var row in rows.Where(row => row.Kind == AllocationRowKind && keySet.Contains(row.Key)))
        {
            var allocation = dbContext.AccrualPaymentAllocations.Local.FirstOrDefault(item => item.Id == row.Id);
            if (allocation is null)
            {
                allocation = new AccrualPaymentAllocation { Id = row.Id, IsActive = true };
                dbContext.AccrualPaymentAllocations.Attach(allocation);
            }

            allocation.IsActive = false;
        }

        foreach (var key in distinctKeys)
        {
            var plan = AccrualPaymentAllocator.Allocate(
                rows
                    .Where(row => row.Kind == AccrualRowKind && row.Key == key && !row.IsCanceled)
                    .Select(row => new AccrualPaymentAllocationAccrual(
                        row.Id, row.SortDate, row.AccountingMonth, row.Amount, row.CreatedAtUtc)),
                rows
                    .Where(row =>
                        row.Kind == PaymentRowKind &&
                        row.Key == key &&
                        !row.IsCanceled &&
                        row.OperationKind == FinancialOperationKinds.Income)
                    .Select(row => new AccrualPaymentAllocationPayment(
                        row.Id, row.SortDate, row.Amount, row.CreatedAtUtc)));

            dbContext.AccrualPaymentAllocations.AddRange(plan.Select(item => new AccrualPaymentAllocation
            {
                FinancialOperationId = item.FinancialOperationId,
                AccrualId = item.AccrualId,
                Amount = item.Amount
            }));
            activeAllocationCount += plan.Count;
        }

        return new AccrualPaymentAllocationRebuildResult(
            distinctKeys.Length,
            previousActiveAllocationCount,
            activeAllocationCount);
    }

    private IQueryable<AllocationLedgerRow> BuildLedgerQuery(Guid[] garageIds, Guid[] incomeTypeIds)
    {
        var accrualRows = dbContext.Accruals.AsNoTracking()
            .Where(item => garageIds.Contains(item.GarageId) && incomeTypeIds.Contains(item.IncomeTypeId))
            .Select(item => new
            {
                Kind = AccrualRowKind,
                item.Id,
                item.GarageId,
                item.IncomeTypeId,
                SortDate = item.DueDate,
                item.AccountingMonth,
                item.Amount,
                item.CreatedAtUtc,
                item.IsCanceled,
                OperationKind = string.Empty
            });
        var paymentRows = dbContext.FinancialOperations.AsNoTracking()
            .Where(item =>
                item.GarageId.HasValue && garageIds.Contains(item.GarageId.Value) &&
                item.IncomeTypeId.HasValue && incomeTypeIds.Contains(item.IncomeTypeId.Value))
            .Select(item => new
            {
                Kind = PaymentRowKind,
                item.Id,
                GarageId = item.GarageId!.Value,
                IncomeTypeId = item.IncomeTypeId!.Value,
                SortDate = item.OperationDate,
                item.AccountingMonth,
                item.Amount,
                item.CreatedAtUtc,
                item.IsCanceled,
                item.OperationKind
            });
        var allocationRows = dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(item =>
                item.IsActive &&
                garageIds.Contains(item.Accrual.GarageId) &&
                incomeTypeIds.Contains(item.Accrual.IncomeTypeId))
            .Select(item => new
            {
                Kind = AllocationRowKind,
                item.Id,
                item.Accrual.GarageId,
                item.Accrual.IncomeTypeId,
                SortDate = item.Accrual.DueDate,
                item.Accrual.AccountingMonth,
                item.Amount,
                item.CreatedAtUtc,
                IsCanceled = false,
                OperationKind = string.Empty
            });

        return accrualRows
            .Concat(paymentRows)
            .Concat(allocationRows)
            .Select(row => new AllocationLedgerRow(
                row.Kind,
                row.Id,
                row.GarageId,
                row.IncomeTypeId,
                row.SortDate,
                row.AccountingMonth,
                row.Amount,
                row.CreatedAtUtc,
                row.IsCanceled,
                row.OperationKind));
    }

    private void OverlayTrackedAccruals(List<AllocationLedgerRow> rows, Guid[] garageIds, Guid[] incomeTypeIds)
    {
        foreach (var accrual in dbContext.ChangeTracker.Entries<Accrual>().Select(entry => entry.Entity))
        {
            rows.RemoveAll(row => row.Kind == AccrualRowKind && row.Id == accrual.Id);
            if (garageIds.Contains(accrual.GarageId) && incomeTypeIds.Contains(accrual.IncomeTypeId))
            {
                rows.Add(new AllocationLedgerRow(
                    AccrualRowKind,
                    accrual.Id,
                    accrual.GarageId,
                    accrual.IncomeTypeId,
                    accrual.DueDate,
                    accrual.AccountingMonth,
                    accrual.Amount,
                    accrual.CreatedAtUtc,
                    accrual.IsCanceled,
                    string.Empty));
            }
        }
    }

    private void OverlayTrackedPayments(List<AllocationLedgerRow> rows, Guid[] garageIds, Guid[] incomeTypeIds)
    {
        foreach (var payment in dbContext.ChangeTracker.Entries<FinancialOperation>().Select(entry => entry.Entity))
        {
            rows.RemoveAll(row => row.Kind == PaymentRowKind && row.Id == payment.Id);
            if (payment.GarageId.HasValue && payment.IncomeTypeId.HasValue &&
                garageIds.Contains(payment.GarageId.Value) && incomeTypeIds.Contains(payment.IncomeTypeId.Value))
            {
                rows.Add(new AllocationLedgerRow(
                    PaymentRowKind,
                    payment.Id,
                    payment.GarageId.Value,
                    payment.IncomeTypeId.Value,
                    payment.OperationDate,
                    payment.AccountingMonth,
                    payment.Amount,
                    payment.CreatedAtUtc,
                    payment.IsCanceled,
                    payment.OperationKind));
            }
        }
    }

    private sealed record AllocationLedgerRow(
        int Kind,
        Guid Id,
        Guid GarageId,
        Guid IncomeTypeId,
        DateOnly SortDate,
        DateOnly AccountingMonth,
        decimal Amount,
        DateTimeOffset CreatedAtUtc,
        bool IsCanceled,
        string OperationKind)
    {
        public AccrualPaymentAllocationKey Key => new(GarageId, IncomeTypeId);
    }
}
