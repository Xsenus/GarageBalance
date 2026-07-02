namespace GarageBalance.Api.Application.Audit;

public interface IAuditService
{
    Task<IReadOnlyList<AuditEventDto>> GetEventsAsync(AuditEventListRequest request, CancellationToken cancellationToken);

    Task<AuditEventDto?> GetEventAsync(Guid id, CancellationToken cancellationToken);

    Task<AuditEventExportDto> ExportEventsCsvAsync(AuditEventListRequest request, CancellationToken cancellationToken);
}
