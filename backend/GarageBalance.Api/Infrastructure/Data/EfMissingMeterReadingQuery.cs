using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfMissingMeterReadingQuery(GarageBalanceDbContext dbContext) : IMissingMeterReadingQuery
{
    public async Task<IReadOnlyList<MissingMeterReadingData>> GetMissingAsync(
        DateOnly accountingMonth,
        IReadOnlyList<string> meterKinds,
        string? normalizedSearch,
        int limit,
        CancellationToken cancellationToken)
    {
        if (meterKinds.Count == 0)
        {
            return [];
        }

        var serviceMeterKinds = meterKinds
            .Where(kind => kind is not MeterKinds.Water and not MeterKinds.Electricity)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (serviceMeterKinds.Length > 0)
        {
            var legacyKinds = meterKinds
                .Where(kind => kind is MeterKinds.Water or MeterKinds.Electricity)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var result = legacyKinds.Length == 0
                ? new List<MissingMeterReadingData>()
                : (await GetMissingAsync(accountingMonth, legacyKinds, normalizedSearch, limit, cancellationToken)).ToList();
            if (result.Count < limit && dbContext.Database.IsNpgsql())
            {
                result.AddRange(await GetMissingServiceMetersPostgreSqlAsync(
                    accountingMonth,
                    serviceMeterKinds,
                    normalizedSearch,
                    limit - result.Count,
                    cancellationToken));
            }
            else
            {
                foreach (var serviceMeterKind in serviceMeterKinds)
                {
                    if (result.Count >= limit)
                    {
                        break;
                    }

                    result.AddRange(await GetMissingServiceMeterAsync(
                        accountingMonth,
                        serviceMeterKind,
                        normalizedSearch,
                        limit - result.Count,
                        cancellationToken));
                }
            }

            return result;
        }

        var includeWater = meterKinds.Contains(MeterKinds.Water, StringComparer.Ordinal);
        var includeElectricity = meterKinds.Contains(MeterKinds.Electricity, StringComparer.Ordinal);
        IReadOnlyList<MissingMeterCandidate> candidates;
        if (dbContext.Database.IsNpgsql())
        {
            candidates = await GetPostgreSqlCandidatesAsync(
                accountingMonth,
                normalizedSearch,
                includeWater,
                includeElectricity,
                limit,
                cancellationToken);
        }
        else
        {
            var garagesQuery = dbContext.Garages.AsNoTracking()
                .Where(garage => !garage.IsArchived)
                .Where(garage =>
                    (includeWater && !dbContext.MeterReadings.Any(reading =>
                        !reading.IsCanceled &&
                        reading.GarageId == garage.Id &&
                        reading.AccountingMonth == accountingMonth &&
                        reading.MeterKind == MeterKinds.Water)) ||
                    (includeElectricity && !dbContext.MeterReadings.Any(reading =>
                        !reading.IsCanceled &&
                        reading.GarageId == garage.Id &&
                        reading.AccountingMonth == accountingMonth &&
                        reading.MeterKind == MeterKinds.Electricity)));
            var candidateQuery = garagesQuery
                .OrderBy(garage => garage.Number)
                .Select(garage => new MissingMeterCandidate(
                    garage.Id,
                    garage.Number,
                    garage.Owner == null ? null : garage.Owner.LastName,
                    garage.Owner == null ? null : garage.Owner.FirstName,
                    garage.Owner == null ? null : garage.Owner.MiddleName,
                    dbContext.MeterReadings.Any(reading =>
                        !reading.IsCanceled &&
                        reading.GarageId == garage.Id &&
                        reading.AccountingMonth == accountingMonth &&
                        reading.MeterKind == MeterKinds.Water),
                    dbContext.MeterReadings.Any(reading =>
                        !reading.IsCanceled &&
                        reading.GarageId == garage.Id &&
                        reading.AccountingMonth == accountingMonth &&
                        reading.MeterKind == MeterKinds.Electricity)));
            candidates = (await candidateQuery.ToListAsync(cancellationToken))
                .Where(candidate => normalizedSearch is null || CandidateMatchesSearch(candidate, normalizedSearch))
                .Take(limit)
                .ToList();
        }

        return candidates
            .SelectMany(candidate => meterKinds
                .Where(meterKind => IsMissing(candidate, meterKind))
                .Select(meterKind => new MissingMeterReadingData(
                    candidate.GarageId,
                    candidate.GarageNumber,
                    BuildOwnerName(candidate),
                    meterKind)))
            .Take(limit)
            .ToList();
    }

    private async Task<IReadOnlyList<MissingMeterReadingData>> GetMissingServiceMeterAsync(
        DateOnly accountingMonth,
        string meterKind,
        string? normalizedSearch,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Garages.AsNoTracking()
            .Where(garage => !garage.IsArchived)
            .Where(garage => !dbContext.MeterReadings.Any(reading =>
                !reading.IsCanceled &&
                reading.GarageId == garage.Id &&
                reading.AccountingMonth == accountingMonth &&
                reading.MeterKind == meterKind));
        if (normalizedSearch is not null)
        {
            query = query.Where(garage =>
                garage.Number.ToLower().Contains(normalizedSearch) ||
                garage.Owner != null && (
                    garage.Owner.LastName.ToLower().Contains(normalizedSearch) ||
                    garage.Owner.FirstName.ToLower().Contains(normalizedSearch) ||
                    garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(normalizedSearch)));
        }

        var rows = await query
            .OrderBy(garage => garage.Number)
            .Take(limit)
            .Select(garage => new
            {
                garage.Id,
                garage.Number,
                OwnerLastName = garage.Owner == null ? null : garage.Owner.LastName,
                OwnerFirstName = garage.Owner == null ? null : garage.Owner.FirstName,
                OwnerMiddleName = garage.Owner == null ? null : garage.Owner.MiddleName
            })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new MissingMeterReadingData(
            row.Id,
            row.Number,
            row.OwnerLastName is null || row.OwnerFirstName is null
                ? null
                : string.Join(' ', new[] { row.OwnerLastName, row.OwnerFirstName, row.OwnerMiddleName }
                    .Where(part => !string.IsNullOrWhiteSpace(part))),
            meterKind)).ToArray();
    }

    private async Task<IReadOnlyList<MissingMeterReadingData>> GetMissingServiceMetersPostgreSqlAsync(
        DateOnly accountingMonth,
        string[] meterKinds,
        string? normalizedSearch,
        int limit,
        CancellationToken cancellationToken)
    {
        var searchPattern = normalizedSearch is null
            ? null
            : PostgresLikeSearch.ContainsPattern(normalizedSearch);
        const string sql = """
            WITH requested_kinds AS MATERIALIZED (
                SELECT requested."MeterKind", requested."Order"
                FROM unnest(@meter_kinds::text[]) WITH ORDINALITY AS requested("MeterKind", "Order")
            ),
            matching_garages AS MATERIALIZED (
                SELECT garage."Id"
                FROM garages AS garage
                WHERE @has_search = FALSE
                   OR garage."Number" ILIKE @search ESCAPE '\'
                UNION
                SELECT garage."Id"
                FROM garages AS garage
                INNER JOIN owners AS owner ON owner."Id" = garage."OwnerId"
                WHERE @has_search = TRUE
                  AND (
                        owner."LastName" ILIKE @search ESCAPE '\'
                     OR owner."FirstName" ILIKE @search ESCAPE '\'
                     OR owner."MiddleName" ILIKE @search ESCAPE '\'
                     OR (owner."LastName" || ' ' || owner."FirstName" || ' ' || COALESCE(owner."MiddleName", '')) ILIKE @search ESCAPE '\'
                  )
            )
            SELECT
                garage."Id" AS "GarageId",
                garage."Number" AS "GarageNumber",
                owner."LastName" AS "OwnerLastName",
                owner."FirstName" AS "OwnerFirstName",
                owner."MiddleName" AS "OwnerMiddleName",
                requested."MeterKind" AS "MeterKind"
            FROM requested_kinds AS requested
            CROSS JOIN garages AS garage
            INNER JOIN matching_garages AS matching ON matching."Id" = garage."Id"
            LEFT JOIN owners AS owner ON owner."Id" = garage."OwnerId"
            WHERE garage."IsArchived" = FALSE
              AND NOT EXISTS (
                    SELECT 1
                    FROM meter_readings AS reading
                    WHERE reading."IsCanceled" = FALSE
                      AND reading."GarageId" = garage."Id"
                      AND reading."AccountingMonth" = @accounting_month
                      AND reading."MeterKind" = requested."MeterKind"
              )
            ORDER BY requested."Order", garage."Number"
            LIMIT @limit
            """;
        var rows = await dbContext.Database.SqlQueryRaw<MissingServiceMeterRow>(
            sql,
            new NpgsqlParameter<string[]>("meter_kinds", meterKinds),
            new NpgsqlParameter<bool>("has_search", searchPattern is not null),
            new NpgsqlParameter<string>("search", searchPattern ?? string.Empty),
            new NpgsqlParameter<DateOnly>("accounting_month", accountingMonth),
            new NpgsqlParameter<int>("limit", limit))
            .ToListAsync(cancellationToken);

        return rows.Select(row => new MissingMeterReadingData(
            row.GarageId,
            row.GarageNumber,
            row.OwnerLastName is null || row.OwnerFirstName is null
                ? null
                : string.Join(' ', new[] { row.OwnerLastName, row.OwnerFirstName, row.OwnerMiddleName }
                    .Where(part => !string.IsNullOrWhiteSpace(part))),
            row.MeterKind)).ToArray();
    }

    private async Task<IReadOnlyList<MissingMeterCandidate>> GetPostgreSqlCandidatesAsync(
        DateOnly accountingMonth,
        string? normalizedSearch,
        bool includeWater,
        bool includeElectricity,
        int limit,
        CancellationToken cancellationToken)
    {
        var searchPattern = normalizedSearch is null
            ? null
            : PostgresLikeSearch.ContainsPattern(normalizedSearch);
        var rows = await dbContext.Database.SqlQuery<MissingMeterCandidateRow>($$"""
            SELECT
                garage."Id" AS "GarageId",
                garage."Number" AS "GarageNumber",
                owner."LastName" AS "OwnerLastName",
                owner."FirstName" AS "OwnerFirstName",
                owner."MiddleName" AS "OwnerMiddleName",
                COALESCE(reading_status."HasWaterReading", FALSE) AS "HasWaterReading",
                COALESCE(reading_status."HasElectricityReading", FALSE) AS "HasElectricityReading"
            FROM garages AS garage
            LEFT JOIN owners AS owner ON owner."Id" = garage."OwnerId"
            LEFT JOIN (
                SELECT
                    reading."GarageId",
                    COUNT(*) FILTER (WHERE reading."MeterKind" = {{MeterKinds.Water}}) > 0 AS "HasWaterReading",
                    COUNT(*) FILTER (WHERE reading."MeterKind" = {{MeterKinds.Electricity}}) > 0 AS "HasElectricityReading"
                FROM meter_readings AS reading
                WHERE reading."IsCanceled" = FALSE
                  AND reading."AccountingMonth" = {{accountingMonth}}
                  AND reading."MeterKind" IN ({{MeterKinds.Water}}, {{MeterKinds.Electricity}})
                GROUP BY reading."GarageId"
            ) AS reading_status ON reading_status."GarageId" = garage."Id"
            WHERE garage."IsArchived" = FALSE
              AND (
                    ({{includeWater}} AND NOT COALESCE(reading_status."HasWaterReading", FALSE))
                 OR ({{includeElectricity}} AND NOT COALESCE(reading_status."HasElectricityReading", FALSE))
              )
              AND (
                    {{searchPattern}}::text IS NULL
                 OR garage."Number" ILIKE {{searchPattern}} ESCAPE '\'
                 OR owner."LastName" ILIKE {{searchPattern}} ESCAPE '\'
                 OR owner."FirstName" ILIKE {{searchPattern}} ESCAPE '\'
                 OR owner."MiddleName" ILIKE {{searchPattern}} ESCAPE '\'
                 OR (owner."LastName" || ' ' || owner."FirstName" || ' ' || COALESCE(owner."MiddleName", '')) ILIKE {{searchPattern}} ESCAPE '\'
              )
            ORDER BY garage."Number"
            LIMIT {{limit}}
            """).ToListAsync(cancellationToken);

        return rows
            .Select(row => new MissingMeterCandidate(
                row.GarageId,
                row.GarageNumber,
                row.OwnerLastName,
                row.OwnerFirstName,
                row.OwnerMiddleName,
                row.HasWaterReading,
                row.HasElectricityReading))
            .ToList();
    }

    private sealed class MissingMeterCandidateRow
    {
        public Guid GarageId { get; set; }
        public string GarageNumber { get; set; } = string.Empty;
        public string? OwnerLastName { get; set; }
        public string? OwnerFirstName { get; set; }
        public string? OwnerMiddleName { get; set; }
        public bool HasWaterReading { get; set; }
        public bool HasElectricityReading { get; set; }
    }

    private sealed class MissingServiceMeterRow
    {
        public Guid GarageId { get; set; }
        public string GarageNumber { get; set; } = string.Empty;
        public string? OwnerLastName { get; set; }
        public string? OwnerFirstName { get; set; }
        public string? OwnerMiddleName { get; set; }
        public string MeterKind { get; set; } = string.Empty;
    }

    private static bool CandidateMatchesSearch(MissingMeterCandidate candidate, string normalizedSearch) =>
        candidate.GarageNumber.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (candidate.OwnerLastName?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (candidate.OwnerFirstName?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (candidate.OwnerMiddleName?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (BuildOwnerName(candidate)?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsMissing(MissingMeterCandidate candidate, string meterKind) =>
        meterKind switch
        {
            MeterKinds.Water => !candidate.HasWaterReading,
            MeterKinds.Electricity => !candidate.HasElectricityReading,
            _ => false
        };

    private static string? BuildOwnerName(MissingMeterCandidate candidate)
    {
        if (candidate.OwnerLastName is null || candidate.OwnerFirstName is null)
        {
            return null;
        }

        return string.Join(' ', new[] { candidate.OwnerLastName, candidate.OwnerFirstName, candidate.OwnerMiddleName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private sealed record MissingMeterCandidate(
        Guid GarageId,
        string GarageNumber,
        string? OwnerLastName,
        string? OwnerFirstName,
        string? OwnerMiddleName,
        bool HasWaterReading,
        bool HasElectricityReading);
}
