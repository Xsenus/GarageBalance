namespace GarageBalance.Api.Application.Finance;

public interface IFinancialJournalQuery
{
    Task<FinancePagedResult<FinancialJournalEntryDto>> GetPageAsync(
        FinancialJournalRequest request,
        CancellationToken cancellationToken);
}

public sealed record FinancialJournalRequest(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? EntityType,
    string? Counterparty,
    string? Status,
    string? Document,
    int? Offset,
    int? Limit);

public sealed record FinancialJournalEntryDto(
    Guid Id,
    string EntityType,
    string OperationType,
    DateOnly OperationDate,
    DateOnly? AccountingMonth,
    decimal Amount,
    string Counterparty,
    string Category,
    string? DocumentNumber,
    string? Comment,
    string Source,
    bool IsCanceled,
    DateTimeOffset CreatedAtUtc,
    Guid? Version,
    bool CanEdit,
    bool CanCancel,
    bool CanRestore,
    string? ProtectionReason,
    string? CorrectionHint);
