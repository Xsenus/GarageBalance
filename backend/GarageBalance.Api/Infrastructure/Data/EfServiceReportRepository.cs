using System.Data;
using System.Text.Json;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfServiceReportRepository(GarageBalanceDbContext db) : IServiceReportRepository
{
    public const string SettingsKey = "reports.service_columns.payments";
    public static readonly Guid SettingsId = Guid.Parse("96f302bb-d6b0-4e91-aafe-b4713ff788df");
    private static readonly (string Code, string Name)[] Defaults = [("electricity", "Электроэнергия"), ("water", "Вода"), ("trash", "Мусор"), ("outdoor_lighting", "Наружное освещение"), ("membership", "Членский взнос"), ("target", "Целевой взнос")];
    private static readonly Guid OtherId = Guid.Parse("970bf090-583f-46f3-a04f-67e96665b829");

    public async Task<ServiceReportColumnsDto> GetColumnsAsync(CancellationToken cancellationToken, string report = "payments")
    {
        var settingsId = ServiceReportScopes.SettingsId(report);
        var key = $"reports.service_columns.{report}";
        var setting = await db.ApplicationSettings.AsNoTracking().SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        var options = await db.ChargeServiceSettings.AsNoTracking().OrderBy(service => service.Name).Select(service => new
        {
            service.Id,
            service.Name,
            service.IncomeTypeId,
            service.IsArchived,
            Code = service.IncomeType == null ? null : service.IncomeType.Code
        }).Take(2001).ToArrayAsync(cancellationToken);
        if (options.Length > 2000) throw new InvalidOperationException("В справочнике больше 2000 услуг. Уточните конфигурацию отчёта.");
        var columns = setting?.JsonValue is not null ? JsonSerializer.Deserialize<ServiceReportColumn[]>(setting.JsonValue)!
            : Defaults.Select((item, index) => new ServiceReportColumn(new Guid(index + 1, 0, 0, new byte[8]), item.Name, options.Where(option => option.Code == item.Code).Select(option => option.Id).ToArray())).ToArray();
        return new(setting?.Version ?? settingsId, columns, options.Select(option => new ServiceReportServiceOption(option.Id, option.Name, option.IncomeTypeId, option.IsArchived)).ToArray(), report);
    }

    public async Task SaveColumnsAsync(UpdateServiceReportColumnsRequest request, Guid? actorId, CancellationToken cancellationToken)
    {
        var settingsId = ServiceReportScopes.SettingsId(request.Report);
        var key = $"reports.service_columns.{request.Report}";
        var setting = await db.ApplicationSettings.SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (setting is null)
        {
            setting = new ApplicationSetting { Id = settingsId, Key = key, Version = settingsId };
            db.ApplicationSettings.Add(setting);
        }
        OptimisticConcurrencyGuard.EnsureCurrent(request.Version, setting);
        setting.JsonValue = JsonSerializer.Serialize(request.Columns);
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;
        setting.UpdatedByUserId = actorId;
    }

    public async Task<ServiceReportDto> GetPaymentsAsync(ServiceReportRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : null;
        var configuration = await GetColumnsAsync(cancellationToken);
        var payments = db.FinancialOperations.AsNoTracking().Where(operation => operation.OperationKind == FinancialOperationKinds.Income && !operation.IsCanceled
            && operation.GarageId != null && operation.OperationDate <= request.DateTo && (!request.DateFrom.HasValue || operation.OperationDate >= request.DateFrom)
            && (!request.GarageId.HasValue || operation.GarageId == request.GarageId));
        var generic = payments.Where(operation => operation.IncomeTypeId == null && operation.FeeCampaignId == null && operation.IrregularPaymentId == null);
        var classified = payments.Where(operation => operation.IncomeTypeId != null || operation.FeeCampaignId != null || operation.IrregularPaymentId != null)
            .Select(operation => new
            {
                Date = operation.OperationDate,
                GarageId = operation.GarageId!.Value,
                GarageNumber = operation.Garage!.Number,
                IncomeTypeId = operation.FeeCampaignId != null || operation.IrregularPaymentId != null ? null : operation.IncomeTypeId,
                operation.Amount
            })
            .Concat(db.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && !allocation.Accrual.IsCanceled)
                .Join(generic, allocation => allocation.FinancialOperationId, operation => operation.Id, (allocation, operation) => new
                {
                    Date = operation.OperationDate,
                    GarageId = operation.GarageId!.Value,
                    GarageNumber = operation.Garage!.Number,
                    IncomeTypeId = allocation.Accrual.FeeCampaignId != null || allocation.Accrual.IrregularPaymentId != null ? (Guid?)null : allocation.Accrual.IncomeTypeId,
                    allocation.Amount
                }))
            .Concat(generic.Select(operation => new
            {
                Date = operation.OperationDate,
                GarageId = operation.GarageId!.Value,
                GarageNumber = operation.Garage!.Number,
                IncomeTypeId = (Guid?)null,
                Amount = operation.Amount - (db.AccrualPaymentAllocations.Where(allocation => allocation.FinancialOperationId == operation.Id && allocation.IsActive && !allocation.Accrual.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m)
            }));
        if (await classified.AnyAsync(bucket => bucket.Amount < 0m, cancellationToken)) throw new InvalidOperationException("Распределение превышает сумму оплаты.");
        var keys = classified.Where(bucket => bucket.Amount != 0).Select(bucket => new { bucket.Date, bucket.GarageId, bucket.GarageNumber }).Distinct();
        var count = await keys.CountAsync(cancellationToken);
        var pageQuery = keys.OrderByDescending(key => key.Date).ThenBy(key => key.GarageNumber.Length).ThenBy(key => key.GarageNumber).ThenBy(key => key.GarageId)
            .Skip(request.Offset).Take(request.Limit);
        var page = await pageQuery.ToArrayAsync(cancellationToken);
        var incomeIds = configuration.Columns.SelectMany(column => column.ServiceIds).Select(id => configuration.Services.Single(service => service.Id == id).IncomeTypeId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var collapsed = classified.Select(bucket => new
        {
            bucket.Date,
            bucket.GarageId,
            bucket.GarageNumber,
            IncomeTypeId = bucket.IncomeTypeId != null && incomeIds.Contains(bucket.IncomeTypeId.Value) ? bucket.IncomeTypeId : null,
            bucket.Amount
        });
        var totals = await collapsed.GroupBy(bucket => bucket.IncomeTypeId).Select(group => new { Id = group.Key, Amount = group.Sum(bucket => bucket.Amount) }).ToArrayAsync(cancellationToken);
        var hasOther = totals.Any(item => item.Id == null && item.Amount != 0);
        var columns = configuration.Columns.Concat(hasOther ? [new ServiceReportColumn(OtherId, "Прочее", [])] : []).ToArray();
        decimal[] Map(IEnumerable<(Guid? Id, decimal Amount)> buckets) => columns.Select(column => buckets.Where(bucket => column.Id == OtherId ? bucket.Id == null : column.ServiceIds.Any(id => configuration.Services.Any(service => service.Id == id && service.IncomeTypeId == bucket.Id && bucket.Id.HasValue))).Sum(bucket => bucket.Amount)).ToArray();
        var rows = new List<ServiceReportRow>();
        var days = new List<ServiceReportDay>();
        // Two bounded aggregate queries, independent of the number of dates on the page.
        // Joining the paged keys prevents materializing historical rows outside this page.
        var pageBuckets = await collapsed.Join(pageQuery, bucket => new { bucket.Date, bucket.GarageId }, key => new { key.Date, key.GarageId }, (bucket, key) => bucket)
            .GroupBy(bucket => new { bucket.Date, bucket.GarageId, bucket.IncomeTypeId })
            .Select(group => new { group.Key.Date, group.Key.GarageId, Id = group.Key.IncomeTypeId, Amount = group.Sum(bucket => bucket.Amount) }).ToArrayAsync(cancellationToken);
        var dates = page.Select(key => key.Date).Distinct().ToArray();
        var dayBuckets = await collapsed.Where(bucket => dates.Contains(bucket.Date)).GroupBy(bucket => new { bucket.Date, bucket.IncomeTypeId })
            .Select(group => new { group.Key.Date, Id = group.Key.IncomeTypeId, Amount = group.Sum(bucket => bucket.Amount) }).ToArrayAsync(cancellationToken);
        foreach (var key in page) rows.Add(new(key.Date, key.GarageId, key.GarageNumber, Map(pageBuckets.Where(bucket => bucket.Date == key.Date && bucket.GarageId == key.GarageId).Select(bucket => (bucket.Id, bucket.Amount)))));
        foreach (var day in dates) days.Add(new(day, Map(dayBuckets.Where(bucket => bucket.Date == day).Select(bucket => (bucket.Id, bucket.Amount)))));
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(request.DateFrom, request.DateTo!.Value, columns, rows, days, Map(totals.Select(bucket => (bucket.Id, bucket.Amount))), count, request.Offset, request.Limit);
    }

    private sealed class DebtBucket
    {
        public Guid GarageId { get; set; }
        public string GarageNumber { get; set; } = "";
        public Guid? IncomeTypeId { get; set; }
        public decimal Amount { get; set; }
    }

    public async Task<ServiceReportDto> GetDebtAsync(ServiceReportRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : null;
        var configuration = await GetColumnsAsync(cancellationToken, request.OverdueOnly ? "overdue" : "accrued");
        // Windowed credit application is performed in PostgreSQL, oldest debt first.
        // Neither historical payments nor individual accruals are loaded into application memory.
        const string sql = """
            WITH garages AS (
                SELECT g.*, GREATEST(CASE WHEN @overdue THEN COALESCE(g."StartingOverdueDebt", g."StartingBalance") ELSE g."StartingBalance" END,0) AS opening
                FROM garages g WHERE (@garage IS NULL OR g."Id"=@garage)
                AND (g."RegisteredOn" IS NULL OR g."RegisteredOn"<=@date)
            ), paid AS (
                SELECT a."AccrualId" AS id, SUM(a."Amount") AS amount
                FROM accrual_payment_allocations a JOIN financial_operations p ON p."Id"=a."FinancialOperationId"
                WHERE a."IsActive" AND NOT p."IsCanceled" AND p."OperationDate"<=@date AND p."OperationKind"='income'
                GROUP BY a."AccrualId"
            ), accruals AS (
                SELECT a.*, COALESCE(p.amount,0) AS paid FROM accruals a JOIN garages g ON g."Id"=a."GarageId"
                LEFT JOIN paid p ON p.id=a."Id" WHERE NOT a."IsCanceled" AND a."AccountingMonth"<=@date
            ), income AS (
                SELECT p."GarageId" AS id, SUM(p."Amount") AS amount FROM financial_operations p JOIN garages g ON g."Id"=p."GarageId"
                WHERE NOT p."IsCanceled" AND p."OperationKind"='income' AND p."OperationDate"<=@date GROUP BY p."GarageId"
            ), allocated AS (
                SELECT p."GarageId" AS id, SUM(a."Amount") AS amount FROM accrual_payment_allocations a
                JOIN financial_operations p ON p."Id"=a."FinancialOperationId" JOIN public.accruals c ON c."Id"=a."AccrualId" AND NOT c."IsCanceled"
                WHERE a."IsActive" AND NOT p."IsCanceled" AND p."OperationKind"='income' AND p."OperationDate"<=@date GROUP BY p."GarageId"
            ), excess AS (
                SELECT "GarageId" AS id, SUM(GREATEST(paid-"Amount",0)) AS amount FROM accruals GROUP BY "GarageId"
            ), credits AS (
                SELECT g."Id" AS id, GREATEST(COALESCE(i.amount,0)-COALESCE(a.amount,0),0)+GREATEST(-g."StartingBalance",0)+COALESCE(e.amount,0) AS amount
                FROM garages g LEFT JOIN income i ON i.id=g."Id" LEFT JOIN allocated a ON a.id=g."Id" LEFT JOIN excess e ON e.id=g."Id"
            ), debts AS (
                SELECT g."Id" AS garage, g."Id" AS id, NULL::uuid AS service, DATE '0001-01-01' AS month, DATE '0001-01-01' AS due, g.opening AS amount FROM garages g WHERE g.opening>0
                UNION ALL
                SELECT a."GarageId", a."Id", CASE WHEN a."FeeCampaignId" IS NOT NULL OR a."IrregularPaymentId" IS NOT NULL THEN NULL ELSE a."IncomeTypeId" END,
                a."AccountingMonth", a."DueDate", GREATEST(a."Amount"-a.paid,0) FROM accruals a
                WHERE a."Amount">a.paid AND (NOT @overdue OR (NOT a."DueDateNeedsReview" AND a."OverdueFromDate"<=@date))
            ), running AS (
                SELECT d.*, COALESCE(SUM(d.amount) OVER(PARTITION BY garage ORDER BY month,due,id ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0) AS before FROM debts d
            )
            SELECT d.garage AS "GarageId", g."Number" AS "GarageNumber", d.service AS "IncomeTypeId", SUM(GREATEST(d.amount-GREATEST(c.amount-d.before,0),0)) AS "Amount"
            FROM running d JOIN garages g ON g."Id"=d.garage JOIN credits c ON c.id=d.garage
            GROUP BY d.garage,g."Number",d.service HAVING SUM(GREATEST(d.amount-GREATEST(c.amount-d.before,0),0))>0
            """;
        var buckets = db.Database.SqlQueryRaw<DebtBucket>(sql, new NpgsqlParameter("date", request.DateTo!.Value),
            new NpgsqlParameter("overdue", request.OverdueOnly), new NpgsqlParameter("garage", NpgsqlDbType.Uuid) { Value = (object?)request.GarageId ?? DBNull.Value });
        var keys = buckets.Select(bucket => new { bucket.GarageId, bucket.GarageNumber }).Distinct();
        var count = await keys.CountAsync(cancellationToken);
        var page = await keys.OrderBy(key => key.GarageNumber.Length).ThenBy(key => key.GarageNumber).ThenBy(key => key.GarageId).Skip(request.Offset).Take(request.Limit).ToArrayAsync(cancellationToken);
        var ids = page.Select(key => key.GarageId).ToArray();
        var pageBuckets = await buckets.Where(bucket => ids.Contains(bucket.GarageId)).ToArrayAsync(cancellationToken);
        var totals = await buckets.GroupBy(bucket => bucket.IncomeTypeId).Select(group => new { Id = group.Key, Amount = group.Sum(bucket => bucket.Amount) }).ToArrayAsync(cancellationToken);
        var assigned = configuration.Columns.SelectMany(column => column.ServiceIds).Select(id => configuration.Services.Single(service => service.Id == id).IncomeTypeId).Where(id => id.HasValue).ToHashSet();
        var hasOther = totals.Any(bucket => !assigned.Contains(bucket.Id) && bucket.Amount != 0);
        var columns = configuration.Columns.Concat(hasOther ? [new ServiceReportColumn(OtherId, "Прочее", [])] : []).ToArray();
        decimal[] Map(IEnumerable<(Guid? Id, decimal Amount)> values) => columns.Select(column => values.Where(bucket => column.Id == OtherId ? !assigned.Contains(bucket.Id)
            : column.ServiceIds.Any(id => configuration.Services.Any(service => service.Id == id && service.IncomeTypeId == bucket.Id && bucket.Id.HasValue))).Sum(bucket => bucket.Amount)).ToArray();
        var rows = page.Select(key => new ServiceReportRow(null, key.GarageId, key.GarageNumber, Map(pageBuckets.Where(bucket => bucket.GarageId == key.GarageId).Select(bucket => (bucket.IncomeTypeId, bucket.Amount))))).ToArray();
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(null, request.DateTo.Value, columns, rows, [], Map(totals.Select(bucket => (bucket.Id, bucket.Amount))), count, request.Offset, request.Limit);
    }
}
