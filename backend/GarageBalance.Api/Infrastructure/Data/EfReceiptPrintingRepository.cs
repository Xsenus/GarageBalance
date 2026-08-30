using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfReceiptPrintingRepository(GarageBalanceDbContext dbContext) : IReceiptPrintingRepository
{
    public async Task<IReadOnlyList<FinancialOperation>> FindReceiptOperationsAsync(Guid financialOperationId, CancellationToken cancellationToken)
    {
        var anchorBatchId = dbContext.FinancialOperations
            .AsNoTracking()
            .Where(item => item.Id == financialOperationId)
            .Select(item => item.ReceiptBatchId);

        var rows = await dbContext.FinancialOperations
            .AsNoTracking()
            .Where(item =>
                item.Id == financialOperationId ||
                (item.ReceiptBatchId != null && item.ReceiptBatchId == anchorBatchId.FirstOrDefault()))
            .OrderBy(item => item.AccountingMonth)
            .ThenBy(item => item.IncomeType == null ? null : item.IncomeType.Name)
            .ThenBy(item => item.Id)
            .Select(item => new ReceiptOperationRow(
                item.Id,
                item.OperationKind,
                item.OperationDate,
                item.AccountingMonth,
                item.Amount,
                item.ReceiptBatchId,
                item.DocumentNumber,
                item.IsCanceled,
                item.GarageId,
                item.Garage == null ? null : item.Garage.Number,
                item.Garage == null ? null : item.Garage.OwnerId,
                item.Garage == null || item.Garage.Owner == null ? null : item.Garage.Owner.LastName,
                item.Garage == null || item.Garage.Owner == null ? null : item.Garage.Owner.FirstName,
                item.Garage == null || item.Garage.Owner == null ? null : item.Garage.Owner.MiddleName,
                item.IncomeTypeId,
                item.IncomeType == null ? null : item.IncomeType.Name))
            .Take(ReceiptPrintingLimits.MaximumLineCount + 1)
            .ToListAsync(cancellationToken);

        return rows.Select(CreateOperation).ToArray();
    }

    public async Task<IReadOnlyList<ReceiptPrintingAllocationData>> GetActiveAllocationsAsync(
        IReadOnlyCollection<Guid> financialOperationIds,
        CancellationToken cancellationToken)
    {
        if (financialOperationIds.Count == 0)
        {
            return [];
        }

        return await dbContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item =>
                financialOperationIds.Contains(item.FinancialOperationId) &&
                item.IsActive &&
                !item.Accrual.IsCanceled)
            .OrderBy(item => item.Accrual.AccountingMonth)
            .ThenBy(item => item.Accrual.IncomeType.Name)
            .Select(item => new ReceiptPrintingAllocationData(
                item.FinancialOperationId,
                item.AccrualId,
                item.Accrual.AccountingMonth,
                item.Accrual.IncomeType.Name,
                item.Amount))
            .ToListAsync(cancellationToken);
    }

    private static FinancialOperation CreateOperation(ReceiptOperationRow row)
    {
        var owner = row.OwnerId is null
            ? null
            : new Owner
            {
                Id = row.OwnerId.Value,
                LastName = row.OwnerLastName ?? string.Empty,
                FirstName = row.OwnerFirstName ?? string.Empty,
                MiddleName = row.OwnerMiddleName
            };
        var garage = row.GarageId is null
            ? null
            : new Garage
            {
                Id = row.GarageId.Value,
                Number = row.GarageNumber ?? string.Empty,
                OwnerId = row.OwnerId,
                Owner = owner
            };
        var incomeType = row.IncomeTypeId is null
            ? null
            : new IncomeType
            {
                Id = row.IncomeTypeId.Value,
                Name = row.IncomeTypeName ?? string.Empty
            };

        return new FinancialOperation
        {
            Id = row.Id,
            OperationKind = row.OperationKind,
            OperationDate = row.OperationDate,
            AccountingMonth = row.AccountingMonth,
            Amount = row.Amount,
            ReceiptBatchId = row.ReceiptBatchId,
            DocumentNumber = row.DocumentNumber,
            IsCanceled = row.IsCanceled,
            GarageId = row.GarageId,
            Garage = garage,
            IncomeTypeId = row.IncomeTypeId,
            IncomeType = incomeType
        };
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record ReceiptOperationRow(
        Guid Id,
        string OperationKind,
        DateOnly OperationDate,
        DateOnly AccountingMonth,
        decimal Amount,
        Guid? ReceiptBatchId,
        string? DocumentNumber,
        bool IsCanceled,
        Guid? GarageId,
        string? GarageNumber,
        Guid? OwnerId,
        string? OwnerLastName,
        string? OwnerFirstName,
        string? OwnerMiddleName,
        Guid? IncomeTypeId,
        string? IncomeTypeName);
}
