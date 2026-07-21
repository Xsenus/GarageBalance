namespace GarageBalance.Api.Application.Settings;

public sealed record PaymentDisplaySettingsDto(bool ShowAllGarageOperationsByDefault);

public sealed record UpdatePaymentDisplaySettingsRequest(bool ShowAllGarageOperationsByDefault);

public sealed record BusinessDateSettingsDto(
    DateOnly SystemDate,
    DateOnly EffectiveDate,
    DateOnly? OverrideDate,
    bool IsOverrideActive,
    DateTimeOffset? UpdatedAtUtc,
    RegularAccrualAutomationSummaryDto? Automation);

public sealed record RegularAccrualAutomationSummaryDto(
    bool Succeeded,
    int CreatedCount,
    int SkippedCount,
    string Message);

public sealed record UpdateBusinessDateRequest(DateOnly? OverrideDate);
