using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

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

    private async Task<IReadOnlyList<MissingMeterCandidate>> GetPostgreSqlCandidatesAsync(
        DateOnly accountingMonth,
        string? normalizedSearch,
        bool includeWater,
        bool includeElectricity,
        int limit,
        CancellationToken cancellationToken)
    {
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
                    {{normalizedSearch}}::text IS NULL
                 OR STRPOS(LOWER(garage."Number"), {{normalizedSearch}}) > 0
                 OR STRPOS(LOWER(owner."LastName"), {{normalizedSearch}}) > 0
                 OR STRPOS(LOWER(owner."FirstName"), {{normalizedSearch}}) > 0
                 OR STRPOS(LOWER(COALESCE(owner."MiddleName", '')), {{normalizedSearch}}) > 0
                 OR STRPOS(LOWER(CONCAT_WS(' ', owner."LastName", owner."FirstName", owner."MiddleName")), {{normalizedSearch}}) > 0
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
