namespace GarageBalance.Api.Domain.Finance;

public static class AnnualAccrualPolicy
{
    private static readonly HashSet<string> AnnualIncomeTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "membership",
        "target",
        "outdoor_lighting"
    };

    public static bool IsAnnualIncomeType(string? incomeTypeCode)
    {
        return !string.IsNullOrWhiteSpace(incomeTypeCode) &&
            AnnualIncomeTypeCodes.Contains(incomeTypeCode.Trim());
    }

    public static int? ResolveAccountingYear(string? incomeTypeCode, DateOnly accountingMonth)
    {
        return IsAnnualIncomeType(incomeTypeCode) ? accountingMonth.Year : null;
    }

    public static bool IsFullyPaid(decimal accruedAmount, decimal allocatedAmount)
    {
        return accruedAmount > 0m && allocatedAmount >= accruedAmount;
    }
}
