namespace GarageBalance.Api.Application.Settings;

public interface IApplicationSettingsService
{
    Task<PaymentDisplaySettingsDto> GetPaymentDisplaySettingsAsync(CancellationToken cancellationToken);
    Task<PaymentDisplaySettingsDto> UpdatePaymentDisplaySettingsAsync(
        UpdatePaymentDisplaySettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<SalaryAccrualSettingsDto> GetSalaryAccrualSettingsAsync(CancellationToken cancellationToken);
    Task<SalaryAccrualSettingsDto> UpdateSalaryAccrualSettingsAsync(
        UpdateSalaryAccrualSettingsRequest request,
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
