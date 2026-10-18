using System.Globalization;
using GarageBalance.Api.Application.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfAccrualPeriodRepairQuery(GarageBalanceDbContext dbContext) : IAccrualPeriodRepairQuery
{
    public async Task<IReadOnlyList<AccrualPeriodRepairCandidate>> GetCandidatesAsync(
        IReadOnlyCollection<string> actions,
        CancellationToken cancellationToken)
    {
        // One row per garage: the audit stores the month as "yyyy-MM", so the minimum is the earliest month.
        var rows = await dbContext.AuditEvents.AsNoTracking()
            .Where(auditEvent =>
                auditEvent.EntityType == "accrual" &&
                actions.Contains(auditEvent.Action) &&
                auditEvent.RelatedGarageId != null &&
                auditEvent.RelatedAccountingMonth != null)
            .GroupBy(auditEvent => auditEvent.RelatedGarageId!)
            .Select(group => new { GarageId = group.Key, FirstMonth = group.Min(auditEvent => auditEvent.RelatedAccountingMonth!) })
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => Guid.TryParse(row.GarageId, out var garageId) &&
                DateOnly.TryParseExact($"{row.FirstMonth}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
                    ? new AccrualPeriodRepairCandidate(garageId, month)
                    : null)
            .OfType<AccrualPeriodRepairCandidate>()
            .OrderBy(candidate => candidate.GarageId)
            .ToList();
    }
}
