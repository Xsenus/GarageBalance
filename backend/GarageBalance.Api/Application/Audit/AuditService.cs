using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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

        if (!string.IsNullOrWhiteSpace(request.Section))
        {
            var sectionPrefix = request.Section.Trim().ToLowerInvariant() + ".";
            query = query.Where(auditEvent => auditEvent.Action.ToLower().StartsWith(sectionPrefix));
        }

        if (!string.IsNullOrWhiteSpace(request.ActionKind))
        {
            query = ApplyActionKindFilter(query, request.ActionKind);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            var entityType = request.EntityType.Trim();
            query = query.Where(auditEvent => auditEvent.EntityType == entityType);
        }

        if (request.ActorUserId is not null)
        {
            query = query.Where(auditEvent => auditEvent.ActorUserId == request.ActorUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.QuickFilter))
        {
            query = ApplyQuickFilter(query, request.QuickFilter);
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

    public async Task<AuditEventPageDto> GetEventsPageAsync(AuditEventListRequest request, CancellationToken cancellationToken)
    {
        var limit = NormalizeLimit(request.Limit);
        var offset = NormalizeOffset(request.Offset);
        var query = dbContext.AuditEvents.AsNoTracking();

        if (string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            return await GetEventsPageForSqliteAsync(query, request, offset, limit, cancellationToken);
        }

        if (request.DateFrom is not null)
        {
            query = query.Where(auditEvent => auditEvent.CreatedAtUtc >= request.DateFrom.Value);
        }

        if (request.DateTo is not null)
        {
            query = query.Where(auditEvent => auditEvent.CreatedAtUtc <= request.DateTo.Value);
        }

        query = ApplyNonDateFilters(query, request);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .Skip(offset)
            .Take(limit)
            .Select(auditEvent => ToDto(auditEvent))
            .ToListAsync(cancellationToken);

        return new AuditEventPageDto(items, totalCount, offset, limit);
    }

    private static AuditEventDto ToDto(AuditEvent auditEvent)
    {
        var maskedSummary = AuditTextMasker.Mask(auditEvent.Summary) ?? string.Empty;
        var beforeAfter = ExtractBeforeAfter(maskedSummary);

        return new AuditEventDto(
            auditEvent.Id,
            auditEvent.CreatedAtUtc,
            auditEvent.ActorUserId,
            auditEvent.Action,
            auditEvent.EntityType,
            AuditTextMasker.Mask(auditEvent.EntityId),
            maskedSummary,
            GetSection(auditEvent.Action),
            GetActionKind(auditEvent.Action),
            ExtractFieldName(maskedSummary),
            beforeAfter.OldValue,
            beforeAfter.NewValue,
            ExtractReason(maskedSummary));
    }

    private static async Task<IReadOnlyList<AuditEventDto>> GetEventsForSqliteAsync(
        IQueryable<AuditEvent> query,
        AuditEventListRequest request,
        int limit,
        CancellationToken cancellationToken)
    {
        query = ApplyNonDateFilters(query, request);

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

    private static async Task<AuditEventPageDto> GetEventsPageForSqliteAsync(
        IQueryable<AuditEvent> query,
        AuditEventListRequest request,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        query = ApplyNonDateFilters(query, request);
        var events = await query.ToListAsync(cancellationToken);

        if (request.DateFrom is not null)
        {
            events = events.Where(auditEvent => auditEvent.CreatedAtUtc >= request.DateFrom.Value).ToList();
        }

        if (request.DateTo is not null)
        {
            events = events.Where(auditEvent => auditEvent.CreatedAtUtc <= request.DateTo.Value).ToList();
        }

        var totalCount = events.Count;
        var items = events
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .Skip(offset)
            .Take(limit)
            .Select(ToDto)
            .ToList();

        return new AuditEventPageDto(items, totalCount, offset, limit);
    }

    public async Task<AuditEventExportDto> ExportEventsCsvAsync(AuditEventListRequest request, CancellationToken cancellationToken)
    {
        var events = await GetEventsAsync(request, cancellationToken);
        var csv = BuildCsv(events);
        var fileName = $"audit-events-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv";

        return new AuditEventExportDto(fileName, "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(csv));
    }

    public async Task<AuditEventDto?> GetEventAsync(Guid id, CancellationToken cancellationToken)
    {
        var auditEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(auditEvent => auditEvent.Id == id, cancellationToken);

        return auditEvent is null ? null : ToDto(auditEvent);
    }

    private static string BuildCsv(IReadOnlyList<AuditEventDto> events)
    {
        var builder = new StringBuilder();
        builder.AppendLine("createdAtUtc,actorUserId,section,actionKind,action,entityType,entityId,fieldName,oldValue,newValue,reason,summary");

        foreach (var auditEvent in events)
        {
            builder
                .Append(EscapeCsv(auditEvent.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(EscapeCsv(auditEvent.ActorUserId?.ToString())).Append(',')
                .Append(EscapeCsv(auditEvent.Section)).Append(',')
                .Append(EscapeCsv(auditEvent.ActionKind)).Append(',')
                .Append(EscapeCsv(auditEvent.Action)).Append(',')
                .Append(EscapeCsv(auditEvent.EntityType)).Append(',')
                .Append(EscapeCsv(auditEvent.EntityId)).Append(',')
                .Append(EscapeCsv(auditEvent.FieldName)).Append(',')
                .Append(EscapeCsv(auditEvent.OldValue)).Append(',')
                .Append(EscapeCsv(auditEvent.NewValue)).Append(',')
                .Append(EscapeCsv(auditEvent.Reason)).Append(',')
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

    private static int NormalizeOffset(int? offset)
    {
        return offset is null or < 0 ? 0 : offset.Value;
    }

    private static IQueryable<AuditEvent> ApplyNonDateFilters(IQueryable<AuditEvent> query, AuditEventListRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var action = request.Action.Trim();
            query = query.Where(auditEvent => auditEvent.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(request.Section))
        {
            var sectionPrefix = request.Section.Trim().ToLowerInvariant() + ".";
            query = query.Where(auditEvent => auditEvent.Action.ToLower().StartsWith(sectionPrefix));
        }

        if (!string.IsNullOrWhiteSpace(request.ActionKind))
        {
            query = ApplyActionKindFilter(query, request.ActionKind);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            var entityType = request.EntityType.Trim();
            query = query.Where(auditEvent => auditEvent.EntityType == entityType);
        }

        if (request.ActorUserId is not null)
        {
            query = query.Where(auditEvent => auditEvent.ActorUserId == request.ActorUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.QuickFilter))
        {
            query = ApplyQuickFilter(query, request.QuickFilter);
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

        return query;
    }

    private static IReadOnlyList<string> GetActionKindNeedles(string actionKind)
    {
        return actionKind.Trim().ToLowerInvariant() switch
        {
            "create" => ["_created"],
            "update" => ["_updated", "password_changed"],
            "archive" => ["_archived"],
            "restore" => ["_restored"],
            "cancel" => ["_canceled", "_cancelled"],
            "delete" => ["_deleted"],
            "login" => ["login_"],
            "fail" => ["_failed", "_rate_limited", "_inactive"],
            "generate" => ["_generated"],
            "import" => ["import."],
            "export" => ["_exported", ".export"],
            _ => []
        };
    }

    private static string GetSection(string action)
    {
        var separatorIndex = action.IndexOf('.', StringComparison.Ordinal);
        return separatorIndex > 0 ? action[..separatorIndex] : "system";
    }

    private static string GetActionKind(string action)
    {
        var normalized = action.ToLowerInvariant();

        if (normalized.Contains("_created", StringComparison.Ordinal))
        {
            return "create";
        }

        if (normalized.Contains("_updated", StringComparison.Ordinal) || normalized.Contains("password_changed", StringComparison.Ordinal))
        {
            return "update";
        }

        if (normalized.Contains("_archived", StringComparison.Ordinal))
        {
            return "archive";
        }

        if (normalized.Contains("_restored", StringComparison.Ordinal))
        {
            return "restore";
        }

        if (normalized.Contains("_canceled", StringComparison.Ordinal) || normalized.Contains("_cancelled", StringComparison.Ordinal))
        {
            return "cancel";
        }

        if (normalized.Contains("_deleted", StringComparison.Ordinal))
        {
            return "delete";
        }

        if (normalized.Contains("_failed", StringComparison.Ordinal) || normalized.Contains("_rate_limited", StringComparison.Ordinal) || normalized.Contains("_inactive", StringComparison.Ordinal))
        {
            return "fail";
        }

        if (normalized.Contains("_generated", StringComparison.Ordinal))
        {
            return "generate";
        }

        if (normalized.StartsWith("auth.login", StringComparison.Ordinal))
        {
            return "login";
        }

        if (normalized.StartsWith("import.", StringComparison.Ordinal))
        {
            return "import";
        }

        if (normalized.Contains("_exported", StringComparison.Ordinal) || normalized.Contains(".export", StringComparison.Ordinal))
        {
            return "export";
        }

        return "other";
    }

    private static string? ExtractFieldName(string summary)
    {
        var match = Regex.Match(summary, @"поле\s+(?<field>.+?):\s+было", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? NormalizeExtractedValue(match.Groups["field"].Value) : null;
    }

    private static (string? OldValue, string? NewValue) ExtractBeforeAfter(string summary)
    {
        var match = Regex.Match(summary, @"было\s+(?<old>.+?);?\s+стало\s+(?<new>.+?)(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
            ? (NormalizeExtractedValue(match.Groups["old"].Value), NormalizeExtractedValue(match.Groups["new"].Value))
            : (null, null);
    }

    private static string? ExtractReason(string summary)
    {
        var match = Regex.Match(summary, @"(?:Причина|Комментарий):\s*(?<reason>.+?)(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? NormalizeExtractedValue(match.Groups["reason"].Value) : null;
    }

    private static string? NormalizeExtractedValue(string value)
    {
        var normalized = value.Trim().TrimEnd(';', '.').Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static IQueryable<AuditEvent> ApplyActionKindFilter(IQueryable<AuditEvent> query, string actionKind)
    {
        var needles = GetActionKindNeedles(actionKind);
        return needles.Count switch
        {
            1 => query.Where(auditEvent => auditEvent.Action.ToLower().Contains(needles[0])),
            2 => query.Where(auditEvent => auditEvent.Action.ToLower().Contains(needles[0]) || auditEvent.Action.ToLower().Contains(needles[1])),
            3 => query.Where(auditEvent =>
                auditEvent.Action.ToLower().Contains(needles[0]) ||
                auditEvent.Action.ToLower().Contains(needles[1]) ||
                auditEvent.Action.ToLower().Contains(needles[2])),
            _ => query
        };
    }

    private static IQueryable<AuditEvent> ApplyQuickFilter(IQueryable<AuditEvent> query, string quickFilter)
    {
        return quickFilter.Trim().ToLowerInvariant() switch
        {
            "deletions" => query.Where(auditEvent =>
                auditEvent.Action.ToLower().Contains("_archived") ||
                auditEvent.Action.ToLower().Contains("_deleted") ||
                auditEvent.Action.ToLower().Contains("_canceled") ||
                auditEvent.Action.ToLower().Contains("_cancelled")),
            "restores" => query.Where(auditEvent => auditEvent.Action.ToLower().Contains("_restored")),
            "financial" => query.Where(auditEvent =>
                auditEvent.Action.ToLower().StartsWith("finance.") ||
                auditEvent.Action.ToLower().Contains("fund") ||
                auditEvent.EntityType == "financial_operation" ||
                auditEvent.EntityType == "accrual" ||
                auditEvent.EntityType == "supplier_accrual" ||
                auditEvent.EntityType == "fund_operation"),
            _ => query
        };
    }
}
