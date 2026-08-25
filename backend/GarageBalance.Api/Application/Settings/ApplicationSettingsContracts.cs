using GarageBalance.Api.Application.Finance;

namespace GarageBalance.Api.Application.Settings;

public sealed record PaymentDisplaySettingsDto(
    bool ShowAllGarageOperationsByDefault,
    Guid Version = default,
    bool ShowPeriodicityColumn = false,
    bool ShowAccrualMonthColumn = false,
    Guid TariffTableVersion = default);

public sealed record UpdatePaymentDisplaySettingsRequest(
    bool ShowAllGarageOperationsByDefault,
    Guid? Version = null,
    bool ShowPeriodicityColumn = false,
    bool ShowAccrualMonthColumn = false,
    Guid? TariffTableVersion = null);

public sealed record TariffTableDisplaySettingsDto(
    bool ShowPeriodicityColumn,
    bool ShowAccrualMonthColumn,
    Guid Version = default);

public sealed record UpdateTariffTableDisplaySettingsRequest(
    bool ShowPeriodicityColumn,
    bool ShowAccrualMonthColumn,
    Guid? Version = null);

public sealed record TariffPanelsLayoutDto(
    int IrregularPaymentsWidthPercent,
    Guid Version = default);

public sealed record UpdateTariffPanelsLayoutRequest(
    int IrregularPaymentsWidthPercent,
    Guid? Version = null);

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

public sealed record PreviewBusinessDateRequest(DateOnly? OverrideDate, Guid? Version = null);

public sealed record BusinessDateChangePreviewDto(
    DateOnly SystemDate,
    DateOnly CurrentEffectiveDate,
    DateOnly ProposedEffectiveDate,
    DateOnly? OverrideDate,
    bool IsChange,
    RegularAccrualAutomationPreviewDto Automation,
    Guid Version);
