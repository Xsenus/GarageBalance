namespace GarageBalance.Api.Application.Finance;

public interface IExpenseWorksheetQuery
{
    Task<ExpenseWorksheetData> GetAsync(
        DateOnly accountingMonth,
        string[] cashExpenseTypeCodes,
        string[] cashExpenseTypeNames,
        CancellationToken cancellationToken);
}

public sealed record ExpenseWorksheetData(
    IReadOnlyList<ExpenseWorksheetSupplierData> SupplierAccruals,
    IReadOnlyList<ExpenseWorksheetSupplierData> SupplierExpenses,
    IReadOnlyList<ExpenseWorksheetStaffData> StaffMembers,
    IReadOnlyList<ExpenseWorksheetStaffExpenseData> StaffExpenses,
    IReadOnlyList<ExpenseWorksheetIncomeData> Incomes,
    FinanceAvailableBalanceData AvailableBalance)
{
    public IReadOnlyList<ExpenseWorksheetSupplierData> SupplierOpeningAccruals { get; init; } = [];

    public IReadOnlyList<ExpenseWorksheetSupplierData> SupplierOpeningExpenses { get; init; } = [];

    public IReadOnlyList<ExpenseWorksheetStaffExpenseData> StaffOpeningExpenses { get; init; } = [];

    public IReadOnlyList<ExpenseWorksheetIncomeData> OpeningIncomes { get; init; } = [];
}

public sealed record ExpenseWorksheetSupplierData(
    Guid SupplierId,
    string SupplierName,
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

public sealed record ExpenseWorksheetIncomeData(string IncomeTypeName, string? IncomeTypeCode, decimal Amount);
