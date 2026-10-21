using System.Data;
using System.Globalization;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.StorageTool;

// Brings a deleted (archived) fund back to its zero balance after its history was changed outside
// the application, by correcting the "remainder returned on deletion" withdrawal by the same amount.
// Only updates rows: a remainder that drops to zero is cancelled, nothing is deleted.
public sealed class ArchivedFundBalanceRepairEngine(GarageBalanceDbContext db, IAccountingCleanupBackupVerifier backupVerifier,
    TimeProvider clock, IAuditEventWriter? auditWriter = null)
{
    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> ReviewedLinks = new Dictionary<string, string>(StringComparer.Ordinal);

    public async Task<ArchivedFundBalanceRepairPlan> PreviewAsync(CancellationToken cancellationToken)
    {
        var source = await AccountingCleanupSchema.ReadAsync(db, ReviewedLinks, cancellationToken);
        var (funds, blocked) = await DescribeAsync(tracked: false, cancellationToken);
        var plan = new ArchivedFundBalanceRepairPlan(1, Guid.NewGuid(), clock.GetUtcNow(), source,
            funds.Select(item => item.Candidate).ToList(), blocked, "");
        return plan with { Fingerprint = ComputeFingerprint(plan) };
    }

    public async Task<ArchivedFundBalanceRepairResult> ExecuteAsync(ArchivedFundBalanceRepairPlan plan, string expectedFingerprint,
        AccountingCleanupBackupReceipt backup, AccountingCleanupExecutionIdentity identity, string reason, CancellationToken cancellationToken)
    {
        if (!identity.HasHostPrivilege || !identity.ApplicationStopped)
            throw Error("cleanup_permission_denied", "Нужны привилегии администратора узла и остановленное приложение.");
        if (string.IsNullOrWhiteSpace(identity.Operator) || identity.Operator.Length > 120 || identity.Operator.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500 || reason.Any(char.IsControl))
            throw Error("cleanup_reason_required", "Укажите исполнителя и причину ремонта от 3 до 500 символов без управляющих знаков.");
        ValidatePlan(plan, expectedFingerprint);
        if (db.Database.CurrentTransaction is not null)
            throw Error("cleanup_transaction_active", "Ремонт должен владеть отдельной транзакцией.");
        var source = await AccountingCleanupSchema.ReadAsync(db, ReviewedLinks, cancellationToken);
        if (source != plan.Source) throw Error("cleanup_source_changed", "Источник или схема не совпадают с предпросмотром.");
        if (plan.Funds.Count == 0) return new(plan.PlanId, 0, plan.Fingerprint);
        await backupVerifier.VerifyAsync(backup, source, clock.GetUtcNow(), cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (db.Database.IsNpgsql())
                await db.Database.ExecuteSqlRawAsync("LOCK TABLE funds, fund_operations IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
            var (current, blocked) = await DescribeAsync(tracked: true, cancellationToken);
            if (!ComputeFingerprint(plan with { Funds = current.Select(item => item.Candidate).ToList(), BlockedFundNames = blocked })
                    .Equals(plan.Fingerprint, StringComparison.Ordinal))
                throw Error("cleanup_preview_stale", "Фонды изменились после предпросмотра. Нужна новая проверка.");

            var writer = auditWriter ?? new AuditEventWriter(db);
            var now = clock.GetUtcNow();
            foreach (var (candidate, fund, history, remainder) in current)
            {
                ArchivedFundRemainder.Apply(fund, history, remainder, candidate.RepairedRemainderAmount, now);
                writer.Add(new(null, "fund.archived_balance_repaired", "fund", fund.Id.ToString(),
                    $"Удалённый фонд «{fund.Name}» возвращён к нулевому остатку: остаток {Money(candidate.Balance)} → 0.00, " +
                    $"возврат остатка при удалении фонда {Money(candidate.RemainderAmount)} → {Money(candidate.RepairedRemainderAmount)}.",
                    ActionKind: "update", EntityDisplayName: fund.Name, Reason: reason.Trim(),
                    Metadata: new Dictionary<string, object?>
                    {
                        ["planId"] = plan.PlanId,
                        ["fingerprint"] = plan.Fingerprint,
                        ["operator"] = identity.Operator,
                        ["backupSha256"] = backup.Sha256,
                        ["balanceBefore"] = candidate.Balance,
                        ["remainderOperationId"] = candidate.RemainderOperationId,
                        ["remainderBefore"] = candidate.RemainderAmount,
                        ["remainderAfter"] = candidate.RepairedRemainderAmount
                    }));
            }
            await db.SaveChangesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None);
            return new(plan.PlanId, current.Count, plan.Fingerprint);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch { /* The caller reports an unknown outcome if the connection was lost. */ }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    public static string ComputeFingerprint(ArchivedFundBalanceRepairPlan plan) => AccountingCleanupSchema.Hash(JsonSerializer.Serialize(new
    {
        plan.FormatVersion,
        plan.PlanId,
        CreatedAtUtc = plan.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        plan.Source,
        Funds = plan.Funds.OrderBy(row => row.FundId).Select(row => new
        {
            row.FundId,
            row.FundName,
            row.FundVersion,
            Balance = Money(row.Balance),
            row.RemainderOperationId,
            RemainderAmount = Money(row.RemainderAmount),
            RepairedRemainderAmount = Money(row.RepairedRemainderAmount)
        }),
        BlockedFundNames = plan.BlockedFundNames.Order(StringComparer.Ordinal)
    }, CanonicalJson));

    private async Task<(List<(ArchivedFundBalanceRepairCandidate Candidate, Fund Fund, List<FundOperation> History, FundOperation Remainder)> Funds,
        List<string> Blocked)> DescribeAsync(bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? db.Funds : db.Funds.AsNoTracking();
        // Archived funds are few; the balance is compared in memory because SQLite tests cannot compare decimals.
        var funds = (await query.Where(fund => fund.IsArchived).ToListAsync(cancellationToken))
            .Where(fund => fund.Balance != 0m).OrderBy(fund => fund.Name, StringComparer.Ordinal).ThenBy(fund => fund.Id).ToList();
        var result = new List<(ArchivedFundBalanceRepairCandidate, Fund, List<FundOperation>, FundOperation)>();
        var blocked = new List<string>();
        foreach (var fund in funds)
        {
            var operations = tracked ? db.FundOperations : db.FundOperations.AsNoTracking();
            var history = (await operations.Where(operation => operation.FundId == fund.Id).ToListAsync(cancellationToken))
                .OrderBy(operation => operation.CreatedAtUtc).ThenBy(operation => operation.Id).ToList();
            if (!ArchivedFundRemainder.TryFind(fund, history, out var remainder, out var repaired))
            {
                blocked.Add(fund.Name);
                continue;
            }
            result.Add((new(fund.Id, fund.Name, fund.Version, fund.Balance, remainder.Id, remainder.Amount, repaired), fund, history, remainder));
        }
        return (result, blocked);
    }

    private void ValidatePlan(ArchivedFundBalanceRepairPlan plan, string expected)
    {
        if (plan is null || plan.FormatVersion != 1 || plan.PlanId == Guid.Empty || plan.Source is null || plan.Funds is null ||
            plan.BlockedFundNames is null || plan.CreatedAtUtc > clock.GetUtcNow() || clock.GetUtcNow() - plan.CreatedAtUtc > TimeSpan.FromHours(24) ||
            string.IsNullOrWhiteSpace(expected) || !string.Equals(plan.Fingerprint, expected.Trim(), StringComparison.Ordinal) ||
            plan.Funds.Any(row => row is null || row.FundId == Guid.Empty || row.RemainderOperationId == Guid.Empty) ||
            plan.Funds.Select(row => row.FundId).Distinct().Count() != plan.Funds.Count ||
            !ComputeFingerprint(plan).Equals(plan.Fingerprint, StringComparison.Ordinal))
            throw Error("cleanup_plan_invalid", "Нужен неизменённый свежий предпросмотр и его точный отпечаток.");
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static AccountingCleanupException Error(string code, string message) => new(code, message);
}
