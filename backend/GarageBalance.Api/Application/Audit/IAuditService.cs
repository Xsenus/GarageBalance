namespace GarageBalance.Api.Application.Audit;

public interface IAuditService
{
    Task<IReadOnlyList<AuditEventDto>> GetEventsAsync(AuditEventListRequest request, CancellationToken cancellationToken);
}
