namespace GarageBalance.Api.Application.Finance;

internal static class CashExpenseClassification
{
    public const string AdvancePaymentExpenseTypeName = "Авансовые выплаты";
    public const string NoReceiptPaymentExpenseTypeName = "Выплата без чека";

    public static readonly string[] TypeCodes =
    [
        "advance",
        "advance_payment",
        "advance_payments",
        "cash_advance",
        "no_receipt",
        "without_receipt",
        "no_check",
        "without_check",
        "cash_no_receipt"
    ];

    public static readonly string[] TypeNames =
    [
        AdvancePaymentExpenseTypeName,
        NoReceiptPaymentExpenseTypeName
    ];
}
