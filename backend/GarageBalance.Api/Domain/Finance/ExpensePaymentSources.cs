namespace GarageBalance.Api.Domain.Finance;

public static class ExpensePaymentSources
{
    public const string Bank = "bank";
    public const string Cash = "cash";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Bank,
        Cash
    };
}
