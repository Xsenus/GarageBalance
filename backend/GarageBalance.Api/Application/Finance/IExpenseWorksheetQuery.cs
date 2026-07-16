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
    FinanceAvailableBalanceData AvailableBalance);

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
    string DepartmentName,
    decimal Rate);

public sealed record ExpenseWorksheetStaffExpenseData(Guid StaffMemberId, decimal Amount);

public sealed record ExpenseWorksheetIncomeData(string IncomeTypeName, string? IncomeTypeCode, decimal Amount);
