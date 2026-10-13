using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public sealed partial class FinanceService
{
    private async Task<bool> RequiresConnectionTariffAsync(Guid garageId, Guid serviceId,
        GarageMeterConnection connection, CancellationToken cancellationToken)
    {
        var month = MonthPeriod.Normalize(connection.ConnectedOn);
        if (month == new DateOnly(9999, 12, 1)) return true;
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var setting = (await GetApplicableMeteredSettingsAsync(connection.MeterKind, month, cancellationToken))
            .FirstOrDefault(item => item.Id == serviceId && item.AppliesToGarage(garageId));
        var tariff = setting is null ? null : SelectTariffForMonth(setting, month);
        if (tariff is null) return true;

        var segments = await BuildGarageRegularAccrualSegmentsAsync(garageId, month, setting, tariff, cancellationToken);
        var requiredFrom = connection.ConnectedOn;
        var requiredTo = connection.ClosedOn is { } closed && closed < monthEnd ? closed : monthEnd;
        foreach (var segment in segments.Where(item => item.CalculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity)
                     .OrderBy(item => item.EffectiveFrom))
        {
            if (segment.EffectiveTo < requiredFrom) continue;
            if (segment.EffectiveFrom > requiredFrom) return true;
            if (segment.EffectiveTo >= requiredTo) return false;
            requiredFrom = segment.EffectiveTo.AddDays(1);
        }
        return true;
    }
}
