namespace GarageBalance.Api.Application.Finance;

public interface IRegularAccrualRecalculationService
{
    Task<FinanceResult<RegularAccrualRecalculationPreviewDto>> PreviewRegularAccrualRecalculationAsync(
        PreviewRegularAccrualRecalculationRequest request,
        CancellationToken cancellationToken);

    Task<FinanceResult<RegularAccrualRecalculationPreviewDto>> ApplyRegularAccrualRecalculationAsync(
        ApplyRegularAccrualRecalculationRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
