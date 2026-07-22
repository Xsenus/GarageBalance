using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFinancialReportPeriodQuery(GarageBalanceDbContext dbContext) : IFinancialReportPeriodQuery
{
    public Task<FinancialReportPeriodData?> GetAsync(
        Guid? garageId,
        Guid? supplierId,
        Guid? staffMemberId,
        CancellationToken cancellationToken)
    {
        if (garageId.HasValue)
        {
            return dbContext.Garages.AsNoTracking()
                .Where(garage => garage.Id == garageId.Value && !garage.IsArchived)
                .Select(_ => new FinancialReportPeriodData(
                    dbContext.Accruals
                        .Where(accrual => accrual.GarageId == garageId.Value && !accrual.IsCanceled)
                        .Min(accrual => (DateOnly?)accrual.AccountingMonth),
                    dbContext.FinancialOperations
                        .Where(operation => operation.GarageId == garageId.Value && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income)
                        .Min(operation => (DateOnly?)operation.AccountingMonth),
                    dbContext.Accruals
                        .Where(accrual => accrual.GarageId == garageId.Value && !accrual.IsCanceled)
                        .Max(accrual => (DateOnly?)accrual.AccountingMonth),
                    dbContext.FinancialOperations
                        .Where(operation => operation.GarageId == garageId.Value && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income)
                        .Max(operation => (DateOnly?)operation.AccountingMonth)))
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (supplierId.HasValue)
        {
            return dbContext.Suppliers.AsNoTracking()
                .Where(supplier => supplier.Id == supplierId.Value)
                .Select(_ => new FinancialReportPeriodData(
                    dbContext.SupplierAccruals
                        .Where(accrual => accrual.SupplierId == supplierId.Value && !accrual.IsCanceled)
                        .Min(accrual => (DateOnly?)accrual.AccountingMonth),
                    dbContext.FinancialOperations
                        .Where(operation => operation.SupplierId == supplierId.Value && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Expense)
                        .Min(operation => (DateOnly?)operation.AccountingMonth),
                    dbContext.SupplierAccruals
                        .Where(accrual => accrual.SupplierId == supplierId.Value && !accrual.IsCanceled)
                        .Max(accrual => (DateOnly?)accrual.AccountingMonth),
                    dbContext.FinancialOperations
                        .Where(operation => operation.SupplierId == supplierId.Value && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Expense)
                        .Max(operation => (DateOnly?)operation.AccountingMonth)))
                .SingleOrDefaultAsync(cancellationToken);
        }

        var staffId = staffMemberId.GetValueOrDefault();
        return dbContext.StaffMembers.AsNoTracking()
            .Where(staffMember => staffMember.Id == staffId)
            .Select(_ => new FinancialReportPeriodData(
                null,
                dbContext.FinancialOperations
                    .Where(operation => operation.StaffMemberId == staffId && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Expense)
                    .Min(operation => (DateOnly?)operation.AccountingMonth),
                null,
                dbContext.FinancialOperations
                    .Where(operation => operation.StaffMemberId == staffId && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Expense)
                    .Max(operation => (DateOnly?)operation.AccountingMonth)))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
