namespace GarageBalance.Api.Application.Finance;

public interface IExpenseWorksheetQuery
{
    Task<ExpenseWorksheetData> GetAsync(
        DateOnly monthFrom,
        DateOnly monthTo,
        string[] cashExpenseTypeCodes,
        string[] cashExpenseTypeNames,
        CancellationToken cancellationToken);

    Task<ExpenseWorksheetSupplierBreakdownData> GetSupplierBreakdownAsync(
        Guid supplierId,
        Guid expenseTypeId,
        DateOnly monthFrom,
        DateOnly monthTo,
        int offset,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record ExpenseWorksheetSupplierBreakdownData(
    IReadOnlyList<ExpenseWorksheetSupplierBreakdownEntryData> Items,
    int TotalCount,
    decimal AccrualTotal,
    decimal ExpenseTotal);

public sealed record ExpenseWorksheetSupplierBreakdownEntryData(
    Guid Id,
    string EntryKind,
    DateOnly AccountingMonth,
    DateOnly? OperationDate,
    decimal Amount,
    string? DocumentNumber,
    string? Comment,
    string? Source,
    DateTimeOffset CreatedAtUtc);

public sealed record ExpenseWorksheetData(
    IReadOnlyList<ExpenseWorksheetSupplierData> SupplierAccruals,
    IReadOnlyList<ExpenseWorksheetSupplierData> SupplierExpenses,
    IReadOnlyList<ExpenseWorksheetStaffData> StaffMembers,
    IReadOnlyList<ExpenseWorksheetStaffExpenseData> StaffExpenses,
    IReadOnlyList<ExpenseWorksheetIncomeData> Incomes,
    IReadOnlyList<ExpenseWorksheetSupplierFundData> SupplierFunds,
    FinanceAvailableBalanceData AvailableBalance)
{
    public IReadOnlyList<ExpenseWorksheetEpisodicExpenseData> EpisodicExpenses { get; init; } = [];

    public IReadOnlyList<ExpenseWorksheetSupplierData> SupplierOpeningAccruals { get; init; } = [];

    public IReadOnlyList<ExpenseWorksheetSupplierData> SupplierOpeningExpenses { get; init; } = [];

    public IReadOnlyList<ExpenseWorksheetSupplierData> SupplierStartingBalances { get; init; } = [];

    public IReadOnlyList<ExpenseWorksheetStaffExpenseData> StaffOpeningExpenses { get; init; } = [];

    public IReadOnlyList<ExpenseWorksheetIncomeData> OpeningIncomes { get; init; } = [];

    public IReadOnlyList<ExpenseWorksheetStaffAdjustmentData> StaffBonuses { get; init; } = [];

    public IReadOnlyList<ExpenseWorksheetStaffAdjustmentData> StaffPenalties { get; init; } = [];

    public IReadOnlyList<ExpenseWorksheetStaffAdjustmentData> StaffOpeningBonuses { get; init; } = [];

    public IReadOnlyList<ExpenseWorksheetStaffAdjustmentData> StaffOpeningPenalties { get; init; } = [];

    public DateOnly? SalaryAccrualMonthTo { get; init; }
}

public sealed record ExpenseWorksheetSupplierData(
    Guid SupplierId,
    string SupplierName,
    Guid ExpenseTypeId,
    string ExpenseTypeName,
    string? ExpenseTypeCode,
    decimal Amount);

public sealed record ExpenseWorksheetEpisodicExpenseData(
    string? CounterpartyName,
    Guid ExpenseTypeId,
    string ExpenseTypeName,
    string? ExpenseTypeCode,
    decimal Amount);

public sealed record ExpenseWorksheetStaffData(
    Guid StaffMemberId,
    string FullName,
    string ExpenseTypeName,
    decimal Rate)
{
    public Guid ExpenseTypeId { get; init; }

    public string? ExpenseTypeCode { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record ExpenseWorksheetStaffExpenseData(Guid StaffMemberId, decimal Amount)
{
    public Guid ExpenseTypeId { get; init; }

    public DateOnly? FirstAccountingMonth { get; init; }
}

public sealed record ExpenseWorksheetStaffAdjustmentData(Guid StaffMemberId, decimal Amount);

public sealed record ExpenseWorksheetIncomeData(string IncomeTypeName, string? IncomeTypeCode, decimal Amount);

public sealed record ExpenseWorksheetSupplierFundData(
    Guid SupplierId,
    Guid ExpenseTypeId,
    Guid ExpenseFundId,
    string ExpenseFundName,
    decimal AvailableBalance);
