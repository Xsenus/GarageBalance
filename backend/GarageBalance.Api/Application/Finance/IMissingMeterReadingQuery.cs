namespace GarageBalance.Api.Application.Finance;

public interface IMissingMeterReadingQuery
{
    Task<IReadOnlyList<MissingMeterReadingData>> GetMissingAsync(
        DateOnly accountingMonth,
        IReadOnlyList<string> meterKinds,
        string? normalizedSearch,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record MissingMeterReadingData(
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    string MeterKind);
