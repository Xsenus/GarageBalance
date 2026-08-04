namespace GarageBalance.Api.Application.Settings;

public sealed record PaymentDisplaySettingsDto(bool ShowAllGarageOperationsByDefault, Guid Version = default);

public sealed record UpdatePaymentDisplaySettingsRequest(bool ShowAllGarageOperationsByDefault, Guid? Version = null);

public sealed record SalaryAccrualSettingsDto(int AccrualDay, Guid Version = default);

public sealed record UpdateSalaryAccrualSettingsRequest(int AccrualDay, Guid? Version = null);

public sealed record BusinessDateSettingsDto(
    DateOnly SystemDate,
    DateOnly EffectiveDate,
    DateOnly? OverrideDate,
    bool IsOverrideActive,
    DateTimeOffset? UpdatedAtUtc,
    RegularAccrualAutomationSummaryDto? Automation,
    Guid Version = default);

public sealed record RegularAccrualAutomationSummaryDto(
    bool Succeeded,
    int CreatedCount,
    int SkippedCount,
    string Message);

public sealed record UpdateBusinessDateRequest(DateOnly? OverrideDate, Guid? Version = null);
