namespace GarageBalance.Api.Application.Finance;

public interface IGarageIncomeWorksheetQuery
{
    Task<GarageIncomeWorksheetData?> GetAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken);
}

public sealed record GarageIncomeWorksheetData(
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    decimal StartingBalance,
    decimal PreviousAccrualTotal,
    decimal PreviousIncomeTotal,
    IReadOnlyList<GarageIncomeWorksheetBucketData> AccrualBuckets,
    IReadOnlyList<GarageIncomeWorksheetBucketData> IncomeBuckets,
    IReadOnlyList<GarageIncomeWorksheetMeterTypeData> MeterIncomeTypes,
    IReadOnlyList<GarageIncomeWorksheetMeterData> MeterReadings,
    IReadOnlyList<GarageIncomeWorksheetAnnualAccrualData> AnnualAccruals,
    IReadOnlyList<GarageIncomeWorksheetAnnualAllocationData> AnnualAllocations);

public sealed record GarageIncomeWorksheetBucketData(
    DateOnly AccountingMonth,
    Guid IncomeTypeId,
    string IncomeTypeName,
    string? IncomeTypeCode,
    decimal Amount);

public sealed record GarageIncomeWorksheetMeterTypeData(
    Guid IncomeTypeId,
    string IncomeTypeName,
    string IncomeTypeCode);

public sealed record GarageIncomeWorksheetMeterData(
    Guid Id,
    Guid Version,
    DateOnly AccountingMonth,
    string MeterKind,
    DateOnly ReadingDate,
    decimal CurrentValue,
    decimal Consumption,
    DateTimeOffset UpdatedAtUtc);

public sealed record GarageIncomeWorksheetAnnualAccrualData(
    Guid AccrualId,
    DateOnly AccountingMonth,
    int AccountingYear,
    Guid IncomeTypeId,
    string IncomeTypeName,
    string IncomeTypeCode,
    decimal Amount);

public sealed record GarageIncomeWorksheetAnnualAllocationData(
    Guid AccrualId,
    DateOnly AccountingMonth,
    decimal Amount);
