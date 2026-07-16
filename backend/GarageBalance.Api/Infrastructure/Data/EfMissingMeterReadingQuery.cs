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

        if (normalizedSearch is not null && !IsSqliteProvider())
        {
            garagesQuery = ApplySearch(garagesQuery, normalizedSearch);
        }

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

        IReadOnlyList<MissingMeterCandidate> candidates;
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            candidates = (await candidateQuery.ToListAsync(cancellationToken))
                .Where(candidate => CandidateMatchesSearch(candidate, normalizedSearch))
                .Take(limit)
                .ToList();
        }
        else
        {
            candidates = await candidateQuery
                .Take(limit)
                .ToListAsync(cancellationToken);
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

    private static IQueryable<Garage> ApplySearch(IQueryable<Garage> query, string normalizedSearch) =>
        query.Where(garage =>
            garage.Number.ToLower().Contains(normalizedSearch) ||
            (garage.Owner != null && (
                garage.Owner.LastName.ToLower().Contains(normalizedSearch) ||
                garage.Owner.FirstName.ToLower().Contains(normalizedSearch) ||
                (garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(normalizedSearch)) ||
                (garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedSearch))));

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

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private sealed record MissingMeterCandidate(
        Guid GarageId,
        string GarageNumber,
        string? OwnerLastName,
        string? OwnerFirstName,
        string? OwnerMiddleName,
        bool HasWaterReading,
        bool HasElectricityReading);
}
