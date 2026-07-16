using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfExpenseWorksheetQuery(GarageBalanceDbContext dbContext) : IExpenseWorksheetQuery
{
    private const int SupplierAccrualCategory = 1;
    private const int SupplierExpenseCategory = 2;
    private const int StaffMemberCategory = 3;
    private const int StaffExpenseCategory = 4;
    private const int IncomeCategory = 5;

    public async Task<ExpenseWorksheetData> GetAsync(DateOnly accountingMonth, CancellationToken cancellationToken)
    {
        var supplierAccruals = dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.AccountingMonth == accountingMonth)
            .GroupBy(accrual => new
            {
                accrual.SupplierId,
                SupplierName = accrual.Supplier.Name,
                accrual.ExpenseTypeId,
                ExpenseTypeName = accrual.ExpenseType.Name,
                ExpenseTypeCode = accrual.ExpenseType.Code
            })
            .Select(group => new
            {
                Category = SupplierAccrualCategory,
                SupplierId = (Guid?)group.Key.SupplierId,
                StaffMemberId = (Guid?)null,
                CounterpartyName = (string?)group.Key.SupplierName,
                TypeId = (Guid?)group.Key.ExpenseTypeId,
                TypeName = (string?)group.Key.ExpenseTypeName,
                TypeCode = group.Key.ExpenseTypeCode,
                Amount = group.Sum(accrual => accrual.Amount)
            });

        var supplierExpenses = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.AccountingMonth == accountingMonth &&
                operation.SupplierId != null &&
                operation.ExpenseTypeId != null)
            .GroupBy(operation => new
            {
                SupplierId = operation.SupplierId!.Value,
                SupplierName = operation.Supplier!.Name,
                ExpenseTypeId = operation.ExpenseTypeId!.Value,
                ExpenseTypeName = operation.ExpenseType!.Name,
                ExpenseTypeCode = operation.ExpenseType.Code
            })
            .Select(group => new
            {
                Category = SupplierExpenseCategory,
                SupplierId = (Guid?)group.Key.SupplierId,
                StaffMemberId = (Guid?)null,
                CounterpartyName = (string?)group.Key.SupplierName,
                TypeId = (Guid?)group.Key.ExpenseTypeId,
                TypeName = (string?)group.Key.ExpenseTypeName,
                TypeCode = group.Key.ExpenseTypeCode,
                Amount = group.Sum(operation => operation.Amount)
            });

        var staffMembers = dbContext.StaffMembers.AsNoTracking()
            .Where(member => !member.IsArchived)
            .Select(member => new
            {
                Category = StaffMemberCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)member.Id,
                CounterpartyName = (string?)member.FullName,
                TypeId = (Guid?)null,
                TypeName = (string?)member.Department.Name,
                TypeCode = (string?)null,
                Amount = member.Rate
            });

        var staffExpenses = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.AccountingMonth == accountingMonth &&
                operation.StaffMemberId != null)
            .GroupBy(operation => operation.StaffMemberId!.Value)
            .Select(group => new
            {
                Category = StaffExpenseCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)group.Key,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)null,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = group.Sum(operation => operation.Amount)
            });

        var incomes = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.AccountingMonth == accountingMonth &&
                operation.IncomeTypeId != null)
            .GroupBy(operation => new
            {
                IncomeTypeName = operation.IncomeType!.Name,
                IncomeTypeCode = operation.IncomeType.Code
            })
            .Select(group => new
            {
                Category = IncomeCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)null,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)null,
                TypeName = (string?)group.Key.IncomeTypeName,
                TypeCode = group.Key.IncomeTypeCode,
                Amount = group.Sum(operation => operation.Amount)
            });

        var rows = await supplierAccruals
            .Concat(supplierExpenses)
            .Concat(staffMembers)
            .Concat(staffExpenses)
            .Concat(incomes)
            .ToListAsync(cancellationToken);

        return new ExpenseWorksheetData(
            rows.Where(row => row.Category == SupplierAccrualCategory)
                .Select(row => new ExpenseWorksheetSupplierData(
                    row.SupplierId!.Value,
                    row.CounterpartyName!,
                    row.TypeId!.Value,
                    row.TypeName!,
                    row.TypeCode,
                    row.Amount))
                .ToList(),
            rows.Where(row => row.Category == SupplierExpenseCategory)
                .Select(row => new ExpenseWorksheetSupplierData(
                    row.SupplierId!.Value,
                    row.CounterpartyName!,
                    row.TypeId!.Value,
                    row.TypeName!,
                    row.TypeCode,
                    row.Amount))
                .ToList(),
            rows.Where(row => row.Category == StaffMemberCategory)
                .Select(row => new ExpenseWorksheetStaffData(
                    row.StaffMemberId!.Value,
                    row.CounterpartyName!,
                    row.TypeName!,
                    row.Amount))
                .ToList(),
            rows.Where(row => row.Category == StaffExpenseCategory)
                .Select(row => new ExpenseWorksheetStaffExpenseData(row.StaffMemberId!.Value, row.Amount))
                .ToList(),
            rows.Where(row => row.Category == IncomeCategory)
                .Select(row => new ExpenseWorksheetIncomeData(row.TypeName!, row.TypeCode, row.Amount))
                .ToList());
    }
}
