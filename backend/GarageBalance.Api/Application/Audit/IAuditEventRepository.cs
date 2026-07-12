using GarageBalance.Api.Domain.Audit;

namespace GarageBalance.Api.Application.Audit;

public interface IAuditEventRepository
{
    Task<IReadOnlyList<AuditEvent>> GetEventsAsync(
        AuditEventListRequest request,
        int limit,
        CancellationToken cancellationToken);

    Task<AuditEventPageData> GetEventsPageAsync(
        AuditEventListRequest request,
        int offset,
        int limit,
        CancellationToken cancellationToken);

    Task<AuditEvent?> FindEventAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record AuditEventPageData(
    IReadOnlyList<AuditEvent> Items,
    int TotalCount);
