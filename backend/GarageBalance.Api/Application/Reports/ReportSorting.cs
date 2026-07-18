namespace GarageBalance.Api.Application.Reports;

public enum ReportSortKind
{
    Consolidated,
    Garages,
    Expense,
    Income,
    CashPayments,
    BankDeposits,
    Fees,
    FundChanges
}

public readonly record struct ReportSort(string Field, bool Descending);

public static class ReportSorting
{
    private static readonly IReadOnlyDictionary<ReportSortKind, ReportSortDefinition> Definitions =
        new Dictionary<ReportSortKind, ReportSortDefinition>
        {
            [ReportSortKind.Consolidated] = new("accountingMonth", true,
                ["accountingMonth", "incomeTotal", "expenseTotal", "accrualTotal", "balance", "debt", "operationCount", "accrualCount", "meterReadingCount"]),
            [ReportSortKind.Garages] = new("accountingMonth", true,
                ["accountingMonth", "garageNumber", "ownerName", "incomeTypeName", "accrualAmount", "incomeAmount", "difference"]),
            [ReportSortKind.Expense] = new("date", true,
                ["date", "accountingMonth", "supplierName", "expenseTypeName", "accrualAmount", "expenseAmount", "difference", "documentNumber"]),
            [ReportSortKind.Income] = new("date", true,
                ["date", "accountingMonth", "garageNumber", "ownerName", "incomeTypeName", "accrualAmount", "incomeAmount", "debt", "documentNumber"]),
            [ReportSortKind.CashPayments] = new("date", true,
                ["date", "amount", "hasReceipt", "purpose", "supplierName", "expenseTypeName", "documentNumber"]),
            [ReportSortKind.BankDeposits] = new("date", true,
                ["date", "amount", "fundName", "comment"]),
            [ReportSortKind.Fees] = new("garageNumber", false,
                ["garageNumber", "ownerName", "feeName", "accrued", "paid", "lastPaymentDate", "debt"]),
            [ReportSortKind.FundChanges] = new("date", true,
                ["date", "fundName", "changeName", "amount", "balanceBefore", "balanceAfter", "actorDisplayName", "reason"])
        };

    public static bool TryNormalize(
        ReportSortKind kind,
        string? sortBy,
        string? sortDirection,
        out ReportSort sort,
        out string? errorCode,
        out string? errorMessage)
    {
        var definition = Definitions[kind];
        var hasSortBy = !string.IsNullOrWhiteSpace(sortBy);
        var field = hasSortBy ? sortBy!.Trim() : definition.DefaultField;
        if (!definition.AllowedFields.Contains(field, StringComparer.Ordinal))
        {
            sort = default;
            errorCode = "report_sort_field_invalid";
            errorMessage = $"Поле сортировки \"{field}\" недоступно для выбранного отчета.";
            return false;
        }

        var hasDirection = !string.IsNullOrWhiteSpace(sortDirection);
        var direction = hasDirection
            ? sortDirection!.Trim()
            : hasSortBy
                ? "asc"
                : definition.DefaultDescending ? "desc" : "asc";
        if (direction is not ("asc" or "desc"))
        {
            sort = default;
            errorCode = "report_sort_direction_invalid";
            errorMessage = "Направление сортировки должно быть asc или desc.";
            return false;
        }

        sort = new ReportSort(field, direction == "desc");
        errorCode = null;
        errorMessage = null;
        return true;
    }

    private sealed record ReportSortDefinition(
        string DefaultField,
        bool DefaultDescending,
        IReadOnlyList<string> AllowedFields);
}
