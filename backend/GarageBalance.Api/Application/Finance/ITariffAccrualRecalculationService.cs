namespace GarageBalance.Api.Application.Finance;

public interface ITariffAccrualRecalculationService
{
    Task RecalculateExistingUnpaidAsync(
        Guid chargeServiceId,
        Guid incomeTypeId,
        DateOnly affectedFrom,
        Guid? actorUserId,
        string reason,
        CancellationToken cancellationToken);
}
