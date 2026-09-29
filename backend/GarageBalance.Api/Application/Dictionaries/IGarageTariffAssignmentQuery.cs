using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IGarageTariffAssignmentQuery
{
    Task<IReadOnlyList<GarageTariffAssignment>> GetApplicableAsync(
        Guid garageId, Guid chargeServiceSettingId, DateOnly accountingMonth, CancellationToken cancellationToken);

    Task<IReadOnlyList<GarageTariffAssignment>> GetApplicableForGaragesAsync(
        IReadOnlyCollection<Guid> garageIds, Guid chargeServiceSettingId, DateOnly accountingMonth, CancellationToken cancellationToken);

    Task<IReadOnlyList<GarageTariffAssignment>> GetForGaragePeriodAsync(
        Guid garageId, DateOnly monthFrom, DateOnly monthTo, CancellationToken cancellationToken);
}
