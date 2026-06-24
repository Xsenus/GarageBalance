using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace GarageBalance.Api.Application.Audit;

public sealed class AuditService(GarageBalanceDbContext dbContext) : IAuditService
{
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;

    public async Task<IReadOnlyList<AuditEventDto>> GetEventsAsync(AuditEventListRequest request, CancellationToken cancellationToken)
    {
        var limit = NormalizeLimit(request.Limit);
        var query = dbContext.AuditEvents.AsNoTracking();

        if (string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            return await GetEventsForSqliteAsync(query, request, limit, cancellationToken);
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
            .Take(limit)
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
        int limit,
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
            .Take(limit)
            .Select(ToDto)
            .ToList();
    }

    public async Task<AuditEventExportDto> ExportEventsCsvAsync(AuditEventListRequest request, CancellationToken cancellationToken)
    {
        var events = await GetEventsAsync(request, cancellationToken);
        var csv = BuildCsv(events);
        var fileName = $"audit-events-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv";

        return new AuditEventExportDto(fileName, "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(csv));
    }

    private static string BuildCsv(IReadOnlyList<AuditEventDto> events)
    {
        var builder = new StringBuilder();
        builder.AppendLine("createdAtUtc,actorUserId,action,entityType,entityId,summary");

        foreach (var auditEvent in events)
        {
            builder
                .Append(EscapeCsv(auditEvent.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(EscapeCsv(auditEvent.ActorUserId?.ToString())).Append(',')
                .Append(EscapeCsv(auditEvent.Action)).Append(',')
                .Append(EscapeCsv(auditEvent.EntityType)).Append(',')
                .Append(EscapeCsv(auditEvent.EntityId)).Append(',')
                .Append(EscapeCsv(auditEvent.Summary))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.Any(character => character is ',' or '"' or '\r' or '\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static int NormalizeLimit(int? limit)
    {
        if (limit is null or <= 0)
        {
            return DefaultListLimit;
        }

        return Math.Min(limit.Value, MaxListLimit);
    }
}
