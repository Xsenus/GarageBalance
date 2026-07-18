using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFeeReportQuery(GarageBalanceDbContext dbContext) : IFeeReportQuery
{
    private const int AccrualCategory = 1;
    private const int PaymentCategory = 2;

    public async Task<IReadOnlyList<FeeCampaign>> GetActiveCampaignsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.FeeCampaigns.AsNoTracking()
            .Include(campaign => campaign.IncomeType)
            .Where(campaign => !campaign.IsArchived && !campaign.IncomeType.IsArchived)
            .OrderBy(campaign => campaign.StartsOn)
            .ThenBy(campaign => campaign.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IncomeType>> GetActiveIncomeTypesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.IncomeTypes.AsNoTracking()
            .Where(incomeType => !incomeType.IsArchived)
            .OrderBy(incomeType => incomeType.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<FeeReportQueryData> GetFeeDataAsync(
        IReadOnlyList<Guid> incomeTypeIds,
        CancellationToken cancellationToken)
    {
        var accrualQuery = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && incomeTypeIds.Contains(accrual.IncomeTypeId))
            .GroupBy(accrual => new
            {
                accrual.GarageId,
                accrual.Garage.Number,
                OwnerLastName = accrual.Garage.Owner != null ? accrual.Garage.Owner.LastName : null,
                OwnerFirstName = accrual.Garage.Owner != null ? accrual.Garage.Owner.FirstName : null,
                OwnerMiddleName = accrual.Garage.Owner != null ? accrual.Garage.Owner.MiddleName : null,
                accrual.IncomeTypeId
            })
            .Select(group => new
            {
                Category = AccrualCategory,
                GarageId = (Guid?)group.Key.GarageId,
                GarageNumber = (string?)group.Key.Number,
                group.Key.OwnerLastName,
                group.Key.OwnerFirstName,
                group.Key.OwnerMiddleName,
                group.Key.IncomeTypeId,
                Accrued = group.Sum(accrual => accrual.Amount),
                Paid = 0m,
                LastPaymentDate = (DateOnly?)null
            });
        var paymentQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.IncomeTypeId.HasValue &&
                incomeTypeIds.Contains(operation.IncomeTypeId.Value))
            .GroupBy(operation => new
            {
                operation.GarageId,
                GarageNumber = operation.Garage == null ? null : operation.Garage.Number,
                OwnerLastName = operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.LastName,
                OwnerFirstName = operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.FirstName,
                OwnerMiddleName = operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.MiddleName,
                IncomeTypeId = operation.IncomeTypeId!.Value
            })
            .Select(group => new
            {
                Category = PaymentCategory,
                group.Key.GarageId,
                group.Key.GarageNumber,
                group.Key.OwnerLastName,
                group.Key.OwnerFirstName,
                group.Key.OwnerMiddleName,
                group.Key.IncomeTypeId,
                Accrued = 0m,
                Paid = group.Sum(operation => operation.Amount),
                LastPaymentDate = group.Max(operation => (DateOnly?)operation.OperationDate)
            });
        var rows = await accrualQuery
            .Concat(paymentQuery)
            .ToListAsync(cancellationToken);

        var accrualsByGarage = rows
            .Where(row => row.Category == AccrualCategory)
            .Select(row => new FeeAccrualByGarageData(
                row.GarageId!.Value,
                row.GarageNumber!,
                row.OwnerLastName,
                row.OwnerFirstName,
                row.OwnerMiddleName,
                row.IncomeTypeId,
                row.Accrued))
            .ToList();
        var paymentsByGarage = rows
            .Where(row => row.Category == PaymentCategory && row.GarageId.HasValue)
            .Select(row => new FeePaymentByGarageData(
                row.GarageId!.Value,
                row.IncomeTypeId,
                row.Paid,
                row.LastPaymentDate))
            .ToList();
        var accrualTotals = accrualsByGarage
            .GroupBy(row => row.IncomeTypeId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Accrued));
        var collectedTotals = rows
            .Where(row => row.Category == PaymentCategory)
            .GroupBy(row => row.IncomeTypeId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Paid));
        var garagesById = rows
            .Where(row => row.GarageId.HasValue && row.GarageNumber != null)
            .GroupBy(row => row.GarageId!.Value)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var row = group.First();
                    return new FeeGarageIdentityData(
                        row.GarageId!.Value,
                        row.GarageNumber!,
                        row.OwnerLastName,
                        row.OwnerFirstName,
                        row.OwnerMiddleName);
                });

        return new FeeReportQueryData(
            accrualTotals,
            collectedTotals,
            accrualsByGarage,
            paymentsByGarage,
            garagesById);
    }

    public async Task<FeeReportQueryData> GetFeeCampaignDataAsync(
        IReadOnlyList<Guid> feeCampaignIds,
        CancellationToken cancellationToken)
    {
        var accrualsByGarage = await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.FeeCampaignId.HasValue &&
                feeCampaignIds.Contains(accrual.FeeCampaignId.Value))
            .GroupBy(accrual => new
            {
                accrual.GarageId,
                accrual.Garage.Number,
                OwnerLastName = accrual.Garage.Owner != null ? accrual.Garage.Owner.LastName : null,
                OwnerFirstName = accrual.Garage.Owner != null ? accrual.Garage.Owner.FirstName : null,
                OwnerMiddleName = accrual.Garage.Owner != null ? accrual.Garage.Owner.MiddleName : null,
                FeeCampaignId = accrual.FeeCampaignId!.Value
            })
            .Select(group => new FeeAccrualByGarageData(
                group.Key.GarageId,
                group.Key.Number,
                group.Key.OwnerLastName,
                group.Key.OwnerFirstName,
                group.Key.OwnerMiddleName,
                group.Key.FeeCampaignId,
                group.Sum(accrual => accrual.Amount)))
            .ToListAsync(cancellationToken);

        var paymentsByGarage = await dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(allocation =>
                allocation.IsActive &&
                !allocation.Accrual.IsCanceled &&
                allocation.Accrual.FeeCampaignId.HasValue &&
                feeCampaignIds.Contains(allocation.Accrual.FeeCampaignId.Value) &&
                !allocation.FinancialOperation.IsCanceled)
            .GroupBy(allocation => new
            {
                allocation.Accrual.GarageId,
                FeeCampaignId = allocation.Accrual.FeeCampaignId!.Value
            })
            .Select(group => new FeePaymentByGarageData(
                group.Key.GarageId,
                group.Key.FeeCampaignId,
                group.Sum(allocation => allocation.Amount),
                group.Max(allocation => (DateOnly?)allocation.FinancialOperation.OperationDate)))
            .ToListAsync(cancellationToken);

        var garagesById = accrualsByGarage
            .GroupBy(row => row.GarageId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var row = group.First();
                    return new FeeGarageIdentityData(
                        row.GarageId,
                        row.GarageNumber,
                        row.OwnerLastName,
                        row.OwnerFirstName,
                        row.OwnerMiddleName);
                });

        return new FeeReportQueryData(
            accrualsByGarage.GroupBy(row => row.IncomeTypeId).ToDictionary(group => group.Key, group => group.Sum(row => row.Accrued)),
            paymentsByGarage.GroupBy(row => row.IncomeTypeId).ToDictionary(group => group.Key, group => group.Sum(row => row.Paid)),
            accrualsByGarage,
            paymentsByGarage,
            garagesById);
    }

    public async Task<FeeReportPageQueryData> GetFeeReportPageAsync(
        IReadOnlyList<Guid> feeEntryIds,
        bool useFeeCampaigns,
        ReportSort sort,
        int offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return await GetFallbackPageAsync(feeEntryIds, useFeeCampaigns, sort, offset, limit, cancellationToken);
        }

        var sourceCte = useFeeCampaigns
            ? """
              accrual_rows AS (
                  SELECT "GarageId" AS garage_id, "FeeCampaignId" AS fee_id, SUM("Amount") AS accrued
                  FROM accruals
                  WHERE "IsCanceled" = FALSE AND "FeeCampaignId" = ANY(@fee_ids)
                  GROUP BY "GarageId", "FeeCampaignId"
              ), payment_rows AS (
                  SELECT accrual."GarageId" AS garage_id,
                         accrual."FeeCampaignId" AS fee_id,
                         SUM(allocation."Amount") AS paid,
                         MAX(operation."OperationDate") AS last_payment_date
                  FROM accrual_payment_allocations allocation
                  INNER JOIN accruals accrual ON accrual."Id" = allocation."AccrualId"
                  INNER JOIN financial_operations operation ON operation."Id" = allocation."FinancialOperationId"
                  WHERE allocation."IsActive" = TRUE
                    AND accrual."IsCanceled" = FALSE
                    AND operation."IsCanceled" = FALSE
                    AND accrual."FeeCampaignId" = ANY(@fee_ids)
                  GROUP BY accrual."GarageId", accrual."FeeCampaignId"
              ), fee_names AS (
                  SELECT "Id" AS fee_id, "Name" AS fee_name FROM fee_campaigns WHERE "Id" = ANY(@fee_ids)
              )
              """
            : """
              accrual_rows AS (
                  SELECT "GarageId" AS garage_id, "IncomeTypeId" AS fee_id, SUM("Amount") AS accrued
                  FROM accruals
                  WHERE "IsCanceled" = FALSE AND "IncomeTypeId" = ANY(@fee_ids)
                  GROUP BY "GarageId", "IncomeTypeId"
              ), payment_rows AS (
                  SELECT "GarageId" AS garage_id,
                         "IncomeTypeId" AS fee_id,
                         SUM("Amount") AS paid,
                         MAX("OperationDate") AS last_payment_date
                  FROM financial_operations
                  WHERE "IsCanceled" = FALSE
                    AND "OperationKind" = 'income'
                    AND "GarageId" IS NOT NULL
                    AND "IncomeTypeId" = ANY(@fee_ids)
                  GROUP BY "GarageId", "IncomeTypeId"
              ), fee_names AS (
                  SELECT "Id" AS fee_id, "Name" AS fee_name FROM income_types WHERE "Id" = ANY(@fee_ids)
              )
              """;
        var reportRowsCte = $$"""
            WITH {{sourceCte}}, fee_keys AS (
                SELECT garage_id, fee_id FROM accrual_rows
                UNION
                SELECT garage_id, fee_id FROM payment_rows
            ), report_rows AS (
                SELECT garage."Id" AS "GarageId",
                       garage."Number" AS "GarageNumber",
                       owner."LastName" AS "OwnerLastName",
                       owner."FirstName" AS "OwnerFirstName",
                       owner."MiddleName" AS "OwnerMiddleName",
                       fee_keys.fee_id AS "FeeEntryId",
                       fee_names.fee_name AS "FeeName",
                       COALESCE(accrual_rows.accrued, 0) AS "Accrued",
                       COALESCE(payment_rows.paid, 0) AS "Paid",
                       payment_rows.last_payment_date AS "LastPaymentDate",
                       COALESCE(accrual_rows.accrued, 0) - COALESCE(payment_rows.paid, 0) AS "Debt"
                FROM fee_keys
                INNER JOIN garages garage ON garage."Id" = fee_keys.garage_id
                LEFT JOIN owners owner ON owner."Id" = garage."OwnerId"
                INNER JOIN fee_names ON fee_names.fee_id = fee_keys.fee_id
                LEFT JOIN accrual_rows ON accrual_rows.garage_id = fee_keys.garage_id AND accrual_rows.fee_id = fee_keys.fee_id
                LEFT JOIN payment_rows ON payment_rows.garage_id = fee_keys.garage_id AND payment_rows.fee_id = fee_keys.fee_id
            )
            """;
        var sortColumn = sort.Field switch
        {
            "ownerName" => "concat_ws(' ', \"OwnerLastName\", \"OwnerFirstName\", \"OwnerMiddleName\")",
            "feeName" => "\"FeeName\"",
            "accrued" => "\"Accrued\"",
            "paid" => "\"Paid\"",
            "lastPaymentDate" => "\"LastPaymentDate\"",
            "debt" => "\"Debt\"",
            _ => "\"GarageNumber\""
        };
        var direction = sort.Descending ? "DESC" : "ASC";
        var limitClause = limit is > 0 ? "LIMIT @limit" : string.Empty;
        var orderClause = $"ORDER BY {sortColumn} {direction}, \"FeeName\" ASC, \"GarageId\" ASC OFFSET @offset {limitClause}";
        var garageSql = string.Concat(reportRowsCte, " SELECT * FROM report_rows ", orderClause);
        var debtorSql = string.Concat(reportRowsCte, " SELECT * FROM report_rows WHERE \"Debt\" > 0 ", orderClause);
        var totalsSql = string.Concat(
            reportRowsCte,
            " SELECT COUNT(*)::int AS \"GarageRowCount\", COALESCE(SUM(\"Debt\") FILTER (WHERE \"Debt\" > 0), 0) AS \"DebtTotal\" FROM report_rows");
        var parameters = CreatePageParameters(feeEntryIds, offset, limit);
        var garageRows = await dbContext.Database
            .SqlQueryRaw<FeeReportGarageQueryRow>(garageSql, parameters)
            .ToListAsync(cancellationToken);
        var debtorRows = await dbContext.Database
            .SqlQueryRaw<FeeReportGarageQueryRow>(debtorSql, CreatePageParameters(feeEntryIds, offset, limit))
            .ToListAsync(cancellationToken);
        var pageTotals = await dbContext.Database
            .SqlQueryRaw<FeeReportPageTotalsQueryRow>(
                totalsSql,
                new NpgsqlParameter<Guid[]>("fee_ids", feeEntryIds.ToArray()))
            .SingleAsync(cancellationToken);

        var summarySql = useFeeCampaigns
            ? """
              SELECT fee_id AS "FeeEntryId", SUM(accrued) AS "Accrued", SUM(paid) AS "Paid"
              FROM (
                  SELECT "FeeCampaignId" AS fee_id, SUM("Amount") AS accrued, 0::numeric AS paid
                  FROM accruals
                  WHERE "IsCanceled" = FALSE AND "FeeCampaignId" = ANY(@fee_ids)
                  GROUP BY "FeeCampaignId"
                  UNION ALL
                  SELECT accrual."FeeCampaignId" AS fee_id, 0::numeric AS accrued, SUM(allocation."Amount") AS paid
                  FROM accrual_payment_allocations allocation
                  INNER JOIN accruals accrual ON accrual."Id" = allocation."AccrualId"
                  INNER JOIN financial_operations operation ON operation."Id" = allocation."FinancialOperationId"
                  WHERE allocation."IsActive" = TRUE AND accrual."IsCanceled" = FALSE AND operation."IsCanceled" = FALSE
                    AND accrual."FeeCampaignId" = ANY(@fee_ids)
                  GROUP BY accrual."FeeCampaignId"
              ) totals
              GROUP BY fee_id
              """
            : """
              SELECT fee_id AS "FeeEntryId", SUM(accrued) AS "Accrued", SUM(paid) AS "Paid"
              FROM (
                  SELECT "IncomeTypeId" AS fee_id, SUM("Amount") AS accrued, 0::numeric AS paid
                  FROM accruals
                  WHERE "IsCanceled" = FALSE AND "IncomeTypeId" = ANY(@fee_ids)
                  GROUP BY "IncomeTypeId"
                  UNION ALL
                  SELECT "IncomeTypeId" AS fee_id, 0::numeric AS accrued, SUM("Amount") AS paid
                  FROM financial_operations
                  WHERE "IsCanceled" = FALSE AND "OperationKind" = 'income' AND "IncomeTypeId" = ANY(@fee_ids)
                  GROUP BY "IncomeTypeId"
              ) totals
              GROUP BY fee_id
              """;
        var summaryRows = await dbContext.Database
            .SqlQueryRaw<FeeReportSummaryQueryRow>(summarySql, new NpgsqlParameter<Guid[]>("fee_ids", feeEntryIds.ToArray()))
            .ToListAsync(cancellationToken);
        return new FeeReportPageQueryData(
            summaryRows.ToDictionary(row => row.FeeEntryId, row => row.Accrued),
            summaryRows.ToDictionary(row => row.FeeEntryId, row => row.Paid),
            garageRows,
            debtorRows,
            pageTotals.GarageRowCount,
            pageTotals.DebtTotal);
    }

    private async Task<FeeReportPageQueryData> GetFallbackPageAsync(
        IReadOnlyList<Guid> feeEntryIds,
        bool useFeeCampaigns,
        ReportSort sort,
        int offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        var data = useFeeCampaigns
            ? await GetFeeCampaignDataAsync(feeEntryIds, cancellationToken)
            : await GetFeeDataAsync(feeEntryIds, cancellationToken);
        var names = useFeeCampaigns
            ? await dbContext.FeeCampaigns.AsNoTracking().Where(row => feeEntryIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken)
            : await dbContext.IncomeTypes.AsNoTracking().Where(row => feeEntryIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        var accrualLookup = data.AccrualsByGarage.ToDictionary(row => (row.GarageId, row.IncomeTypeId));
        var paymentLookup = data.PaymentsByGarage.ToDictionary(row => (row.GarageId, row.IncomeTypeId));
        var rows = accrualLookup.Keys.Concat(paymentLookup.Keys).Distinct().Select(key =>
        {
            accrualLookup.TryGetValue(key, out var accrual);
            paymentLookup.TryGetValue(key, out var payment);
            data.GaragesById.TryGetValue(key.GarageId, out var garage);
            var accrued = accrual?.Accrued ?? 0m;
            var paid = payment?.Paid ?? 0m;
            return new FeeReportGarageQueryRow(
                key.GarageId,
                accrual?.GarageNumber ?? garage?.GarageNumber ?? string.Empty,
                accrual?.OwnerLastName ?? garage?.OwnerLastName,
                accrual?.OwnerFirstName ?? garage?.OwnerFirstName,
                accrual?.OwnerMiddleName ?? garage?.OwnerMiddleName,
                key.IncomeTypeId,
                names[key.IncomeTypeId],
                accrued,
                paid,
                payment?.LastPaymentDate,
                accrued - paid);
        }).ToList();
        var garageRows = ApplySort(rows, sort).ThenBy(row => row.FeeName).ThenBy(row => row.GarageId);
        var debtorRows = ApplySort(rows.Where(row => row.Debt > 0), sort).ThenBy(row => row.FeeName).ThenBy(row => row.GarageId);
        return new FeeReportPageQueryData(
            data.AccrualTotals,
            data.CollectedTotals,
            ApplyPage(garageRows, offset, limit).ToList(),
            ApplyPage(debtorRows, offset, limit).ToList(),
            rows.Count,
            rows.Where(row => row.Debt > 0).Sum(row => row.Debt));
    }

    private static object[] CreatePageParameters(IReadOnlyList<Guid> feeEntryIds, int offset, int? limit)
    {
        var parameters = new List<object>
        {
            new NpgsqlParameter<Guid[]>("fee_ids", feeEntryIds.ToArray()),
            new NpgsqlParameter<int>("offset", offset)
        };
        if (limit is > 0)
        {
            parameters.Add(new NpgsqlParameter<int>("limit", limit.Value));
        }

        return parameters.ToArray();
    }

    private static IOrderedEnumerable<FeeReportGarageQueryRow> ApplySort(IEnumerable<FeeReportGarageQueryRow> rows, ReportSort sort) =>
        sort.Field switch
        {
            "ownerName" => sort.Descending
                ? rows.OrderByDescending(row => $"{row.OwnerLastName} {row.OwnerFirstName} {row.OwnerMiddleName}", StringComparer.Ordinal)
                : rows.OrderBy(row => $"{row.OwnerLastName} {row.OwnerFirstName} {row.OwnerMiddleName}", StringComparer.Ordinal),
            "feeName" => sort.Descending ? rows.OrderByDescending(row => row.FeeName, StringComparer.Ordinal) : rows.OrderBy(row => row.FeeName, StringComparer.Ordinal),
            "accrued" => sort.Descending ? rows.OrderByDescending(row => row.Accrued) : rows.OrderBy(row => row.Accrued),
            "paid" => sort.Descending ? rows.OrderByDescending(row => row.Paid) : rows.OrderBy(row => row.Paid),
            "lastPaymentDate" => sort.Descending ? rows.OrderByDescending(row => row.LastPaymentDate) : rows.OrderBy(row => row.LastPaymentDate),
            "debt" => sort.Descending ? rows.OrderByDescending(row => row.Debt) : rows.OrderBy(row => row.Debt),
            _ => sort.Descending ? rows.OrderByDescending(row => row.GarageNumber, StringComparer.Ordinal) : rows.OrderBy(row => row.GarageNumber, StringComparer.Ordinal)
        };

    private static IEnumerable<T> ApplyPage<T>(IEnumerable<T> rows, int offset, int? limit)
    {
        var page = offset > 0 ? rows.Skip(offset) : rows;
        return limit is > 0 ? page.Take(limit.Value) : page;
    }

    private sealed record FeeReportSummaryQueryRow(Guid FeeEntryId, decimal Accrued, decimal Paid);
    private sealed record FeeReportPageTotalsQueryRow(int GarageRowCount, decimal DebtTotal);
}
