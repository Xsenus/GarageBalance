namespace GarageBalance.Api.Domain.Finance;

public static class ExpensePaymentTypes
{
    public const string WithReceipt = "with_receipt";
    public const string WithoutReceipt = "without_receipt";

    public static bool IsSupported(string? value) =>
        value is WithReceipt or WithoutReceipt;
}
