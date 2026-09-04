namespace GarageBalance.Api.Application.Settings;

public interface IApplicationSettingsService
{
    Task<PaymentDisplaySettingsDto> GetPaymentDisplaySettingsAsync(CancellationToken cancellationToken);
    Task<PaymentDisplaySettingsDto> UpdatePaymentDisplaySettingsAsync(
        UpdatePaymentDisplaySettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<TariffTableDisplaySettingsDto> GetTariffTableDisplaySettingsAsync(CancellationToken cancellationToken);
    Task<TariffTableDisplaySettingsDto> UpdateTariffTableDisplaySettingsAsync(
        UpdateTariffTableDisplaySettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<TariffPanelsLayoutDto> GetTariffPanelsLayoutAsync(Guid userId, CancellationToken cancellationToken);
    Task<TariffPanelsLayoutDto> UpdateTariffPanelsLayoutAsync(
        UpdateTariffPanelsLayoutRequest request,
        Guid userId,
        CancellationToken cancellationToken);
    Task<SalaryAccrualSettingsDto> GetSalaryAccrualSettingsAsync(CancellationToken cancellationToken);
    Task<SalaryAccrualSettingsDto> UpdateSalaryAccrualSettingsAsync(
        UpdateSalaryAccrualSettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<ActionCommentSettingsDto> GetActionCommentSettingsAsync(CancellationToken cancellationToken);
    Task<ActionCommentSettingsDto> UpdateActionCommentSettingsAsync(
        UpdateActionCommentSettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<PayoutMutationSettingsDto> GetPayoutMutationSettingsAsync(CancellationToken cancellationToken);
    Task<PayoutMutationSettingsDto> UpdatePayoutMutationSettingsAsync(
        UpdatePayoutMutationSettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<HistoricalMeterReadingCorrectionSettingsDto> GetHistoricalMeterReadingCorrectionSettingsAsync(CancellationToken cancellationToken);
    Task<HistoricalMeterReadingCorrectionSettingsDto> UpdateHistoricalMeterReadingCorrectionSettingsAsync(
        UpdateHistoricalMeterReadingCorrectionSettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<BusinessDateSettingsDto> GetBusinessDateSettingsAsync(CancellationToken cancellationToken);
    Task<BusinessDateChangePreviewDto> PreviewBusinessDateChangeAsync(
        PreviewBusinessDateRequest request,
        CancellationToken cancellationToken);
    Task<BusinessDateSettingsDto> UpdateBusinessDateSettingsAsync(
        UpdateBusinessDateRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
