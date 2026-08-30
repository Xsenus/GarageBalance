using GarageBalance.Api.Domain.Import;

namespace GarageBalance.Api.Application.Import;

public interface IImportQuarantineRepository
{
    Task<IReadOnlyList<AccessImportQuarantineListItemData>> GetOpenItemsAsync(
        Guid? accessImportRunId,
        int limit,
        CancellationToken cancellationToken);

    Task<AccessImportQuarantineItem?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);
    void Add(AccessImportQuarantineItem item);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record AccessImportQuarantineListItemData(
    Guid Id,
    Guid? AccessImportRunId,
    string SourceSystem,
    string EntityType,
    string? ExternalId,
    string RowHash,
    string ReasonCode,
    string ReasonMessage,
    string Severity,
    string Status,
    DateTimeOffset CreatedAtUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? ResolvedAtUtc,
    Guid? ResolvedByUserId,
    string? ResolutionComment);
