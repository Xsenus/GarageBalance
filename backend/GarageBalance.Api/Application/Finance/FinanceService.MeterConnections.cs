using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public sealed partial class FinanceService
{
    private sealed record MeterConnectionContext(GarageMeterConnection? Connection, decimal InitialValue);

    // Call under the same chain lock as the reading mutation, so connection edits cannot race it.
    private async Task<FinanceResult<MeterConnectionContext>> ValidateMeterConnectionAsync(
        Garage garage, string meterKind, DateOnly month, DateOnly readingDate,
        bool requireApplicableService, CancellationToken cancellationToken)
    {
        if (garage.AccountingStartMonth is { } accountingStart && month < accountingStart)
            return FinanceResult<MeterConnectionContext>.Failure("meter_reading_before_accounting_start",
                $"Показания гаража учитываются с {accountingStart:MM.yyyy}. Выберите допустимый месяц или исправьте «Учитывать с» в карточке гаража.");
        var connection = await meterReadingRepository.GetConnectionForUpdateAsync(garage.Id, meterKind, cancellationToken);
        if (requireApplicableService &&
            !await chargeServiceSettingRepository.IsRegularMeterKindApplicableAsync(garage.Id, meterKind, cancellationToken))
        {
            return FinanceResult<MeterConnectionContext>.Failure("meter_start_service_not_applicable", "Эта счётчиковая услуга не применяется к гаражу.");
        }

        if (connection is not null)
        {
            if (month < MonthPeriod.Normalize(connection.ConnectedOn) || readingDate < connection.ConnectedOn ||
                connection.ClosedOn is { } closed && (month > MonthPeriod.Normalize(closed) || readingDate > closed))
            {
                return FinanceResult<MeterConnectionContext>.Failure("meter_reading_outside_connection", "Дата и месяц показания должны входить в период подключения услуги.");
            }
            if (connection.BaselineMeterDevice is not { IsCanceled: false } baseline)
            {
                return FinanceResult<MeterConnectionContext>.Failure("meter_start_value_required", "Укажите стартовое показание подключённой услуги в карточке гаража. Ноль можно указать явно.");
            }
            return FinanceResult<MeterConnectionContext>.Success(new(connection, baseline.InitialValue));
        }

        if (IsBeforeMeterBaseline(garage, meterKind, month))
        {
            return FinanceResult<MeterConnectionContext>.Failure("meter_reading_before_baseline", "Показание должно относиться к месяцу после начального показания из карточки гаража.");
        }
        var firstReading = await meterReadingRepository.GetFirstActiveAsync(garage.Id, meterKind, cancellationToken);
        var device = await meterReadingRepository.GetActiveDeviceForUpdateAsync(garage.Id, meterKind, cancellationToken);
        var value = device?.InitialValue ?? firstReading?.PreviousValue ?? GetInitialMeterValue(garage, meterKind);
        if (!value.HasValue)
        {
            return FinanceResult<MeterConnectionContext>.Failure(
                meterKind == MeterKinds.Water ? "water_meter_reading_baseline_required" : "meter_connection_required",
                "Укажите дату подключения и стартовое показание услуги в карточке гаража. Пустое значение не считается нулём.");
        }
        return FinanceResult<MeterConnectionContext>.Success(new(null, value.Value));
    }

    private static bool HasMeterGapWarning(Garage garage, string meterKind, DateOnly month,
        MeterReading? previousReading, GarageMeterConnection? connection) =>
        connection is null
            ? HasGapWarning(garage, meterKind, month, previousReading)
            : meterKind != MeterKinds.Water &&
              (previousReading?.AccountingMonth ?? MonthPeriod.Normalize(connection.ConnectedOn).AddMonths(-1)) < month.AddMonths(-1);
}
