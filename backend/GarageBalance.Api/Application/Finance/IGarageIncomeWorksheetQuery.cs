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
    IReadOnlyList<GarageIncomeWorksheetAllocationData> Allocations,
    IReadOnlyList<GarageIncomeWorksheetAdvanceData> Advances,
    IReadOnlyList<GarageIncomeWorksheetCalculationData> Calculations = null!,
    IReadOnlyList<GarageIncomeWorksheetReasonData> Reasons = null!);

public sealed record GarageIncomeWorksheetBucketData(
    DateOnly AccountingMonth,
    Guid IncomeTypeId,
    string IncomeTypeName,
    string? IncomeTypeCode,
    decimal Amount,
    Guid? IrregularPaymentId = null,
    bool IrregularPaymentIsAvailable = true);

public sealed record GarageIncomeWorksheetMeterTypeData(
    Guid IncomeTypeId,
    string IncomeTypeName,
    string IncomeTypeCode,
    string MeterKind);

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

public sealed record GarageIncomeWorksheetAllocationData(
    Guid AccrualId,
    DateOnly AccrualAccountingMonth,
    Guid IncomeTypeId,
    string IncomeTypeName,
    DateOnly PaymentAccountingMonth,
    decimal Amount,
    Guid? IrregularPaymentId = null);

public sealed record GarageIncomeWorksheetAdvanceData(
    Guid IncomeTypeId,
    decimal Amount);

public sealed record GarageIncomeWorksheetCalculationData(
    DateOnly AccountingMonth,
    Guid IncomeTypeId,
    string IncomeTypeName,
    string CalculationDetailsJson);

public sealed record GarageIncomeWorksheetReasonData(
    DateOnly AccountingMonth,
    Guid IncomeTypeId,
    string IncomeTypeName,
    string Reason);
