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

    public static bool IsAnnual(int? periodicityMonths, string? incomeTypeCode)
    {
        return periodicityMonths.HasValue
            ? periodicityMonths.Value >= 12
            : IsAnnualIncomeType(incomeTypeCode);
    }

    public static int? ResolveAccountingYear(
        string? incomeTypeCode,
        DateOnly accountingMonth,
        int? periodicityMonths = null)
    {
        return IsAnnual(periodicityMonths, incomeTypeCode) ? accountingMonth.Year : null;
    }

    public static bool IsFullyPaid(decimal accruedAmount, decimal allocatedAmount)
    {
        return accruedAmount > 0m && allocatedAmount >= accruedAmount;
    }

    // A legacy regular obligation can block a second annual charge without being
    // reclassified or recalculated. Explicit nonannual history remains month-scoped.
    // Keep the equivalent EF query expressions in EfAccrualRepository in sync.
    public static int? ResolveHistoricalObligationYear(int? accountingYear, bool? isAnnualAccrual, DateOnly accountingMonth)
    {
        return accountingYear ?? (isAnnualAccrual != false ? accountingMonth.Year : null);
    }

    public static DateOnly ResolveAccountingMonth(DateOnly scheduledMonth, DateOnly garageStartMonth)
    {
        var scheduled = new DateOnly(scheduledMonth.Year, scheduledMonth.Month, 1);
        var start = new DateOnly(garageStartMonth.Year, garageStartMonth.Month, 1);
        return start > scheduled ? start : scheduled;
    }
}
