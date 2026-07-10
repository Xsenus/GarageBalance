using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Integrations;

public sealed record ReceiptPrintingActionRequest(
    [Required, MaxLength(40)] string Action,
    [MaxLength(1000)] string? Reason);

public sealed record ReceiptPrintingActionDto(
    Guid AuditEventId,
    Guid FinancialOperationId,
    string Action,
    string Status,
    string StatusMessage,
    string? DocumentNumber,
    bool IsCopy,
    string? CopyMark,
    DateTimeOffset RegisteredAtUtc);

public sealed record ReceiptPrintingResult<T>(bool Succeeded, T? Value, string? ErrorCode, string? ErrorMessage)
{
    public static ReceiptPrintingResult<T> Success(T value) => new(true, value, null, null);

    public static ReceiptPrintingResult<T> Failure(string errorCode, string errorMessage) => new(false, default, errorCode, errorMessage);
}
