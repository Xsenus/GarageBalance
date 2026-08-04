namespace GarageBalance.Api.Application.Integrations;

public interface IReceiptPrintingAdapter
{
    IntegrationAdapterAvailability Availability => IntegrationAdapterAvailability.Disabled("Адаптер печати не подключен.");

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
    string? Reason,
    bool IsCopy,
    string? CopyMark,
    Guid? ReceiptBatchId = null,
    IReadOnlyList<ReceiptPrintingLineItem>? Lines = null);

public sealed record ReceiptPrintingLineItem(
    Guid FinancialOperationId,
    string IncomeTypeName,
    DateOnly AccountingMonth,
    decimal Amount,
    IReadOnlyList<ReceiptPrintingAllocationItem> Allocations);

public sealed record ReceiptPrintingAllocationItem(
    Guid AccrualId,
    DateOnly AccountingMonth,
    string IncomeTypeName,
    decimal Amount);

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
            ReceiptPrintingActions.Reprint => "Повторная печать копии",
            _ => "Печать квитанции"
        };

        return Task.FromResult(ReceiptPrintingAdapterResult.Pending(
            request.IsCopy
                ? $"{actionLabel} с отметкой {request.CopyMark} зарегистрирована в истории. Фактическая печать будет доступна после подключения адаптера."
                : $"{actionLabel} зарегистрирована в истории. Фактическая печать будет доступна после подключения адаптера."));
    }
}

public static class ReceiptPrintingActions
{
    public const string Print = "print";
    public const string Cancel = "cancel";
    public const string Reprint = "reprint";
}

public sealed record IntegrationAdapterAvailability(bool IsAvailable, string Status, string Message)
{
    public static IntegrationAdapterAvailability Ready(string message) => new(true, "ready", message);

    public static IntegrationAdapterAvailability Disabled(string message) => new(false, "adapter_disabled", message);

    public static IntegrationAdapterAvailability Invalid(string message) => new(false, "adapter_not_configured", message);
}
