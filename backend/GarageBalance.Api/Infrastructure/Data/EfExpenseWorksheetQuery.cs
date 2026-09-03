using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfExpenseWorksheetQuery(
    GarageBalanceDbContext dbContext,
    IBusinessDateProvider? businessDateProvider = null) : IExpenseWorksheetQuery
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
    private const int BalanceAdjustmentCategory = 16;
    private const int SupplierFundCategory = 17;
    private const int SalaryConfigurationCategory = 18;
    private const int SupplierStartingBalanceCategory = 19;
    private const int EpisodicExpenseCategory = 20;
    private const int StaffStateCategory = 21;
    private const int StaffSalaryRatePeriodCategory = 22;
    private const int StaffEmploymentPeriodStartCategory = 23;
    private const int StaffEmploymentPeriodEndCategory = 24;

    public Task<ExpenseWorksheetSupplierBreakdownData> GetSupplierBreakdownAsync(
        Guid supplierId,
        Guid expenseTypeId,
        DateOnly monthFrom,
        DateOnly monthTo,
        int offset,
        int limit,
        CancellationToken cancellationToken) =>
        EfExpenseWorksheetSupplierBreakdownQuery.GetAsync(
            dbContext,
            supplierId,
            expenseTypeId,
            monthFrom,
            monthTo,
            offset,
            limit,
            cancellationToken);

    public Task<ExpenseWorksheetStaffBreakdownData> GetStaffBreakdownAsync(
        Guid staffMemberId,
        Guid? expenseTypeId,
        DateOnly monthFrom,
        DateOnly monthTo,
        DateOnly businessDate,
        string businessTimeZoneId,
        int offset,
        int limit,
        CancellationToken cancellationToken) =>
        EfExpenseWorksheetStaffBreakdownQuery.GetAsync(
            dbContext,
            businessDate,
            businessTimeZoneId,
            staffMemberId,
            expenseTypeId,
            monthFrom,
            monthTo,
            offset,
            limit,
            cancellationToken);

    public Task<ExpenseWorksheetData> GetAsync(
        DateOnly accountingMonth,
        string[] cashExpenseTypeCodes,
        string[] cashExpenseTypeNames,
        CancellationToken cancellationToken) =>
        GetAsync(accountingMonth, accountingMonth, cashExpenseTypeCodes, cashExpenseTypeNames, cancellationToken);

    public async Task<ExpenseWorksheetData> GetAsync(
        DateOnly monthFrom,
        DateOnly monthTo,
        string[] cashExpenseTypeCodes,
        string[] cashExpenseTypeNames,
        CancellationToken cancellationToken)
    {
        var configuredSalaryAccrualDay = dbContext.ApplicationSettings
            .AsNoTracking()
            .Where(setting => setting.Key == ApplicationSettingsService.SalaryAccrualDayKey)
            .Select(setting => setting.IntegerValue);
        var businessDate = businessDateProvider?.Today;
        var businessMonth = businessDate is null
            ? (DateOnly?)null
            : new DateOnly(businessDate.Value.Year, businessDate.Value.Month, 1);

        var supplierAccruals = dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.AccountingMonth >= monthFrom && accrual.AccountingMonth <= monthTo)
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
                operation.AccountingMonth >= monthFrom && operation.AccountingMonth <= monthTo &&
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
                    Amount = businessDate == null ||
                        monthFrom < businessMonth!.Value ||
                        (monthFrom == businessMonth.Value &&
                            businessDate.Value.Day >= (configuredSalaryAccrualDay.FirstOrDefault() ?? ApplicationSettingsService.DefaultSalaryAccrualDay))
                            ? member.Rate
                            : 0m,
                    IncomeTotal = 0m,
                    BankDepositTotal = 0m,
                    CashExpenseTotal = 0m,
                    BankExpenseTotal = 0m,
                    HistoryStartMonth = (DateOnly?)null,
                    StaffCreatedAtUtc = (DateTimeOffset?)member.CreatedAtUtc
                });

        var staffStates = dbContext.StaffMembers.AsNoTracking()
            .Select(member => new
            {
                Category = StaffStateCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)member.Id,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)null,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = member.IsArchived ? 1m : 0m,
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)member.UpdatedAtUtc
            });

        var staffSalaryRatePeriods = dbContext.StaffSalaryRatePeriods.AsNoTracking()
            .Where(period => period.EffectiveFrom <= monthTo)
            .Select(period => new
            {
                Category = StaffSalaryRatePeriodCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)period.StaffMemberId,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)period.Id,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = period.Rate,
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)period.EffectiveFrom,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var staffEmploymentPeriodStarts = dbContext.StaffEmploymentPeriods.AsNoTracking()
            .Where(period => period.EffectiveFrom <= monthTo)
            .Select(period => new
            {
                Category = StaffEmploymentPeriodStartCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)period.StaffMemberId,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)period.Id,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = 0m,
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)period.EffectiveFrom,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var staffEmploymentPeriodEnds = dbContext.StaffEmploymentPeriods.AsNoTracking()
            .Where(period => period.EffectiveFrom <= monthTo && period.EffectiveTo != null)
            .Select(period => new
            {
                Category = StaffEmploymentPeriodEndCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)period.StaffMemberId,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)period.Id,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = 0m,
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = period.EffectiveTo,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var staffExpenses = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.AccountingMonth >= monthFrom && operation.AccountingMonth <= monthTo &&
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
                operation.AccountingMonth >= monthFrom && operation.AccountingMonth <= monthTo &&
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
                operation.AccountingMonth < monthFrom &&
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
            .Where(accrual => !accrual.IsCanceled && accrual.AccountingMonth < monthFrom)
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
                operation.AccountingMonth < monthFrom &&
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
                operation.AccountingMonth < monthFrom &&
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
                !adjustment.IsCanceled &&
                adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Bonus &&
                adjustment.AccountingMonth >= monthFrom && adjustment.AccountingMonth <= monthTo)
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
                !adjustment.IsCanceled &&
                adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Penalty &&
                adjustment.AccountingMonth >= monthFrom && adjustment.AccountingMonth <= monthTo)
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
                !adjustment.IsCanceled &&
                adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Bonus &&
                adjustment.AccountingMonth < monthFrom)
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
                !adjustment.IsCanceled &&
                adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Penalty &&
                adjustment.AccountingMonth < monthFrom)
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
                    (operation.ExpensePaymentSource == ExpensePaymentSources.Cash ||
                        (operation.ExpensePaymentSource == null &&
                            (operation.ExpensePaymentType == ExpensePaymentTypes.WithoutReceipt ||
                                (operation.ExpensePaymentType == null &&
                            operation.ExpenseType != null &&
                            ((operation.ExpenseType.Code != null && cashExpenseTypeCodes.Contains(operation.ExpenseType.Code)) ||
                                    cashExpenseTypeNames.Contains(operation.ExpenseType.Name))))))
                        ? operation.Amount
                        : 0m),
                BankExpenseTotal = group.Sum(operation =>
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    (operation.ExpensePaymentSource == ExpensePaymentSources.Bank ||
                        (operation.ExpensePaymentSource == null &&
                            operation.ExpensePaymentType != ExpensePaymentTypes.WithoutReceipt &&
                            (operation.ExpensePaymentType != null ||
                                operation.ExpenseType == null ||
                                !((operation.ExpenseType.Code != null && cashExpenseTypeCodes.Contains(operation.ExpenseType.Code)) ||
                                    cashExpenseTypeNames.Contains(operation.ExpenseType.Name)))))
                        ? operation.Amount
                        : 0m),
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var bankDeposits = dbContext.CashBankTransfers.AsNoTracking()
            .Where(transfer => !transfer.IsCanceled)
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
                BankDepositTotal = group.Sum(transfer => transfer.Amount),
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var balanceAdjustments = dbContext.CashBankBalanceOperations.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = BalanceAdjustmentCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)null,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)null,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = 0m,
                IncomeTotal = group.Sum(operation =>
                    operation.Direction == CashBankBalanceDirections.Increase
                        ? operation.Amount
                        : -operation.Amount),
                BankDepositTotal = group.Sum(operation =>
                    operation.Account == CashBankAccounts.Bank
                        ? operation.Direction == CashBankBalanceDirections.Increase
                            ? operation.Amount
                            : -operation.Amount
                        : 0m),
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var supplierFunds = dbContext.Suppliers
            .AsNoTracking()
            .Where(supplier =>
                !supplier.IsArchived &&
                supplier.ChargeServiceSetting != null &&
                !supplier.ChargeServiceSetting.IsArchived &&
                supplier.ExpenseTypeId != null &&
                supplier.ExpenseFundId != null &&
                !supplier.ExpenseFund!.IsArchived)
            .Select(supplier => new
            {
                Category = SupplierFundCategory,
                SupplierId = (Guid?)supplier.Id,
                StaffMemberId = (Guid?)supplier.ExpenseFundId,
                CounterpartyName = (string?)supplier.ExpenseFund!.Name,
                TypeId = supplier.ExpenseTypeId,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = supplier.ExpenseFund!.Balance,
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var episodicExpenses = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.AccountingMonth >= monthFrom && operation.AccountingMonth <= monthTo &&
                operation.SupplierId == null &&
                operation.StaffMemberId == null &&
                operation.ExpenseTypeId != null)
            .GroupBy(operation => new
            {
                operation.CounterpartyName,
                ExpenseTypeId = operation.ExpenseTypeId!.Value,
                ExpenseTypeName = operation.ExpenseType!.Name,
                ExpenseTypeCode = operation.ExpenseType.Code
            })
            .Select(group => new
            {
                Category = EpisodicExpenseCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)null,
                CounterpartyName = group.Key.CounterpartyName,
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

        var supplierStartingBalances = dbContext.Suppliers
            .AsNoTracking()
            .Where(supplier =>
                !supplier.IsArchived &&
                supplier.StartingBalance != 0 &&
                supplier.ExpenseTypeId != null &&
                supplier.ExpenseType != null &&
                !supplier.ExpenseType.IsArchived)
            .Select(supplier => new
            {
                Category = SupplierStartingBalanceCategory,
                SupplierId = (Guid?)supplier.Id,
                StaffMemberId = (Guid?)null,
                CounterpartyName = (string?)supplier.Name,
                TypeId = supplier.ExpenseTypeId,
                TypeName = (string?)supplier.ExpenseType!.Name,
                TypeCode = supplier.ExpenseType.Code,
                Amount = supplier.StartingBalance,
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var salaryConfiguration = dbContext.ApplicationSettings
            .AsNoTracking()
            .Where(setting => setting.Key == ApplicationSettingsService.SalaryAccrualDayKey)
            .Select(setting => new
            {
                Category = SalaryConfigurationCategory,
                SupplierId = (Guid?)null,
                StaffMemberId = (Guid?)null,
                CounterpartyName = (string?)null,
                TypeId = (Guid?)null,
                TypeName = (string?)null,
                TypeCode = (string?)null,
                Amount = (decimal)(setting.IntegerValue ?? ApplicationSettingsService.DefaultSalaryAccrualDay),
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                HistoryStartMonth = (DateOnly?)null,
                StaffCreatedAtUtc = (DateTimeOffset?)null
            });

        var rows = await supplierAccruals
            .Concat(supplierExpenses)
            .Concat(episodicExpenses)
            .Concat(staffMembers)
            .Concat(staffStates)
            .Concat(staffSalaryRatePeriods)
            .Concat(staffEmploymentPeriodStarts)
            .Concat(staffEmploymentPeriodEnds)
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
            .Concat(balanceAdjustments)
            .Concat(supplierFunds)
            .Concat(salaryConfiguration)
            .Concat(supplierStartingBalances)
            .ToListAsync(cancellationToken);

        var salaryAccrualMonthTo = monthTo;
        if (businessDate is { } currentBusinessDate)
        {
            var currentBusinessMonth = new DateOnly(currentBusinessDate.Year, currentBusinessDate.Month, 1);
            var salaryAccrualDay = (int)(rows.FirstOrDefault(row => row.Category == SalaryConfigurationCategory)?.Amount
                ?? ApplicationSettingsService.DefaultSalaryAccrualDay);
            if (monthTo >= currentBusinessMonth && (monthFrom > currentBusinessMonth || currentBusinessDate.Day < salaryAccrualDay))
            {
                salaryAccrualMonthTo = currentBusinessMonth.AddMonths(-1);
            }
            else if (monthTo > currentBusinessMonth)
            {
                salaryAccrualMonthTo = currentBusinessMonth;
            }
        }

        var staffRows = rows.Where(row => row.Category == StaffMemberCategory).ToList();
        var salaryRatePeriods = rows
            .Where(row => row.Category == StaffSalaryRatePeriodCategory)
            .Select(row => new StaffSalaryRatePeriod
            {
                Id = row.TypeId!.Value,
                StaffMemberId = row.StaffMemberId!.Value,
                EffectiveFrom = row.HistoryStartMonth!.Value,
                Rate = row.Amount
            })
            .OrderBy(period => period.EffectiveFrom)
            .ToList();
        var employmentEnds = rows
            .Where(row => row.Category == StaffEmploymentPeriodEndCategory)
            .ToDictionary(row => row.TypeId!.Value, row => row.HistoryStartMonth!.Value);
        var employmentPeriods = rows
            .Where(row => row.Category == StaffEmploymentPeriodStartCategory)
            .Select(row => new StaffEmploymentPeriod
            {
                Id = row.TypeId!.Value,
                StaffMemberId = row.StaffMemberId!.Value,
                EffectiveFrom = row.HistoryStartMonth!.Value,
                EffectiveTo = employmentEnds.TryGetValue(row.TypeId!.Value, out var effectiveTo) ? effectiveTo : null
            })
            .OrderBy(period => period.EffectiveFrom)
            .ToList();
        var ratePeriodsByStaff = salaryRatePeriods.ToLookup(period => period.StaffMemberId);
        var employmentPeriodsByStaff = employmentPeriods.ToLookup(period => period.StaffMemberId);
        var staffStateByMember = rows
            .Where(row => row.Category == StaffStateCategory)
            .ToDictionary(
                row => row.StaffMemberId.GetValueOrDefault(),
                row => new StaffState(row.StaffMemberId.GetValueOrDefault(), row.Amount != 0m, row.StaffCreatedAtUtc.GetValueOrDefault()));

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
            staffRows
                .Select(row => new ExpenseWorksheetStaffData(
                    row.StaffMemberId!.Value,
                    row.CounterpartyName!,
                    row.TypeName!,
                    row.Amount)
                {
                    ExpenseTypeId = row.TypeId!.Value,
                    ExpenseTypeCode = row.TypeCode,
                    CreatedAtUtc = row.StaffCreatedAtUtc!.Value,
                    BaseAccrualAmount = StaffSalaryTimeline.CalculateBaseAccrual(
                        monthFrom,
                        salaryAccrualMonthTo,
                        row.Amount,
                        MonthPeriod.Normalize(businessDateProvider?.ToBusinessDate(row.StaffCreatedAtUtc!.Value)
                            ?? DateOnly.FromDateTime(row.StaffCreatedAtUtc.Value.UtcDateTime)),
                        staffStateByMember[row.StaffMemberId.Value].IsArchived,
                        MonthPeriod.Normalize(businessDateProvider?.ToBusinessDate(staffStateByMember[row.StaffMemberId.Value].UpdatedAtUtc)
                            ?? DateOnly.FromDateTime(staffStateByMember[row.StaffMemberId.Value].UpdatedAtUtc.UtcDateTime)),
                        ratePeriodsByStaff[row.StaffMemberId.Value].ToList(),
                        employmentPeriodsByStaff[row.StaffMemberId.Value].ToList()),
                    OpeningBaseAccrualAmount = StaffSalaryTimeline.CalculateBaseAccrual(
                        MonthPeriod.Normalize(businessDateProvider?.ToBusinessDate(row.StaffCreatedAtUtc!.Value)
                            ?? DateOnly.FromDateTime(row.StaffCreatedAtUtc.Value.UtcDateTime)),
                        monthFrom.AddMonths(-1),
                        row.Amount,
                        MonthPeriod.Normalize(businessDateProvider?.ToBusinessDate(row.StaffCreatedAtUtc.Value)
                            ?? DateOnly.FromDateTime(row.StaffCreatedAtUtc.Value.UtcDateTime)),
                        staffStateByMember[row.StaffMemberId.Value].IsArchived,
                        MonthPeriod.Normalize(businessDateProvider?.ToBusinessDate(staffStateByMember[row.StaffMemberId.Value].UpdatedAtUtc)
                            ?? DateOnly.FromDateTime(staffStateByMember[row.StaffMemberId.Value].UpdatedAtUtc.UtcDateTime)),
                        ratePeriodsByStaff[row.StaffMemberId.Value].ToList(),
                        employmentPeriodsByStaff[row.StaffMemberId.Value].ToList())
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
            rows.Where(row => row.Category == SupplierFundCategory)
                .Select(row => new ExpenseWorksheetSupplierFundData(
                    row.SupplierId!.Value,
                    row.TypeId!.Value,
                    row.StaffMemberId!.Value,
                    row.CounterpartyName!,
                    row.Amount))
                .ToList(),
            new FinanceAvailableBalanceData(
                rows.Sum(row => row.IncomeTotal),
                rows.Sum(row => row.BankDepositTotal),
                rows.Sum(row => row.CashExpenseTotal),
                rows.Sum(row => row.BankExpenseTotal)))
        {
            EpisodicExpenses = rows.Where(row => row.Category == EpisodicExpenseCategory)
                .Select(row => new ExpenseWorksheetEpisodicExpenseData(
                    row.CounterpartyName,
                    row.TypeId!.Value,
                    row.TypeName!,
                    row.TypeCode,
                    row.Amount))
                .ToList(),
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
            SupplierStartingBalances = rows.Where(row => row.Category == SupplierStartingBalanceCategory)
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
                .ToList(),
            SalaryAccrualMonthTo = salaryAccrualMonthTo >= monthFrom ? salaryAccrualMonthTo : null
        };
    }

    private sealed record StaffState(Guid StaffMemberId, bool IsArchived, DateTimeOffset UpdatedAtUtc);

}
