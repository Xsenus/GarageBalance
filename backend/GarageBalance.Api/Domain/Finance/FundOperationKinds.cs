namespace GarageBalance.Api.Domain.Finance;

public static class FundOperationKinds
{
    public const string Deposit = "deposit";
    public const string Withdraw = "withdraw";

    public static bool IsSupported(string? value)
    {
        return string.Equals(value, Deposit, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, Withdraw, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }
}
