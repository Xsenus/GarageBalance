using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Audit;

public sealed class AuditService(GarageBalanceDbContext dbContext) : IAuditService
{
    private const int ListLimit = 100;

    public async Task<IReadOnlyList<AuditEventDto>> GetEventsAsync(AuditEventListRequest request, CancellationToken cancellationToken)
    {
        var query = dbContext.AuditEvents.AsNoTracking();

        if (string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            return await GetEventsForSqliteAsync(query, request, cancellationToken);
        }

        if (request.DateFrom is not null)
        {
            query = query.Where(auditEvent => auditEvent.CreatedAtUtc >= request.DateFrom.Value);
        }

        if (request.DateTo is not null)
        {
            query = query.Where(auditEvent => auditEvent.CreatedAtUtc <= request.DateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var action = request.Action.Trim();
            query = query.Where(auditEvent => auditEvent.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(auditEvent =>
                auditEvent.Action.ToLower().Contains(search) ||
                auditEvent.EntityType.ToLower().Contains(search) ||
                (auditEvent.EntityId != null && auditEvent.EntityId.ToLower().Contains(search)) ||
                auditEvent.Summary.ToLower().Contains(search));
        }

        return await query
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .Take(ListLimit)
            .Select(auditEvent => ToDto(auditEvent))
            .ToListAsync(cancellationToken);
    }

    private static AuditEventDto ToDto(AuditEvent auditEvent)
    {
        return new AuditEventDto(
            auditEvent.Id,
            auditEvent.CreatedAtUtc,
            auditEvent.ActorUserId,
            auditEvent.Action,
            auditEvent.EntityType,
            AuditTextMasker.Mask(auditEvent.EntityId),
            AuditTextMasker.Mask(auditEvent.Summary) ?? string.Empty);
    }

    private static async Task<IReadOnlyList<AuditEventDto>> GetEventsForSqliteAsync(
        IQueryable<AuditEvent> query,
        AuditEventListRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var action = request.Action.Trim();
            query = query.Where(auditEvent => auditEvent.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(auditEvent =>
                auditEvent.Action.ToLower().Contains(search) ||
                auditEvent.EntityType.ToLower().Contains(search) ||
                (auditEvent.EntityId != null && auditEvent.EntityId.ToLower().Contains(search)) ||
                auditEvent.Summary.ToLower().Contains(search));
        }

        var events = await query.ToListAsync(cancellationToken);

        if (request.DateFrom is not null)
        {
            events = events.Where(auditEvent => auditEvent.CreatedAtUtc >= request.DateFrom.Value).ToList();
        }

        if (request.DateTo is not null)
        {
            events = events.Where(auditEvent => auditEvent.CreatedAtUtc <= request.DateTo.Value).ToList();
        }

        return events
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .Take(ListLimit)
            .Select(ToDto)
            .ToList();
    }
}
