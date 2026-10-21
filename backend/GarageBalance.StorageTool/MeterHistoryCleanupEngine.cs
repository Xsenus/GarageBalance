using System.Data;
using System.Globalization;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.StorageTool;

// Clears the «Показания» form after acceptance testing: every meter reading and every
// meter replacement is removed, while start values, connections and «Начало учёта»
// boundaries stay. Accruals calculated from readings are cancelled and payments are
// redistributed, so money paid for them becomes an advance of the same service.
public sealed class MeterHistoryCleanupEngine(GarageBalanceDbContext db, IAccountingCleanupBackupVerifier backupVerifier,
    TimeProvider clock, IAuditEventWriter? auditWriter = null)
{
    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);
    internal static readonly IReadOnlyDictionary<string, string> ReviewedLinks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["public.accrual_payment_allocations.AccrualId->accruals"] = "c",
        ["public.financial_operations.TargetAccrualId->accruals"] = "r",
        ["public.garage_meter_connections.BaselineMeterDeviceId->meter_devices"] = "r",
        ["public.meter_devices.ReplacedDeviceId->meter_devices"] = "r",
        ["public.meter_readings.MeterDeviceId->meter_devices"] = "r"
    };

    public async Task<MeterHistoryCleanupPlan> PreviewAsync(CancellationToken cancellationToken)
    {
        var source = await AccountingCleanupSchema.ReadAsync(db, ReviewedLinks, cancellationToken);
        var content = await DescribeAsync(cancellationToken);
        var plan = new MeterHistoryCleanupPlan(1, Guid.NewGuid(), clock.GetUtcNow(), source, content.GarageIds.Count, content.Readings.Count,
            content.KeptReadings.Count, content.Replacements.Count, content.Reactivated.Count, content.Accruals.Count, content.PaidAccrualIds.Count,
            MoneyMath.RoundMoney(content.Accruals.Sum(item => item.Amount)), content.PaidTotal, content.Fingerprint, "");
        return plan with { Fingerprint = ComputeFingerprint(plan) };
    }

    public async Task<MeterHistoryCleanupResult> ExecuteAsync(MeterHistoryCleanupPlan plan, string expectedFingerprint,
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
        if (plan.ReadingCount == 0 && plan.ReplacementDeviceCount == 0 && plan.MeteredAccrualCount == 0)
            return new(plan.PlanId, 0, 0, 0, 0, 0, plan.Fingerprint);
        await backupVerifier.VerifyAsync(backup, source, clock.GetUtcNow(), cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (db.Database.IsNpgsql())
                await db.Database.ExecuteSqlRawAsync(
                    "LOCK TABLE garages, meter_readings, meter_devices, garage_meter_connections, accruals, accrual_payment_allocations, " +
                    "financial_operations IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
            var content = await DescribeAsync(cancellationToken);
            if (!content.Fingerprint.Equals(plan.ContentFingerprint, StringComparison.Ordinal))
                throw Error("cleanup_preview_stale", "Показания, приборы или начисления изменились после предпросмотра. Нужна новая проверка.");

            var now = clock.GetUtcNow();
            var accrualIds = content.Accruals.Select(item => item.Id).ToArray();
            var accruals = await db.Accruals.Where(accrual => accrualIds.Contains(accrual.Id)).ToListAsync(cancellationToken);
            foreach (var accrual in accruals)
            {
                accrual.IsCanceled = true;
                accrual.UpdatedAtUtc = now;
            }
            var keys = accruals.Select(accrual => new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)).Distinct().ToArray();
            var rebuild = await new EfAccrualPaymentAllocationRepository(db).RebuildAsync(keys, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            var keptReadingIds = content.KeptReadings.ToArray();
            var deletedReadings = await db.MeterReadings.Where(reading => !keptReadingIds.Contains(reading.Id)).ExecuteDeleteAsync(cancellationToken);
            var replacementIds = content.Replacements.ToArray();
            await db.MeterDevices.Where(device => replacementIds.Contains(device.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(device => device.ReplacedDeviceId, (Guid?)null), cancellationToken);
            var deletedDevices = await db.MeterDevices.Where(device => replacementIds.Contains(device.Id)).ExecuteDeleteAsync(cancellationToken);
            var reactivatedIds = content.Reactivated.ToArray();
            var reactivated = await db.MeterDevices.Where(device => reactivatedIds.Contains(device.Id)).ToListAsync(cancellationToken);
            foreach (var device in reactivated)
            {
                device.RemovedOn = null;
                device.FinalValue = null;
                device.Version = Guid.NewGuid();
                device.UpdatedAtUtc = now;
            }
            if (deletedReadings != content.Readings.Count || deletedDevices != replacementIds.Length || reactivated.Count != reactivatedIds.Length)
                throw Error("cleanup_preview_stale", "Состав данных изменился во время очистки.");

            var writer = auditWriter ?? new AuditEventWriter(db);
            var garageNumbers = await db.Garages.AsNoTracking().Where(garage => content.GarageIds.Contains(garage.Id))
                .ToDictionaryAsync(garage => garage.Id, garage => garage.Number, cancellationToken);
            foreach (var garageId in content.GarageIds.Order())
            {
                var number = garageNumbers.GetValueOrDefault(garageId, "?");
                var readings = content.Readings.Count(item => item.GarageId == garageId);
                var canceled = accruals.Count(accrual => accrual.GarageId == garageId);
                writer.Add(new(null, "finance.meter_history_cleared", "garage", garageId.ToString(),
                    $"Очищены показания гаража {number} после проверки замены счётчиков: показаний {readings}, " +
                    $"замен {content.ReplacementGarages.Count(item => item == garageId)}, отменено начислений по показаниям {canceled}. " +
                    "Стартовые значения сохранены, оплаты перераспределены.",
                    ActionKind: "delete", EntityDisplayName: $"Гараж {number}", Reason: reason.Trim(),
                    Metadata: new Dictionary<string, object?>
                    {
                        ["planId"] = plan.PlanId,
                        ["fingerprint"] = plan.Fingerprint,
                        ["operator"] = identity.Operator,
                        ["backupSha256"] = backup.Sha256,
                        ["readingCount"] = readings,
                        ["canceledAccrualIds"] = accruals.Where(accrual => accrual.GarageId == garageId).Select(accrual => accrual.Id).ToArray()
                    },
                    RelatedGarageId: garageId.ToString(), RelatedGarageNumber: number));
            }
            writer.Add(new(null, "finance.meter_history_cleanup", "meter_accounting", plan.PlanId.ToString(),
                $"Очищена форма «Показания»: удалено показаний {deletedReadings}, замен счётчиков {deletedDevices}, " +
                $"отменено начислений по показаниям {accruals.Count}; оплаты {plan.PaidMeteredTotal.ToString("0.00", CultureInfo.InvariantCulture)} перераспределены.",
                ActionKind: "delete", EntityDisplayName: "Учёт показаний", Reason: reason.Trim(),
                Metadata: new Dictionary<string, object?>
                {
                    ["planId"] = plan.PlanId,
                    ["fingerprint"] = plan.Fingerprint,
                    ["operator"] = identity.Operator,
                    ["backupSha256"] = backup.Sha256,
                    ["garageCount"] = content.GarageIds.Count,
                    ["rebuiltKeys"] = rebuild.KeyCount
                }));
            await db.SaveChangesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None);
            return new(plan.PlanId, deletedReadings, deletedDevices, reactivated.Count, accruals.Count, rebuild.KeyCount, plan.Fingerprint);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch { /* The caller reports an unknown outcome if the connection was lost. */ }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    public static string ComputeFingerprint(MeterHistoryCleanupPlan plan) => AccountingCleanupSchema.Hash(JsonSerializer.Serialize(new
    {
        plan.FormatVersion,
        plan.PlanId,
        CreatedAtUtc = plan.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        plan.Source,
        plan.GarageCount,
        plan.ReadingCount,
        plan.KeptStartReadingCount,
        plan.ReplacementDeviceCount,
        plan.ReactivatedDeviceCount,
        plan.MeteredAccrualCount,
        plan.PaidMeteredAccrualCount,
        MeteredAccrualTotal = plan.MeteredAccrualTotal.ToString("0.00", CultureInfo.InvariantCulture),
        PaidMeteredTotal = plan.PaidMeteredTotal.ToString("0.00", CultureInfo.InvariantCulture),
        plan.ContentFingerprint
    }, CanonicalJson));

    private async Task<Content> DescribeAsync(CancellationToken cancellationToken)
    {
        var allReadings = await db.MeterReadings.AsNoTracking()
            .Select(reading => new ReadingRow(reading.Id, reading.GarageId, reading.Version, reading.MeterKind, reading.AccountingMonth,
                reading.ReadingDate, reading.IsCanceled)).ToListAsync(cancellationToken);
        var devices = await db.MeterDevices.AsNoTracking().ToListAsync(cancellationToken);
        // A replacement is any device that took over from another one, except the «Начало учёта»
        // boundaries that carry the start values of the current accounting.
        var replacements = devices.Where(device => !MeterBaselineResolver.IsRestartBoundary(device) &&
            (device.ReplacedDeviceId.HasValue || device.ReplacementAccountingMonth.HasValue || device.HasReplacementSnapshot)).ToList();
        var replacementIds = replacements.Select(device => device.Id).ToHashSet();
        if (await db.GarageMeterConnections.AnyAsync(connection => connection.BaselineMeterDeviceId.HasValue &&
                replacementIds.Contains(connection.BaselineMeterDeviceId.Value), cancellationToken))
            throw Error("cleanup_replacement_is_baseline", "Заменённый прибор служит стартом подключения. Очистка требует отдельного разбора.");
        if (devices.Any(device => !replacementIds.Contains(device.Id) && device.ReplacedDeviceId.HasValue && replacementIds.Contains(device.ReplacedDeviceId.Value)))
            throw Error("cleanup_replacement_chain_unexpected", "После замены установлен прибор, который не является заменой. Очистка требует отдельного разбора.");
        // After removing replacements the latest remaining device of every garage meter becomes active again.
        var reactivated = devices.Where(device => !replacementIds.Contains(device.Id) && !device.IsCanceled)
            .GroupBy(device => (device.GarageId, device.MeterKind))
            .Where(group => replacements.Any(device => device.GarageId == group.Key.GarageId && device.MeterKind == group.Key.MeterKind))
            .Select(group => group.OrderByDescending(device => device.InstalledOn).ThenByDescending(device => device.CreatedAtUtc).First())
            .Where(device => device.RemovedOn.HasValue || device.FinalValue.HasValue)
            .Select(device => device.Id).ToList();
        var candidates = await db.Accruals.AsNoTracking().Where(accrual => !accrual.IsCanceled &&
                (accrual.RequiresMeterReading || accrual.CalculationMeterKind != null))
            .Select(accrual => new AccrualRow(accrual.Id, accrual.GarageId, accrual.Amount, accrual.CalculationDetailsJson)).ToListAsync(cancellationToken);
        // Historical amounts without a formula do not depend on readings and stay as they are.
        var accruals = candidates.Where(accrual => !HasOnlyHistoricalAmount(accrual.CalculationDetailsJson)).OrderBy(accrual => accrual.Id).ToList();
        var accrualIds = accruals.Select(accrual => accrual.Id).ToArray();
        var paid = await db.AccrualPaymentAllocations.AsNoTracking().Where(allocation => allocation.IsActive &&
                !allocation.FinancialOperation.IsCanceled && accrualIds.Contains(allocation.AccrualId))
            .Select(allocation => new { allocation.AccrualId, allocation.Amount }).ToListAsync(cancellationToken);
        // A garage meter without a device, a connection start or a start value in the garage card
        // used its earliest reading as the start value: that reading stays, so the start is not lost.
        var legacyStarts = await db.Garages.AsNoTracking()
            .Select(garage => new { garage.Id, garage.InitialWaterMeterValue, garage.InitialElectricityMeterValue }).ToListAsync(cancellationToken);
        var connectionStarts = await db.GarageMeterConnections.AsNoTracking().Where(connection => connection.BaselineMeterDeviceId.HasValue)
            .Select(connection => new { connection.GarageId, connection.MeterKind }).ToListAsync(cancellationToken);
        bool HasStartSource(Guid garageId, string kind) =>
            devices.Any(device => device.GarageId == garageId && device.MeterKind == kind && !device.IsCanceled && !replacementIds.Contains(device.Id)) ||
            connectionStarts.Any(connection => connection.GarageId == garageId && connection.MeterKind == kind) ||
            legacyStarts.Any(garage => garage.Id == garageId && (kind == MeterKinds.Water ? garage.InitialWaterMeterValue.HasValue :
                kind == MeterKinds.Electricity && garage.InitialElectricityMeterValue.HasValue));
        var kept = allReadings.Where(reading => !reading.IsCanceled)
            .GroupBy(reading => (reading.GarageId, reading.MeterKind))
            .Where(group => !HasStartSource(group.Key.GarageId, group.Key.MeterKind))
            .Select(group => group.OrderBy(reading => reading.AccountingMonth).ThenBy(reading => reading.ReadingDate).ThenBy(reading => reading.Id).First().Id)
            .ToHashSet();
        var readings = allReadings.Where(reading => !kept.Contains(reading.Id)).ToList();
        var garageIds = readings.Select(reading => reading.GarageId).Concat(replacements.Select(device => device.GarageId))
            .Concat(accruals.Select(accrual => accrual.GarageId)).Distinct().ToList();
        var fingerprint = AccountingCleanupSchema.Hash(JsonSerializer.Serialize(new
        {
            Readings = allReadings.OrderBy(item => item.Id).Select(item => $"{item.Id}:{item.Version}:{kept.Contains(item.Id)}"),
            Devices = devices.OrderBy(item => item.Id).Select(item => $"{item.Id}:{item.Version}"),
            Accruals = accruals.Select(item => $"{item.Id}:{item.Amount.ToString("0.00", CultureInfo.InvariantCulture)}"),
            Paid = paid.OrderBy(item => item.AccrualId).ThenBy(item => item.Amount).Select(item => $"{item.AccrualId}:{item.Amount.ToString("0.00", CultureInfo.InvariantCulture)}")
        }, CanonicalJson));
        return new(readings, kept.Order().ToList(), replacements.Select(device => device.Id).ToList(), replacements.Select(device => device.GarageId).ToList(),
            reactivated, accruals, paid.Select(item => item.AccrualId).ToHashSet(), MoneyMath.RoundMoney(paid.Sum(item => item.Amount)),
            garageIds, fingerprint);
    }

    private static bool HasOnlyHistoricalAmount(string? calculationDetailsJson)
    {
        if (string.IsNullOrWhiteSpace(calculationDetailsJson)) return true;
        try
        {
            using var document = JsonDocument.Parse(calculationDetailsJson);
            return !document.RootElement.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number || version.GetInt32() == 0;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private void ValidatePlan(MeterHistoryCleanupPlan plan, string expected)
    {
        if (plan is null || plan.FormatVersion != 1 || plan.PlanId == Guid.Empty || plan.Source is null ||
            plan.CreatedAtUtc > clock.GetUtcNow() || clock.GetUtcNow() - plan.CreatedAtUtc > TimeSpan.FromHours(24) ||
            string.IsNullOrWhiteSpace(expected) || !string.Equals(plan.Fingerprint, expected.Trim(), StringComparison.Ordinal) ||
            !ComputeFingerprint(plan).Equals(plan.Fingerprint, StringComparison.Ordinal))
            throw Error("cleanup_plan_invalid", "Нужен неизменённый свежий предпросмотр и его точный отпечаток.");
    }

    private static AccountingCleanupException Error(string code, string message) => new(code, message);

    private sealed record ReadingRow(Guid Id, Guid GarageId, Guid Version, string MeterKind, DateOnly AccountingMonth, DateOnly ReadingDate, bool IsCanceled);
    private sealed record AccrualRow(Guid Id, Guid GarageId, decimal Amount, string? CalculationDetailsJson);
    private sealed record Content(IReadOnlyList<ReadingRow> Readings, IReadOnlyList<Guid> KeptReadings, IReadOnlyList<Guid> Replacements, IReadOnlyList<Guid> ReplacementGarages,
        IReadOnlyList<Guid> Reactivated, IReadOnlyList<AccrualRow> Accruals, IReadOnlySet<Guid> PaidAccrualIds, decimal PaidTotal,
        IReadOnlyList<Guid> GarageIds, string Fingerprint);
}
