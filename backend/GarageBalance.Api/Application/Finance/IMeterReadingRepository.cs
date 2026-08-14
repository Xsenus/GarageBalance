using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public interface IMeterReadingRepository
{
    Task<IReadOnlyList<MeterReading>> GetListAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? meterKind,
        string? normalizedSearch,
        int limit,
        CancellationToken cancellationToken);

    Task<MeterReadingPageData> GetPageAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? meterKind,
        string? normalizedSearch,
        int offset,
        int limit,
        CancellationToken cancellationToken);

    Task<MeterReadingYearPageData> GetYearPageAsync(
        int year,
        string meterKind,
        int offset,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, MeterReading>> GetActiveByGarageIdsAsync(IReadOnlyCollection<Guid> garageIds, string meterKind, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<IReadOnlyList<MeterReading>> GetActiveForGaragePeriodAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        IReadOnlyCollection<string> meterKinds,
        CancellationToken cancellationToken);
    Task<int> CountActiveAsync(DateOnly? monthFrom, DateOnly? monthTo, string? normalizedSearch, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, Guid garageId, string meterKind, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<MeterReading?> GetPreviousActiveAsync(Guid? ignoredId, Guid garageId, string meterKind, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<MeterReading?> GetNextActiveAsync(Guid? ignoredId, Guid garageId, string meterKind, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<IReadOnlyList<MeterReading>> GetActiveFromForUpdateAsync(Guid? ignoredId, Guid garageId, string meterKind, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<MeterReading?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task ReloadForUpdateAsync(MeterReading reading, CancellationToken cancellationToken);
    Task<MeterReading?> GetActiveAsync(Guid garageId, string meterKind, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<IReadOnlyList<MeterDevice>> GetDevicesAsync(Guid garageId, string meterKind, CancellationToken cancellationToken);
    Task<MeterDevice?> GetDeviceForDateForUpdateAsync(Guid garageId, string meterKind, DateOnly date, CancellationToken cancellationToken);
    Task<MeterDevice?> GetActiveDeviceForUpdateAsync(Guid garageId, string meterKind, CancellationToken cancellationToken);
    Task<IReadOnlyList<MeterReading>> GetAllActiveForUpdateAsync(Guid garageId, string meterKind, CancellationToken cancellationToken);
    void Add(MeterReading reading);
    void Add(MeterDevice device);
}

public sealed record MeterReadingPageData(IReadOnlyList<MeterReading> Items, int TotalCount);

public sealed record MeterReadingYearGarageData(Guid Id, string Number);

public sealed record MeterReadingYearValueData(
    Guid Id,
    Guid GarageId,
    DateOnly AccountingMonth,
    decimal CurrentValue,
    Guid Version,
    Guid? MeterDeviceId,
    string? MeterDeviceSerialNumber,
    bool IsMeterReplacement);

public sealed record MeterReadingYearPageData(
    IReadOnlyList<MeterReadingYearGarageData> Garages,
    IReadOnlyList<MeterReadingYearValueData> Readings,
    int TotalCount);
