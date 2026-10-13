using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.StorageTool;

public sealed class ArchivedGarageCleanupEngine(GarageBalanceDbContext db, IAccountingCleanupBackupVerifier backupVerifier,
    TimeProvider clock, IAuditEventWriter? auditWriter = null)
{
    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> ExpectedDeleteRules = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["public.accruals"] = "r",
        ["public.financial_operations"] = "r",
        ["public.fee_campaign_garages"] = "c",
        ["public.garage_meter_connections"] = "r",
        ["public.garage_people_count_periods"] = "c",
        ["public.garage_report_quick_list_garages"] = "r",
        ["public.garage_tariff_assignments"] = "r",
        ["public.meter_devices"] = "r",
        ["public.meter_readings"] = "r"
    };

    public async Task<ArchivedGarageCleanupPlan> PreviewAsync(int maximumGarages, CancellationToken cancellationToken)
    {
        if (maximumGarages is < 1 or > 500)
            throw Error("cleanup_limit_invalid", "Размер одной операции должен быть от 1 до 500 гаражей.");
        var source = await GetSourceAsync(cancellationToken);
        var query = CandidateQuery();
        var total = await query.CountAsync(cancellationToken);
        var eligibleQuery = Eligible(query);
        var eligibleCount = await eligibleQuery.CountAsync(cancellationToken);
        var eligible = await eligibleQuery.OrderBy(row => row.Number.Length).ThenBy(row => row.Number).ThenBy(row => row.Id)
            .Take(maximumGarages).ToListAsync(cancellationToken);
        var protectedRows = await query.Where(row => row.HasBalance || row.HasOperations || row.HasAccruals || row.HasOpeningAdjustments ||
                row.HasReadings || row.HasTariffAssignments || row.HasFeeLinks || row.HasServiceScope)
            .OrderBy(row => row.Number.Length).ThenBy(row => row.Number).ThenBy(row => row.Id).Take(100).ToListAsync(cancellationToken);
        var reasonCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["opening_balance"] = await query.CountAsync(row => row.HasBalance, cancellationToken),
            ["payments"] = await query.CountAsync(row => row.HasOperations, cancellationToken),
            ["accruals"] = await query.CountAsync(row => row.HasAccruals, cancellationToken),
            ["opening_adjustments"] = await query.CountAsync(row => row.HasOpeningAdjustments, cancellationToken),
            ["meter_readings"] = await query.CountAsync(row => row.HasReadings, cancellationToken),
            ["individual_tariffs"] = await query.CountAsync(row => row.HasTariffAssignments, cancellationToken),
            ["fee_campaigns"] = await query.CountAsync(row => row.HasFeeLinks, cancellationToken),
            ["service_scope"] = await query.CountAsync(row => row.HasServiceScope, cancellationToken)
        };
        var plan = new ArchivedGarageCleanupPlan(1, Guid.NewGuid(), clock.GetUtcNow(), source, total, eligibleCount,
            eligibleCount > eligible.Count, eligible.Select(ToCandidate).ToArray(), protectedRows.Select(ToCandidate).ToArray(), reasonCounts, "");
        return plan with { Fingerprint = ComputeFingerprint(plan) };
    }

    public async Task<ArchivedGarageCleanupResult> ExecuteAsync(ArchivedGarageCleanupPlan plan, string expectedFingerprint,
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
        var source = await GetSourceAsync(cancellationToken);
        if (source != plan.Source) throw Error("cleanup_source_changed", "Источник или схема не совпадают с предпросмотром.");
        if (plan.EligibleGarages.Count == 0) return new(plan.PlanId, 0, plan.Fingerprint);
        await backupVerifier.VerifyAsync(backup, source, clock.GetUtcNow(), cancellationToken);
        var ids = plan.EligibleGarages.Select(row => row.GarageId).Order().ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, cancellationToken);
        try
        {
            // FOR UPDATE also excludes FK inserts. With READ COMMITTED, dependencies
            // committed while this lock was waiting are visible to the next query.
            if (db.Database.IsNpgsql())
                await db.Garages.FromSqlInterpolated($"SELECT * FROM garages WHERE \"Id\" = ANY({ids}) ORDER BY \"Id\" FOR UPDATE")
                    .AsNoTracking().ToListAsync(cancellationToken);
            else await db.Garages.AsNoTracking().Where(garage => ids.Contains(garage.Id)).ToListAsync(cancellationToken);
            var current = await CandidateQuery().Where(row => ids.Contains(row.Id)).ToListAsync(cancellationToken);
            var currentPlan = plan with { EligibleGarages = current.Select(ToCandidate).ToArray() };
            if (current.Count != ids.Length || current.Any(row => GetProtectionReasons(row).Count > 0) ||
                !ComputeFingerprint(currentPlan).Equals(plan.Fingerprint, StringComparison.Ordinal))
                throw Error("cleanup_preview_stale", "Гаражи или их зависимости изменились после предпросмотра. Нужна новая проверка.");
            if (await GetSourceAsync(cancellationToken) != source)
                throw Error("cleanup_source_changed", "Схема изменилась после блокировки. Очистка отменена.");

            await db.GarageMeterConnections.Where(connection => ids.Contains(connection.GarageId)).ExecuteDeleteAsync(cancellationToken);
            await db.MeterDevices.Where(device => ids.Contains(device.GarageId)).ExecuteDeleteAsync(cancellationToken);
            await db.GaragePeopleCountPeriods.Where(period => ids.Contains(period.GarageId)).ExecuteDeleteAsync(cancellationToken);
            await db.GarageReportQuickListGarages.Where(link => ids.Contains(link.GarageId)).ExecuteDeleteAsync(cancellationToken);
            var deleted = await db.Garages.Where(garage => ids.Contains(garage.Id) && garage.IsArchived).ExecuteDeleteAsync(cancellationToken);
            if (deleted != ids.Length) throw Error("cleanup_preview_stale", "Состав гаражей изменился во время очистки.");
            var writer = auditWriter ?? new AuditEventWriter(db);
            foreach (var garage in current)
                writer.Add(new(null, "dictionary.garage_purged_without_financial_history", "garage", garage.Id.ToString(),
                    $"Окончательно удалён архивный гараж {garage.Number} без финансовой истории.",
                    ActionKind: "delete", EntityDisplayName: $"Гараж {garage.Number}", Reason: reason.Trim(),
                    Metadata: new Dictionary<string, object?>
                    {
                        ["planId"] = plan.PlanId,
                        ["fingerprint"] = plan.Fingerprint,
                        ["operator"] = identity.Operator,
                        ["backupSha256"] = backup.Sha256
                    },
                    RelatedGarageId: garage.Id.ToString(), RelatedGarageNumber: garage.Number));
            await db.SaveChangesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Once commit is sent, finish reading its outcome even if the CLI is
            // canceled. A lost acknowledgment must not be reported as a rollback.
            await transaction.CommitAsync(CancellationToken.None);
            return new(plan.PlanId, deleted, plan.Fingerprint);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch { /* The caller reports an unknown outcome if the connection was lost. */ }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    public static string ComputeFingerprint(ArchivedGarageCleanupPlan plan) => Hash(JsonSerializer.Serialize(new
    {
        plan.FormatVersion,
        plan.PlanId,
        CreatedAtUtc = plan.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        plan.Source,
        Garages = plan.EligibleGarages.OrderBy(row => row.GarageId).Select(row => new
        {
            row.GarageId,
            row.GarageNumber,
            row.GarageVersion,
            Reasons = row.ProtectionReasons.Order(StringComparer.Ordinal),
            row.MeterDeviceCount,
            row.MeterConnectionCount,
            row.PeoplePeriodCount,
            row.QuickListLinkCount
        })
    }, CanonicalJson));

    private void ValidatePlan(ArchivedGarageCleanupPlan plan, string expected)
    {
        if (plan is null || plan.FormatVersion != 1 || plan.PlanId == Guid.Empty || plan.Source is null || plan.EligibleGarages is null ||
            plan.EligibleGarages.Count > 500 || plan.CreatedAtUtc > clock.GetUtcNow() || clock.GetUtcNow() - plan.CreatedAtUtc > TimeSpan.FromHours(24) ||
            string.IsNullOrWhiteSpace(expected) || !string.Equals(plan.Fingerprint, expected.Trim(), StringComparison.Ordinal) ||
            plan.EligibleGarages.Any(row => row is null || row.GarageId == Guid.Empty || row.GarageVersion == Guid.Empty || row.ProtectionReasons is null || row.ProtectionReasons.Count > 0) ||
            plan.EligibleGarages.Select(row => row.GarageId).Distinct().Count() != plan.EligibleGarages.Count ||
            !ComputeFingerprint(plan).Equals(plan.Fingerprint, StringComparison.Ordinal))
            throw Error("cleanup_plan_invalid", "Нужен неизменённый свежий предпросмотр и его точный отпечаток.");
    }

    private IQueryable<CandidateRow> CandidateQuery() => db.Garages.AsNoTracking().Where(garage => garage.IsArchived)
        .Select(garage => new CandidateRow
        {
            Id = garage.Id,
            Number = garage.Number,
            Version = garage.Version,
            HasBalance = garage.StartingBalance != 0m || (garage.StartingOverdueDebt ?? 0m) != 0m,
            HasOperations = db.FinancialOperations.Any(operation => operation.GarageId == garage.Id),
            HasAccruals = db.Accruals.Any(accrual => accrual.GarageId == garage.Id),
            HasOpeningAdjustments = db.OpeningBalanceAdjustments.Any(adjustment => adjustment.TargetKind == OpeningBalanceAdjustmentTargetKinds.Garage && adjustment.TargetId == garage.Id),
            HasReadings = db.MeterReadings.Any(reading => reading.GarageId == garage.Id),
            HasTariffAssignments = db.GarageTariffAssignments.Any(assignment => assignment.GarageId == garage.Id),
            HasFeeLinks = db.FeeCampaignGarages.Any(link => link.GarageId == garage.Id),
            HasServiceScope = db.ChargeServiceSettings.Any(setting => setting.GarageIds.Contains(garage.Id)),
            DeviceCount = db.MeterDevices.Count(device => device.GarageId == garage.Id),
            ConnectionCount = db.GarageMeterConnections.Count(connection => connection.GarageId == garage.Id),
            PeoplePeriodCount = db.GaragePeopleCountPeriods.Count(period => period.GarageId == garage.Id),
            QuickListLinkCount = db.GarageReportQuickListGarages.Count(link => link.GarageId == garage.Id)
        });

    private static IQueryable<CandidateRow> Eligible(IQueryable<CandidateRow> query) => query.Where(row =>
        !row.HasBalance && !row.HasOperations && !row.HasAccruals && !row.HasOpeningAdjustments && !row.HasReadings &&
        !row.HasTariffAssignments && !row.HasFeeLinks && !row.HasServiceScope);

    private static ArchivedGarageCleanupCandidate ToCandidate(CandidateRow row) => new(row.Id, row.Number, row.Version,
        GetProtectionReasons(row), row.DeviceCount, row.ConnectionCount, row.PeoplePeriodCount, row.QuickListLinkCount);

    private static IReadOnlyList<string> GetProtectionReasons(CandidateRow row)
    {
        var reasons = new List<string>();
        if (row.HasBalance) reasons.Add("opening_balance");
        if (row.HasOperations) reasons.Add("payments");
        if (row.HasAccruals) reasons.Add("accruals");
        if (row.HasOpeningAdjustments) reasons.Add("opening_adjustments");
        if (row.HasReadings) reasons.Add("meter_readings");
        if (row.HasTariffAssignments) reasons.Add("individual_tariffs");
        if (row.HasFeeLinks) reasons.Add("fee_campaigns");
        if (row.HasServiceScope) reasons.Add("service_scope");
        return reasons;
    }

    private async Task<AccountingCleanupSource> GetSourceAsync(CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql())
        {
            var tables = db.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys())
                .Where(key => key.PrincipalEntityType.ClrType == typeof(Garage))
                .Select(key => $"public.{key.DeclaringEntityType.GetTableName()}").Order(StringComparer.Ordinal).ToArray();
            if (tables.Length != ExpectedDeleteRules.Count || tables.Distinct(StringComparer.Ordinal).Count() != ExpectedDeleteRules.Count ||
                tables.Any(table => !ExpectedDeleteRules.ContainsKey(table)))
                throw Error("cleanup_schema_unreviewed", "В схеме есть непроверенная связь с гаражом. Удаление запрещено.");
            return new(Environment.MachineName, "sqlite-test", 0, db.Database.GetDbConnection().Database,
                db.Database.GetDbConnection().GetHashCode().ToString(CultureInfo.InvariantCulture), Hash(string.Join('|', tables)));
        }
        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database))
            throw Error("cleanup_source_invalid", "Нужны однозначные узел и имя базы данных.");
        var profile = await db.Database.SqlQuery<DatabaseProfile>($"""
            SELECT oid::bigint AS "DatabaseOid", pg_postmaster_start_time() AS "ServerStartedAt"
            FROM pg_database WHERE datname = current_database()
            """).SingleAsync(cancellationToken);
        var links = await db.Database.SqlQuery<DatabaseGarageLink>($"""
            SELECT namespace.nspname || '.' || relation.relname AS "TableName", constraint_row.confdeltype::text AS "DeleteRule",
                pg_get_constraintdef(constraint_row.oid) AS "Definition",
                array_to_string(ARRAY(SELECT attribute.attname FROM unnest(constraint_row.conkey) WITH ORDINALITY AS key(number, position)
                    JOIN pg_attribute AS attribute ON attribute.attrelid = constraint_row.conrelid AND attribute.attnum = key.number ORDER BY key.position), ',') AS "Columns",
                array_to_string(ARRAY(SELECT attribute.attname FROM unnest(constraint_row.confkey) WITH ORDINALITY AS key(number, position)
                    JOIN pg_attribute AS attribute ON attribute.attrelid = constraint_row.confrelid AND attribute.attnum = key.number ORDER BY key.position), ',') AS "TargetColumns"
            FROM pg_constraint AS constraint_row
            JOIN pg_class AS relation ON relation.oid = constraint_row.conrelid
            JOIN pg_namespace AS namespace ON namespace.oid = relation.relnamespace
            WHERE constraint_row.confrelid = 'garages'::regclass AND constraint_row.contype = 'f'
            """).ToListAsync(cancellationToken);
        if (links.Count != ExpectedDeleteRules.Count || links.Select(link => link.TableName).Distinct(StringComparer.Ordinal).Count() != ExpectedDeleteRules.Count ||
            links.Any(link => !ExpectedDeleteRules.TryGetValue(link.TableName, out var rule) || rule != link.DeleteRule ||
                link.Columns != "GarageId" || link.TargetColumns != "Id"))
            throw Error("cleanup_schema_unreviewed", "В схеме есть непроверенная связь с гаражом. Удаление запрещено.");
        var migrations = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
        var schema = Hash(string.Join('\n', links.OrderBy(link => link.TableName, StringComparer.Ordinal).Select(link => $"{link.TableName}|{link.Definition}")
            .Concat(migrations.Order(StringComparer.Ordinal))));
        return new(Environment.MachineName, connection.Host, connection.Port, connection.Database!,
            $"{profile.DatabaseOid}:{profile.ServerStartedAt.ToUniversalTime():O}", schema);
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static AccountingCleanupException Error(string code, string message) => new(code, message);

    private sealed class CandidateRow
    {
        public Guid Id { get; set; }
        public string Number { get; set; } = "";
        public Guid Version { get; set; }
        public bool HasBalance { get; set; }
        public bool HasOperations { get; set; }
        public bool HasAccruals { get; set; }
        public bool HasOpeningAdjustments { get; set; }
        public bool HasReadings { get; set; }
        public bool HasTariffAssignments { get; set; }
        public bool HasFeeLinks { get; set; }
        public bool HasServiceScope { get; set; }
        public int DeviceCount { get; set; }
        public int ConnectionCount { get; set; }
        public int PeoplePeriodCount { get; set; }
        public int QuickListLinkCount { get; set; }
    }

    private sealed class DatabaseProfile
    {
        public long DatabaseOid { get; set; }
        public DateTimeOffset ServerStartedAt { get; set; }
    }

    private sealed class DatabaseGarageLink
    {
        public string TableName { get; set; } = "";
        public string DeleteRule { get; set; } = "";
        public string Definition { get; set; } = "";
        public string Columns { get; set; } = "";
        public string TargetColumns { get; set; } = "";
    }
}
