using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Application.Integrations;

public sealed record ReceiptPrintingActionRequest(
    [Required, MaxLength(40)] string Action,
    [ActionComment, MaxLength(1000)] string? Reason);

public sealed record ReceiptPrintingActionDto(
    Guid AuditEventId,
    Guid FinancialOperationId,
    string Action,
    string Status,
    string StatusMessage,
    string? DocumentNumber,
    bool IsCopy,
    string? CopyMark,
    DateTimeOffset RegisteredAtUtc,
    Guid? ReceiptBatchId = null,
    decimal TotalAmount = 0,
    int LineCount = 1);

public sealed record ReceiptPrintingResult<T>(bool Succeeded, T? Value, string? ErrorCode, string? ErrorMessage)
{
    public static ReceiptPrintingResult<T> Success(T value) => new(true, value, null, null);

    public static ReceiptPrintingResult<T> Failure(string errorCode, string errorMessage) => new(false, default, errorCode, errorMessage);
}
