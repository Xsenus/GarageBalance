using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfAuditEventRepository(GarageBalanceDbContext dbContext) : IAuditEventRepository
{
    public async Task<IReadOnlyList<AuditEvent>> GetEventsAsync(
        AuditEventListRequest request,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AuditEvents.AsNoTracking();
        if (IsSqliteProvider())
        {
            query = ApplyNonDateFilters(query, request);
            var events = await query.ToListAsync(cancellationToken);
            return ApplyDateFilters(events, request)
                .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
                .Take(limit)
                .ToList();
        }

        query = ApplyDateFilters(query, request);
        query = ApplyNonDateFilters(query, request);
        return await query
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<AuditEventPageData> GetEventsPageAsync(
        AuditEventListRequest request,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AuditEvents.AsNoTracking();
        if (IsSqliteProvider())
        {
            query = ApplyNonDateFilters(query, request);
            var events = ApplyDateFilters(await query.ToListAsync(cancellationToken), request).ToList();
            return new AuditEventPageData(
                events.OrderByDescending(auditEvent => auditEvent.CreatedAtUtc).Skip(offset).Take(limit).ToList(),
                events.Count);
        }

        query = ApplyDateFilters(query, request);
        query = ApplyNonDateFilters(query, request);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new AuditEventPageData(items, totalCount);
    }

    public Task<AuditEvent?> FindEventAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.AuditEvents.AsNoTracking()
            .FirstOrDefaultAsync(auditEvent => auditEvent.Id == id, cancellationToken);
    }

    private static IQueryable<AuditEvent> ApplyDateFilters(
        IQueryable<AuditEvent> query,
        AuditEventListRequest request)
    {
        if (request.DateFrom is not null)
        {
            query = query.Where(auditEvent => auditEvent.CreatedAtUtc >= request.DateFrom.Value);
        }

        if (request.DateTo is not null)
        {
            query = query.Where(auditEvent => auditEvent.CreatedAtUtc <= request.DateTo.Value);
        }

        return query;
    }

    private static IEnumerable<AuditEvent> ApplyDateFilters(
        IEnumerable<AuditEvent> events,
        AuditEventListRequest request)
    {
        if (request.DateFrom is not null)
        {
            events = events.Where(auditEvent => auditEvent.CreatedAtUtc >= request.DateFrom.Value);
        }

        if (request.DateTo is not null)
        {
            events = events.Where(auditEvent => auditEvent.CreatedAtUtc <= request.DateTo.Value);
        }

        return events;
    }

    private static IQueryable<AuditEvent> ApplyNonDateFilters(
        IQueryable<AuditEvent> query,
        AuditEventListRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var action = request.Action.Trim();
            query = query.Where(auditEvent => auditEvent.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(request.Section))
        {
            query = ApplySectionFilter(query, request.Section);
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

        query = ApplyRelatedFilters(query, request);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(auditEvent =>
                auditEvent.Action.ToLower().Contains(search) ||
                auditEvent.EntityType.ToLower().Contains(search) ||
                (auditEvent.EntityId != null && auditEvent.EntityId.ToLower().Contains(search)) ||
                (auditEvent.EntityDisplayName != null && auditEvent.EntityDisplayName.ToLower().Contains(search)) ||
                (auditEvent.RelatedGarageId != null && auditEvent.RelatedGarageId.ToLower().Contains(search)) ||
                (auditEvent.RelatedGarageNumber != null && auditEvent.RelatedGarageNumber.ToLower().Contains(search)) ||
                (auditEvent.RelatedAccountingMonth != null && auditEvent.RelatedAccountingMonth.ToLower().Contains(search)) ||
                (auditEvent.RelatedCounterpartyId != null && auditEvent.RelatedCounterpartyId.ToLower().Contains(search)) ||
                (auditEvent.RelatedCounterpartyName != null && auditEvent.RelatedCounterpartyName.ToLower().Contains(search)) ||
                (auditEvent.RelatedDocumentId != null && auditEvent.RelatedDocumentId.ToLower().Contains(search)) ||
                (auditEvent.RelatedDocumentNumber != null && auditEvent.RelatedDocumentNumber.ToLower().Contains(search)) ||
                auditEvent.Summary.ToLower().Contains(search));
        }

        return query;
    }

    private static IQueryable<AuditEvent> ApplyRelatedFilters(
        IQueryable<AuditEvent> query,
        AuditEventListRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RelatedGarage))
        {
            var garage = request.RelatedGarage.Trim().ToLowerInvariant();
            query = query.Where(auditEvent =>
                (auditEvent.RelatedGarageId != null && auditEvent.RelatedGarageId.ToLower().Contains(garage)) ||
                (auditEvent.RelatedGarageNumber != null && auditEvent.RelatedGarageNumber.ToLower().Contains(garage)));
        }

        if (!string.IsNullOrWhiteSpace(request.RelatedAccountingMonth))
        {
            var accountingMonth = request.RelatedAccountingMonth.Trim().ToLowerInvariant();
            query = query.Where(auditEvent =>
                auditEvent.RelatedAccountingMonth != null &&
                auditEvent.RelatedAccountingMonth.ToLower() == accountingMonth);
        }

        if (!string.IsNullOrWhiteSpace(request.RelatedCounterparty))
        {
            var counterparty = request.RelatedCounterparty.Trim().ToLowerInvariant();
            query = query.Where(auditEvent =>
                (auditEvent.RelatedCounterpartyId != null && auditEvent.RelatedCounterpartyId.ToLower().Contains(counterparty)) ||
                (auditEvent.RelatedCounterpartyName != null && auditEvent.RelatedCounterpartyName.ToLower().Contains(counterparty)));
        }

        if (!string.IsNullOrWhiteSpace(request.RelatedDocument))
        {
            var document = request.RelatedDocument.Trim().ToLowerInvariant();
            query = query.Where(auditEvent =>
                (auditEvent.RelatedDocumentId != null && auditEvent.RelatedDocumentId.ToLower().Contains(document)) ||
                (auditEvent.RelatedDocumentNumber != null && auditEvent.RelatedDocumentNumber.ToLower().Contains(document)));
        }

        return query;
    }

    private static IQueryable<AuditEvent> ApplySectionFilter(IQueryable<AuditEvent> query, string section)
    {
        var normalizedSection = section.Trim().ToLowerInvariant();
        var sectionPrefix = normalizedSection + ".";
        return query.Where(auditEvent =>
            (auditEvent.Section != null && auditEvent.Section.ToLower() == normalizedSection) ||
            (auditEvent.Section == null && auditEvent.Action.ToLower().StartsWith(sectionPrefix)));
    }

    private static IQueryable<AuditEvent> ApplyActionKindFilter(IQueryable<AuditEvent> query, string actionKind)
    {
        var normalizedActionKind = actionKind.Trim().ToLowerInvariant();
        var needles = GetActionKindNeedles(actionKind);
        return needles.Count switch
        {
            1 => query.Where(auditEvent =>
                (auditEvent.ActionKind != null && auditEvent.ActionKind.ToLower() == normalizedActionKind) ||
                (auditEvent.ActionKind == null && auditEvent.Action.ToLower().Contains(needles[0]))),
            2 => query.Where(auditEvent =>
                (auditEvent.ActionKind != null && auditEvent.ActionKind.ToLower() == normalizedActionKind) ||
                (auditEvent.ActionKind == null && (auditEvent.Action.ToLower().Contains(needles[0]) || auditEvent.Action.ToLower().Contains(needles[1])))),
            3 => query.Where(auditEvent =>
                (auditEvent.ActionKind != null && auditEvent.ActionKind.ToLower() == normalizedActionKind) ||
                (auditEvent.ActionKind == null && (
                    auditEvent.Action.ToLower().Contains(needles[0]) ||
                    auditEvent.Action.ToLower().Contains(needles[1]) ||
                    auditEvent.Action.ToLower().Contains(needles[2])))),
            _ => query
        };
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

    private static IQueryable<AuditEvent> ApplyQuickFilter(IQueryable<AuditEvent> query, string quickFilter)
    {
        return quickFilter.Trim().ToLowerInvariant() switch
        {
            "deletions" => query.Where(auditEvent =>
                auditEvent.ActionKind == "archive" ||
                auditEvent.ActionKind == "delete" ||
                auditEvent.ActionKind == "cancel" ||
                (auditEvent.ActionKind == null && (
                    auditEvent.Action.ToLower().Contains("_archived") ||
                    auditEvent.Action.ToLower().Contains("_deleted") ||
                    auditEvent.Action.ToLower().Contains("_canceled") ||
                    auditEvent.Action.ToLower().Contains("_cancelled")))),
            "restores" => query.Where(auditEvent =>
                auditEvent.ActionKind == "restore" ||
                (auditEvent.ActionKind == null && auditEvent.Action.ToLower().Contains("_restored"))),
            "financial" => query.Where(auditEvent =>
                auditEvent.Section == "finance" ||
                (auditEvent.Section == null && auditEvent.Action.ToLower().StartsWith("finance.")) ||
                auditEvent.Action.ToLower().Contains("fund") ||
                auditEvent.EntityType == "financial_operation" ||
                auditEvent.EntityType == "accrual" ||
                auditEvent.EntityType == "supplier_accrual" ||
                auditEvent.EntityType == "fund_operation"),
            _ => query
        };
    }

    private bool IsSqliteProvider() =>
        string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal);
}
