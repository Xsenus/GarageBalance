namespace GarageBalance.Api.Application.Settings;

public sealed record PaymentDisplaySettingsDto(bool ShowAllGarageOperationsByDefault);

public sealed record UpdatePaymentDisplaySettingsRequest(bool ShowAllGarageOperationsByDefault);
