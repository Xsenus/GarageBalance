namespace GarageBalance.Api.Domain.Finance;

public sealed class CashBankBalanceOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Account { get; set; } = string.Empty;
    public string OperationKind { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public DateOnly OperationDate { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public static class CashBankAccounts
{
    public const string Cash = "cash";
    public const string Bank = "bank";
}

public static class CashBankBalanceOperationKinds
{
    public const string OpeningBalance = "opening_balance";
    public const string Adjustment = "adjustment";
}

public static class CashBankBalanceDirections
{
    public const string Increase = "increase";
    public const string Decrease = "decrease";
}
