using GarageBalance.Api.Domain.Audit;

namespace GarageBalance.Api.Application.Audit;

public interface IAuditEventStore
{
    void Add(AuditEvent auditEvent);
}
