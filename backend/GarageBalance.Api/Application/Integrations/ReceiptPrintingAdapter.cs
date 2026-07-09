namespace GarageBalance.Api.Application.Integrations;

public interface IReceiptPrintingAdapter
{
    Task<ReceiptPrintingAdapterResult> ProcessAsync(ReceiptPrintingAdapterRequest request, CancellationToken cancellationToken);
}

public sealed record ReceiptPrintingAdapterRequest(
    string Action,
    Guid FinancialOperationId,
    string DocumentNumber,
    decimal Amount,
    DateOnly OperationDate,
    DateOnly AccountingMonth,
    string? GarageNumber,
    string? OwnerName,
    string? IncomeTypeName,
    string? Reason);

public sealed record ReceiptPrintingAdapterResult(
    string Status,
    string StatusMessage,
    string? DeviceResponseCode = null,
    string? ExternalReceiptId = null)
{
    public static ReceiptPrintingAdapterResult Pending(string statusMessage) => new("pending_adapter", statusMessage);

    public static ReceiptPrintingAdapterResult Printed(string statusMessage, string? deviceResponseCode = null, string? externalReceiptId = null) =>
        new("printed", statusMessage, deviceResponseCode, externalReceiptId);

    public static ReceiptPrintingAdapterResult Failed(string status, string statusMessage, string? deviceResponseCode = null) =>
        new(status, statusMessage, deviceResponseCode);
}

public sealed class DisabledReceiptPrintingAdapter : IReceiptPrintingAdapter
{
    public Task<ReceiptPrintingAdapterResult> ProcessAsync(ReceiptPrintingAdapterRequest request, CancellationToken cancellationToken)
    {
        var actionLabel = request.Action switch
        {
            ReceiptPrintingActions.Cancel => "Отмена печати",
            ReceiptPrintingActions.Reprint => "Повторная печать",
            _ => "Печать квитанции"
        };

        return Task.FromResult(ReceiptPrintingAdapterResult.Pending(
            $"{actionLabel} зарегистрирована в истории. Фактическая печать будет доступна после подключения адаптера."));
    }
}

public static class ReceiptPrintingActions
{
    public const string Print = "print";
    public const string Cancel = "cancel";
    public const string Reprint = "reprint";
}
