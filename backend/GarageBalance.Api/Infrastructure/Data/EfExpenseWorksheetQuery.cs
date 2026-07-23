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
    private const int AvailableBalanceCategory = 6;
    private const int BankDepositCategory = 7;
    private const int SupplierOpeningAccrualCategory = 8;
    private const int SupplierOpeningExpenseCategory = 9;
    private const int StaffOpeningExpenseCategory = 10;
    private const int OpeningIncomeCategory = 11;
    private const int StaffBonusCategory = 12;
    private const int StaffPenaltyCategory = 13;
    private const int StaffOpeningBonusCategory = 14;
    private const int StaffOpeningPenaltyCategory = 15;

    public async Task<ExpenseWorksheetData> GetAsync(
        DateOnly accountingMonth,
        string[] cashExpenseTypeCodes,
        string[] cashExpenseTypeNames,
        CancellationToken cancellationToken)
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
                Amount = group.Sum(accrual => accrual.Amount),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
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
                Amount = group.Sum(operation => operation.Amount),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var staffMembers = dbContext.StaffMembers.AsNoTracking()
            .Where(member => !member.IsArchived)
            .SelectMany(
                _ => dbContext.ExpenseTypes.AsNoTracking()
                    .Where(expenseType => !expenseType.IsArchived && expenseType.Code == "salary"),
                (member, expenseType) => new
                {
                    Category = StaffMemberCategory,
                    SupplierId = (Guid?)null,
                    StaffMemberId = (Guid?)member.Id,
                    CounterpartyName = (string?)member.FullName,
                    TypeId = (Guid?)expenseType.Id,
                    TypeName = (string?)expenseType.Name,
                    TypeCode = expenseType.Code,
                    Amount = member.Rate,
                    IncomeTotal = 0m,
                    BankDepositTotal = 0m,
                    CashExpenseTotal = 0m,
                    BankExpenseTotal = 0m,
                    HistoryStartMonth = (DateOnly?)null,
                    StaffCreatedAtUtc = (DateTimeOffset?)member.CreatedAtUtc
                });

        var staffExpenses = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.AccountingMonth == accountingMonth &&
                operation.StaffMemberId != null &&
                operation.ExpenseTypeId != null)
            .GroupBy(operation => new
            {
                StaffMemberId = operation.StaffMemberId!.Value,
                ExpenseTypeId = operation.ExpenseTypeId!.Value,
                ExpenseTypeName = operation.ExpenseType!.Name,
                ExpenseTypeCode = operation.ExpenseType.Code
            })
            .Select(group => new
            {
                Category = StaffExpenseCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)group.Key.StaffMemberId,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)group.Key.ExpenseTypeId,
                TypeName = (string?)group.Key.ExpenseTypeName,
                TypeCode = group.Key.ExpenseTypeCode,
                Amount = group.Sum(operation => operation.Amount),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
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
                Amount = group.Sum(operation => operation.Amount),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var openingIncomes = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.AccountingMonth < accountingMonth &&
                operation.IncomeTypeId != null)
            .GroupBy(operation => new
            {
                IncomeTypeName = operation.IncomeType!.Name,
                IncomeTypeCode = operation.IncomeType.Code
            })
            .Select(group => new
            {
                Category = OpeningIncomeCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)null,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)null,
                TypeName = (string?)group.Key.IncomeTypeName,
                TypeCode = group.Key.IncomeTypeCode,
                Amount = group.Sum(operation => operation.Amount),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var supplierOpeningAccruals = dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.AccountingMonth < accountingMonth)
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
                Category = SupplierOpeningAccrualCategory,
                SupplierId = (Guid?)group.Key.SupplierId,
                StaffMemberId = (Guid?)null,
                CounterpartyName = (string?)group.Key.SupplierName,
                TypeId = (Guid?)group.Key.ExpenseTypeId,
                TypeName = (string?)group.Key.ExpenseTypeName,
                TypeCode = group.Key.ExpenseTypeCode,
                Amount = group.Sum(accrual => accrual.Amount),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var supplierOpeningExpenses = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.AccountingMonth < accountingMonth &&
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
                Category = SupplierOpeningExpenseCategory,
                SupplierId = (Guid?)group.Key.SupplierId,
                StaffMemberId = (Guid?)null,
                CounterpartyName = (string?)group.Key.SupplierName,
                TypeId = (Guid?)group.Key.ExpenseTypeId,
                TypeName = (string?)group.Key.ExpenseTypeName,
                TypeCode = group.Key.ExpenseTypeCode,
                Amount = group.Sum(operation => operation.Amount),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var staffOpeningExpenses = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.AccountingMonth < accountingMonth &&
                operation.StaffMemberId != null &&
                operation.ExpenseTypeId != null)
            .GroupBy(operation => new
            {
                StaffMemberId = operation.StaffMemberId!.Value,
                ExpenseTypeId = operation.ExpenseTypeId!.Value,
                ExpenseTypeName = operation.ExpenseType!.Name,
                ExpenseTypeCode = operation.ExpenseType.Code
            })
            .Select(group => new
            {
                Category = StaffOpeningExpenseCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)group.Key.StaffMemberId,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)group.Key.ExpenseTypeId,
                TypeName = (string?)group.Key.ExpenseTypeName,
                TypeCode = group.Key.ExpenseTypeCode,
                Amount = group.Sum(operation => operation.Amount),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)group.Min(operation => operation.AccountingMonth),
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var staffBonuses = dbContext.StaffSalaryAdjustments.AsNoTracking()
            .Where(adjustment =>
                adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Bonus &&
                adjustment.AccountingMonth == accountingMonth)
            .GroupBy(adjustment => adjustment.StaffMemberId)
            .Select(group => new
            {
                Category = StaffBonusCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)group.Key,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)null,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = group.Sum(adjustment => adjustment.Amount),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });
        var staffPenalties = dbContext.StaffSalaryAdjustments.AsNoTracking()
            .Where(adjustment =>
                adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Penalty &&
                adjustment.AccountingMonth == accountingMonth)
            .GroupBy(adjustment => adjustment.StaffMemberId)
            .Select(group => new
            {
                Category = StaffPenaltyCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)group.Key,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)null,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = group.Sum(adjustment => adjustment.Amount),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });
        var staffOpeningBonuses = dbContext.StaffSalaryAdjustments.AsNoTracking()
            .Where(adjustment =>
                adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Bonus &&
                adjustment.AccountingMonth < accountingMonth)
            .GroupBy(adjustment => adjustment.StaffMemberId)
            .Select(group => new
            {
                Category = StaffOpeningBonusCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)group.Key,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)null,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = group.Sum(adjustment => adjustment.Amount),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });
        var staffOpeningPenalties = dbContext.StaffSalaryAdjustments.AsNoTracking()
            .Where(adjustment =>
                adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Penalty &&
                adjustment.AccountingMonth < accountingMonth)
            .GroupBy(adjustment => adjustment.StaffMemberId)
            .Select(group => new
            {
                Category = StaffOpeningPenaltyCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)group.Key,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)null,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = group.Sum(adjustment => adjustment.Amount),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var availableBalance = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = AvailableBalanceCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)null,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)null,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = 0m,
                IncomeTotal = group.Sum(operation => operation.OperationKind == FinancialOperationKinds.Income ? operation.Amount : 0m),
                BankDepositTotal = 0m,
                CashExpenseTotal = group.Sum(operation =>
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    (operation.ExpensePaymentType == ExpensePaymentTypes.WithoutReceipt ||
                        (operation.ExpensePaymentType == null &&
                            operation.ExpenseType != null &&
                            ((operation.ExpenseType.Code != null && cashExpenseTypeCodes.Contains(operation.ExpenseType.Code)) ||
                                cashExpenseTypeNames.Contains(operation.ExpenseType.Name))))
                        ? operation.Amount
                        : 0m),
                BankExpenseTotal = group.Sum(operation =>
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    operation.ExpensePaymentType != ExpensePaymentTypes.WithoutReceipt &&
                    (operation.ExpensePaymentType != null ||
                        operation.ExpenseType == null ||
                        !((operation.ExpenseType.Code != null && cashExpenseTypeCodes.Contains(operation.ExpenseType.Code)) ||
                            cashExpenseTypeNames.Contains(operation.ExpenseType.Name)))
                        ? operation.Amount
                        : 0m),
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var bankDeposits = dbContext.FundOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FundOperationKinds.Deposit &&
                operation.IsCashToBankTransfer)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = BankDepositCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)null,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)null,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = 0m,
                IncomeTotal = 0m,
                BankDepositTotal = group.Sum(operation => operation.Amount),
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var rows = await supplierAccruals
            .Concat(supplierExpenses)
            .Concat(staffMembers)
            .Concat(staffExpenses)
            .Concat(incomes)
            .Concat(openingIncomes)
            .Concat(supplierOpeningAccruals)
            .Concat(supplierOpeningExpenses)
            .Concat(staffOpeningExpenses)
            .Concat(staffBonuses)
            .Concat(staffPenalties)
            .Concat(staffOpeningBonuses)
            .Concat(staffOpeningPenalties)
            .Concat(availableBalance)
            .Concat(bankDeposits)
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
                    row.Amount)
                {
                    ExpenseTypeId = row.TypeId!.Value,
                    ExpenseTypeCode = row.TypeCode,
                    CreatedAtUtc = row.StaffCreatedAtUtc!.Value
                })
                .ToList(),
            rows.Where(row => row.Category == StaffExpenseCategory)
                .Select(row => new ExpenseWorksheetStaffExpenseData(row.StaffMemberId!.Value, row.Amount)
                {
                    ExpenseTypeId = row.TypeId!.Value
                })
                .ToList(),
            rows.Where(row => row.Category == IncomeCategory)
                .Select(row => new ExpenseWorksheetIncomeData(row.TypeName!, row.TypeCode, row.Amount))
                .ToList(),
            new FinanceAvailableBalanceData(
                rows.Sum(row => row.IncomeTotal),
                rows.Sum(row => row.BankDepositTotal),
                rows.Sum(row => row.CashExpenseTotal),
                rows.Sum(row => row.BankExpenseTotal)))
        {
            SupplierOpeningAccruals = rows.Where(row => row.Category == SupplierOpeningAccrualCategory)
                .Select(row => new ExpenseWorksheetSupplierData(
                    row.SupplierId!.Value,
                    row.CounterpartyName!,
                    row.TypeId!.Value,
                    row.TypeName!,
                    row.TypeCode,
                    row.Amount))
                .ToList(),
            SupplierOpeningExpenses = rows.Where(row => row.Category == SupplierOpeningExpenseCategory)
                .Select(row => new ExpenseWorksheetSupplierData(
                    row.SupplierId!.Value,
                    row.CounterpartyName!,
                    row.TypeId!.Value,
                    row.TypeName!,
                    row.TypeCode,
                    row.Amount))
                .ToList(),
            StaffOpeningExpenses = rows.Where(row => row.Category == StaffOpeningExpenseCategory)
                .Select(row => new ExpenseWorksheetStaffExpenseData(row.StaffMemberId!.Value, row.Amount)
                {
                    ExpenseTypeId = row.TypeId!.Value,
                    FirstAccountingMonth = row.HistoryStartMonth
                })
                .ToList(),
            OpeningIncomes = rows.Where(row => row.Category == OpeningIncomeCategory)
                .Select(row => new ExpenseWorksheetIncomeData(row.TypeName!, row.TypeCode, row.Amount))
                .ToList(),
            StaffBonuses = rows.Where(row => row.Category == StaffBonusCategory)
                .Select(row => new ExpenseWorksheetStaffAdjustmentData(row.StaffMemberId!.Value, row.Amount))
                .ToList(),
            StaffPenalties = rows.Where(row => row.Category == StaffPenaltyCategory)
                .Select(row => new ExpenseWorksheetStaffAdjustmentData(row.StaffMemberId!.Value, row.Amount))
                .ToList(),
            StaffOpeningBonuses = rows.Where(row => row.Category == StaffOpeningBonusCategory)
                .Select(row => new ExpenseWorksheetStaffAdjustmentData(row.StaffMemberId!.Value, row.Amount))
                .ToList(),
            StaffOpeningPenalties = rows.Where(row => row.Category == StaffOpeningPenaltyCategory)
                .Select(row => new ExpenseWorksheetStaffAdjustmentData(row.StaffMemberId!.Value, row.Amount))
                .ToList()
        };
    }

}
