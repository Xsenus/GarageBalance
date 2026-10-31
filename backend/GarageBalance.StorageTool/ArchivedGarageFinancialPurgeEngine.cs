using System.Data;
using System.Globalization;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.StorageTool;

// Removes archived garages together with their accruals, payments and meter history.
// Fund deposits made from the removed payments are taken out of the fund history and the
// rest of the fund chain is recalculated, exactly as cancelling the payment would do. A deleted
// fund keeps its zero balance: its "remainder returned on deletion" changes by the same amount.
public sealed class ArchivedGarageFinancialPurgeEngine(GarageBalanceDbContext db, IAccountingCleanupBackupVerifier backupVerifier,
    TimeProvider clock, IAuditEventWriter? auditWriter = null)
{
    private const int MaximumGarages = 20;
    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);
    internal static readonly IReadOnlyDictionary<string, string> ReviewedLinks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["public.accrual_payment_allocations.AccrualId->accruals"] = "c",
        ["public.accrual_payment_allocations.CreditAccrualId->accruals"] = "r",
        ["public.accrual_payment_allocations.FinancialOperationId->financial_operations"] = "c",
        ["public.accruals.GarageId->garages"] = "r",
        ["public.expense_payment_batch_operations.OperationId->financial_operations"] = "r",
        ["public.fee_campaign_garages.GarageId->garages"] = "c",
        ["public.financial_operations.GarageId->garages"] = "r",
        ["public.financial_operations.TargetAccrualId->accruals"] = "r",
        ["public.fund_operations.SourceFinancialOperationId->financial_operations"] = "r",
        ["public.garage_meter_connections.BaselineMeterDeviceId->meter_devices"] = "r",
        ["public.garage_meter_connections.GarageId->garages"] = "r",
        ["public.garage_people_count_periods.GarageId->garages"] = "c",
        ["public.garage_report_quick_list_garages.GarageId->garages"] = "r",
        ["public.garage_tariff_assignments.GarageId->garages"] = "r",
        ["public.meter_devices.GarageId->garages"] = "r",
        ["public.meter_devices.ReplacedDeviceId->meter_devices"] = "r",
        ["public.meter_readings.GarageId->garages"] = "r",
        ["public.meter_readings.MeterDeviceId->meter_devices"] = "r",
        ["public.supplier_accruals.SourceFinancialOperationId->financial_operations"] = "r"
    };

    public async Task<ArchivedGarageFinancialPurgePlan> PreviewAsync(CancellationToken cancellationToken)
    {
        var source = await AccountingCleanupSchema.ReadAsync(db, ReviewedLinks, cancellationToken);
        var archived = await db.Garages.AsNoTracking().Where(garage => garage.IsArchived)
            .OrderBy(garage => garage.Number.Length).ThenBy(garage => garage.Number).ThenBy(garage => garage.Id)
            .Select(garage => new { garage.Id, garage.Number, garage.Version }).ToListAsync(cancellationToken);
        var garages = new List<ArchivedGarageFinancialPurgeCandidate>();
        var blocked = new List<string>();
        foreach (var garage in archived)
        {
            if (garages.Count >= MaximumGarages || await IsBlockedAsync(garage.Id, cancellationToken)) blocked.Add(garage.Number);
            else garages.Add(await DescribeAsync(garage.Id, garage.Number, garage.Version, cancellationToken));
        }
        var plan = new ArchivedGarageFinancialPurgePlan(1, Guid.NewGuid(), clock.GetUtcNow(), source, archived.Count, garages, blocked, "");
        return plan with { Fingerprint = ComputeFingerprint(plan) };
    }

    public async Task<ArchivedGarageFinancialPurgeResult> ExecuteAsync(ArchivedGarageFinancialPurgePlan plan, string expectedFingerprint,
        AccountingCleanupBackupReceipt backup, AccountingCleanupExecutionIdentity identity, string reason, CancellationToken cancellationToken)
    {
        if (!identity.HasHostPrivilege || !identity.ApplicationStopped)
            throw Error("cleanup_permission_denied", "Нужны привилегии администратора узла и остановленное приложение.");
        if (string.IsNullOrWhiteSpace(identity.Operator) || identity.Operator.Length > 120 || identity.Operator.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500 || reason.Any(char.IsControl))
            throw Error("cleanup_reason_required", "Укажите исполнителя и причину очистки от 3 до 500 символов без управляющих знаков.");
        ValidatePlan(plan, expectedFingerprint);
        if (db.Database.CurrentTransaction is not null)
            throw Error("cleanup_transaction_active", "Очистка должна владеть отдельной транзакцией.");
        var source = await AccountingCleanupSchema.ReadAsync(db, ReviewedLinks, cancellationToken);
        if (source != plan.Source) throw Error("cleanup_source_changed", "Источник или схема не совпадают с предпросмотром.");
        if (plan.Garages.Count == 0) return new(plan.PlanId, 0, 0, 0, 0, 0m, plan.Fingerprint);
        await backupVerifier.VerifyAsync(backup, source, clock.GetUtcNow(), cancellationToken);
        var ids = plan.Garages.Select(row => row.GarageId).Order().ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (db.Database.IsNpgsql())
            {
                await db.Database.ExecuteSqlRawAsync(
                    "LOCK TABLE garages, accruals, financial_operations, accrual_payment_allocations, fund_operations, funds, " +
                    "meter_readings, meter_devices IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
            }
            var current = new List<ArchivedGarageFinancialPurgeCandidate>();
            foreach (var row in plan.Garages)
            {
                var garage = await db.Garages.AsNoTracking().Where(item => item.Id == row.GarageId && item.IsArchived)
                    .Select(item => new { item.Id, item.Number, item.Version }).SingleOrDefaultAsync(cancellationToken);
                if (garage is null || await IsBlockedAsync(garage.Id, cancellationToken))
                    throw Error("cleanup_preview_stale", "Гаражи или их зависимости изменились после предпросмотра. Нужна новая проверка.");
                current.Add(await DescribeAsync(garage.Id, garage.Number, garage.Version, cancellationToken));
            }
            if (!ComputeFingerprint(plan with { Garages = current }).Equals(plan.Fingerprint, StringComparison.Ordinal))
                throw Error("cleanup_preview_stale", "Гаражи или их зависимости изменились после предпросмотра. Нужна новая проверка.");

            var writer = auditWriter ?? new AuditEventWriter(db);
            var operationIds = await db.FinancialOperations.Where(operation => operation.GarageId.HasValue && ids.Contains(operation.GarageId.Value))
                .Select(operation => operation.Id).ToArrayAsync(cancellationToken);
            var accrualIds = await db.Accruals.Where(accrual => ids.Contains(accrual.GarageId)).Select(accrual => accrual.Id).ToArrayAsync(cancellationToken);
            var removedFundOperations = await RemoveFundOperationsAsync(operationIds, plan, identity, reason, writer, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await db.AccrualPaymentAllocations.Where(allocation => accrualIds.Contains(allocation.AccrualId) || allocation.FinancialOperationId.HasValue && operationIds.Contains(allocation.FinancialOperationId.Value))
                .ExecuteDeleteAsync(cancellationToken);
            var deletedOperations = await db.FinancialOperations.Where(operation => operationIds.Contains(operation.Id)).ExecuteDeleteAsync(cancellationToken);
            var deletedAccruals = await db.Accruals.Where(accrual => accrualIds.Contains(accrual.Id)).ExecuteDeleteAsync(cancellationToken);
            await db.MeterReadings.Where(reading => ids.Contains(reading.GarageId)).ExecuteDeleteAsync(cancellationToken);
            await db.GarageMeterConnections.Where(connection => ids.Contains(connection.GarageId)).ExecuteDeleteAsync(cancellationToken);
            await db.MeterDevices.Where(device => ids.Contains(device.GarageId) && device.ReplacedDeviceId != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(device => device.ReplacedDeviceId, (Guid?)null), cancellationToken);
            await db.MeterDevices.Where(device => ids.Contains(device.GarageId)).ExecuteDeleteAsync(cancellationToken);
            await db.GaragePeopleCountPeriods.Where(period => ids.Contains(period.GarageId)).ExecuteDeleteAsync(cancellationToken);
            await db.GarageReportQuickListGarages.Where(link => ids.Contains(link.GarageId)).ExecuteDeleteAsync(cancellationToken);
            await db.GarageTariffAssignments.Where(assignment => ids.Contains(assignment.GarageId)).ExecuteDeleteAsync(cancellationToken);
            await db.FeeCampaignGarages.Where(link => ids.Contains(link.GarageId)).ExecuteDeleteAsync(cancellationToken);
            await db.OpeningBalanceAdjustments.Where(adjustment => adjustment.TargetKind == OpeningBalanceAdjustmentTargetKinds.Garage &&
                ids.Contains(adjustment.TargetId)).ExecuteDeleteAsync(cancellationToken);
            var deleted = await db.Garages.Where(garage => ids.Contains(garage.Id) && garage.IsArchived).ExecuteDeleteAsync(cancellationToken);
            if (deleted != ids.Length || deletedOperations != current.Sum(row => row.OperationCount) || deletedAccruals != current.Sum(row => row.AccrualCount))
                throw Error("cleanup_preview_stale", "Состав данных изменился во время очистки.");

            foreach (var garage in current)
                writer.Add(new(null, "dictionary.garage_purged_with_financial_history", "garage", garage.GarageId.ToString(),
                    $"Окончательно удалён архивный гараж {garage.GarageNumber} вместе с финансовой историей: начислений {garage.AccrualCount}, " +
                    $"операций {garage.OperationCount}, операций фондов {garage.FundOperationCount}, показаний {garage.MeterReadingCount}. " +
                    $"Действующие поступления {garage.ActiveIncomeTotal.ToString("0.00", CultureInfo.InvariantCulture)} исключены из кассы и фондов.",
                    ActionKind: "delete", EntityDisplayName: $"Гараж {garage.GarageNumber}", Reason: reason.Trim(),
                    Metadata: new Dictionary<string, object?>
                    {
                        ["planId"] = plan.PlanId,
                        ["fingerprint"] = plan.Fingerprint,
                        ["operator"] = identity.Operator,
                        ["backupSha256"] = backup.Sha256,
                        ["accrualCount"] = garage.AccrualCount,
                        ["operationCount"] = garage.OperationCount,
                        ["fundOperationCount"] = garage.FundOperationCount,
                        ["meterReadingCount"] = garage.MeterReadingCount,
                        ["activeIncomeTotal"] = garage.ActiveIncomeTotal
                    },
                    RelatedGarageId: garage.GarageId.ToString(), RelatedGarageNumber: garage.GarageNumber));
            await db.SaveChangesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None);
            return new(plan.PlanId, deleted, deletedAccruals, deletedOperations, removedFundOperations,
                current.Sum(row => row.ActiveIncomeTotal), plan.Fingerprint);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch { /* The caller reports an unknown outcome if the connection was lost. */ }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    public static string ComputeFingerprint(ArchivedGarageFinancialPurgePlan plan) => AccountingCleanupSchema.Hash(JsonSerializer.Serialize(new
    {
        plan.FormatVersion,
        plan.PlanId,
        CreatedAtUtc = plan.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        plan.Source,
        Garages = plan.Garages.OrderBy(row => row.GarageId).Select(row => new
        {
            row.GarageId,
            row.GarageNumber,
            row.GarageVersion,
            row.AccrualCount,
            row.OperationCount,
            row.FundOperationCount,
            row.MeterReadingCount,
            row.MeterDeviceCount,
            ActiveIncomeTotal = row.ActiveIncomeTotal.ToString("0.00", CultureInfo.InvariantCulture),
            row.ContentFingerprint
        })
    }, CanonicalJson));

    // Recalculates each affected fund from the first removed deposit, keeping the
    // BalanceBefore/BalanceAfter chain of every later operation consistent.
    private async Task<int> RemoveFundOperationsAsync(Guid[] operationIds, ArchivedGarageFinancialPurgePlan plan,
        AccountingCleanupExecutionIdentity identity, string reason, IAuditEventWriter writer, CancellationToken cancellationToken)
    {
        var sourced = await db.FundOperations.Where(operation => operation.SourceFinancialOperationId.HasValue &&
            operationIds.Contains(operation.SourceFinancialOperationId.Value)).ToListAsync(cancellationToken);
        foreach (var group in sourced.GroupBy(operation => operation.FundId))
        {
            var fund = await db.Funds.SingleAsync(item => item.Id == group.Key, cancellationToken);
            var history = (await db.FundOperations.Where(operation => operation.FundId == fund.Id).ToListAsync(cancellationToken))
                .OrderBy(operation => operation.CreatedAtUtc).ThenBy(operation => operation.Id).ToList();
            var removed = group.Select(operation => operation.Id).ToHashSet();
            var first = history.FindIndex(operation => removed.Contains(operation.Id));
            var balanceBefore = fund.Balance;
            var balance = history[first].BalanceBefore;
            foreach (var operation in history.Skip(first).Where(operation => !removed.Contains(operation.Id)))
            {
                operation.BalanceBefore = balance;
                if (!operation.IsCanceled)
                    balance += operation.OperationKind == FundOperationKinds.Deposit ? operation.Amount : -operation.Amount;
                operation.BalanceAfter = MoneyMath.RoundMoney(balance);
            }
            fund.Balance = MoneyMath.RoundMoney(balance);
            fund.UpdatedAtUtc = clock.GetUtcNow();
            fund.Version = Guid.NewGuid();
            decimal? remainderBefore = null;
            if (fund.IsArchived && fund.Balance != 0m)
            {
                var remaining = history.Where(operation => !removed.Contains(operation.Id)).ToList();
                if (!ArchivedFundRemainder.TryFind(fund, remaining, out var remainder, out var repairedAmount))
                    throw Error("cleanup_archived_fund_unbalanced",
                        $"Удалённый фонд «{fund.Name}» нельзя оставить с ненулевым остатком: нет подходящей операции возврата остатка.");
                remainderBefore = remainder.Amount;
                ArchivedFundRemainder.Apply(fund, remaining, remainder, repairedAmount, clock.GetUtcNow());
            }
            db.FundOperations.RemoveRange(group);
            writer.Add(new(null, "fund.operations_removed_with_archived_garage", "fund", fund.Id.ToString(),
                $"Из фонда «{fund.Name}» исключены операции удалённого архивного гаража: {group.Count()}. " +
                $"Остаток {balanceBefore.ToString("0.00", CultureInfo.InvariantCulture)} → {fund.Balance.ToString("0.00", CultureInfo.InvariantCulture)}." +
                (remainderBefore is null ? "" : " Фонд удалён, поэтому на ту же сумму изменён возврат остатка при удалении фонда."),
                ActionKind: "delete", EntityDisplayName: fund.Name, Reason: reason.Trim(),
                Metadata: new Dictionary<string, object?>
                {
                    ["planId"] = plan.PlanId,
                    ["operator"] = identity.Operator,
                    ["removedOperationCount"] = group.Count(),
                    ["balanceBefore"] = balanceBefore,
                    ["balanceAfter"] = fund.Balance,
                    ["negativeBalance"] = fund.Balance < 0m,
                    ["archivedRemainderBefore"] = remainderBefore
                }));
        }
        return sourced.Count;
    }

    private async Task<bool> IsBlockedAsync(Guid garageId, CancellationToken cancellationToken) =>
        await db.ChargeServiceSettings.AnyAsync(setting => setting.GarageIds.Contains(garageId), cancellationToken) ||
        await db.SupplierAccruals.AnyAsync(accrual => accrual.SourceFinancialOperationId.HasValue &&
            db.FinancialOperations.Any(operation => operation.Id == accrual.SourceFinancialOperationId && operation.GarageId == garageId), cancellationToken) ||
        await db.Set<ExpensePaymentBatchOperation>().AnyAsync(link => link.Operation.GarageId == garageId, cancellationToken) ||
        await db.FinancialOperations.AnyAsync(operation => operation.GarageId != garageId && operation.TargetAccrualId.HasValue &&
            db.Accruals.Any(accrual => accrual.Id == operation.TargetAccrualId && accrual.GarageId == garageId), cancellationToken);

    private async Task<ArchivedGarageFinancialPurgeCandidate> DescribeAsync(Guid garageId, string number, Guid version, CancellationToken cancellationToken)
    {
        var accruals = await db.Accruals.AsNoTracking().Where(accrual => accrual.GarageId == garageId)
            .Select(accrual => new { accrual.Id, accrual.Amount, accrual.IsCanceled }).ToListAsync(cancellationToken);
        var operations = await db.FinancialOperations.AsNoTracking().Where(operation => operation.GarageId == garageId)
            .Select(operation => new { operation.Id, operation.Amount, operation.IsCanceled, operation.OperationKind }).ToListAsync(cancellationToken);
        var operationIds = operations.Select(operation => operation.Id).ToArray();
        var fundOperations = await db.FundOperations.AsNoTracking().Where(operation => operation.SourceFinancialOperationId.HasValue &&
                operationIds.Contains(operation.SourceFinancialOperationId.Value))
            .Select(operation => new { operation.Id, operation.FundId, operation.Amount, operation.IsCanceled }).ToListAsync(cancellationToken);
        var readings = await db.MeterReadings.AsNoTracking().Where(reading => reading.GarageId == garageId)
            .Select(reading => new { reading.Id, reading.Version }).ToListAsync(cancellationToken);
        var devices = await db.MeterDevices.AsNoTracking().Where(device => device.GarageId == garageId)
            .Select(device => new { device.Id, device.Version }).ToListAsync(cancellationToken);
        var content = AccountingCleanupSchema.Hash(JsonSerializer.Serialize(new
        {
            Accruals = accruals.OrderBy(item => item.Id).Select(item => $"{item.Id}:{item.Amount.ToString("0.00", CultureInfo.InvariantCulture)}:{item.IsCanceled}"),
            Operations = operations.OrderBy(item => item.Id).Select(item => $"{item.Id}:{item.Amount.ToString("0.00", CultureInfo.InvariantCulture)}:{item.IsCanceled}"),
            FundOperations = fundOperations.OrderBy(item => item.Id).Select(item => $"{item.Id}:{item.FundId}:{item.Amount.ToString("0.00", CultureInfo.InvariantCulture)}:{item.IsCanceled}"),
            Readings = readings.OrderBy(item => item.Id).Select(item => $"{item.Id}:{item.Version}"),
            Devices = devices.OrderBy(item => item.Id).Select(item => $"{item.Id}:{item.Version}")
        }, CanonicalJson));
        var activeIncome = operations.Where(item => !item.IsCanceled && item.OperationKind == FinancialOperationKinds.Income).Sum(item => item.Amount);
        return new(garageId, number, version, accruals.Count, operations.Count, fundOperations.Count, readings.Count, devices.Count,
            MoneyMath.RoundMoney(activeIncome), content);
    }

    private void ValidatePlan(ArchivedGarageFinancialPurgePlan plan, string expected)
    {
        if (plan is null || plan.FormatVersion != 1 || plan.PlanId == Guid.Empty || plan.Source is null || plan.Garages is null ||
            plan.Garages.Count > MaximumGarages || plan.CreatedAtUtc > clock.GetUtcNow() || clock.GetUtcNow() - plan.CreatedAtUtc > TimeSpan.FromHours(24) ||
            string.IsNullOrWhiteSpace(expected) || !string.Equals(plan.Fingerprint, expected.Trim(), StringComparison.Ordinal) ||
            plan.Garages.Any(row => row is null || row.GarageId == Guid.Empty || row.GarageVersion == Guid.Empty) ||
            plan.Garages.Select(row => row.GarageId).Distinct().Count() != plan.Garages.Count ||
            !ComputeFingerprint(plan).Equals(plan.Fingerprint, StringComparison.Ordinal))
            throw Error("cleanup_plan_invalid", "Нужен неизменённый свежий предпросмотр и его точный отпечаток.");
    }

    private static AccountingCleanupException Error(string code, string message) => new(code, message);
}
