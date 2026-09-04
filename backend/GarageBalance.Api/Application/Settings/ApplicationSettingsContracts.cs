using GarageBalance.Api.Application.Finance;

namespace GarageBalance.Api.Application.Settings;

public sealed record PaymentDisplaySettingsDto(
    bool ShowAllGarageOperationsByDefault,
    Guid Version = default,
    bool ShowPeriodicityColumn = false,
    bool ShowAccrualMonthColumn = false,
    Guid TariffTableVersion = default,
    bool ShowFundName = false,
    string AccrualReasonDisplayMode = AccrualReasonDisplayModes.PenaltiesOnly,
    Guid AccrualReasonDisplayVersion = default);

public sealed record UpdatePaymentDisplaySettingsRequest(
    bool ShowAllGarageOperationsByDefault,
    Guid? Version = null,
    bool ShowPeriodicityColumn = false,
    bool ShowAccrualMonthColumn = false,
    Guid? TariffTableVersion = null,
    bool ShowFundName = false,
    string AccrualReasonDisplayMode = AccrualReasonDisplayModes.PenaltiesOnly,
    Guid? AccrualReasonDisplayVersion = null);

public static class AccrualReasonDisplayModes
{
    public const string PenaltiesOnly = "penalties_only";
    public const string All = "all";
    public const string Hidden = "hidden";

    public static bool IsValid(string? value) =>
        value is PenaltiesOnly or All or Hidden;
}

public sealed record TariffTableDisplaySettingsDto(
    bool ShowPeriodicityColumn,
    bool ShowAccrualMonthColumn,
    Guid Version = default,
    bool ShowFundName = false);

public sealed record UpdateTariffTableDisplaySettingsRequest(
    bool ShowPeriodicityColumn,
    bool ShowAccrualMonthColumn,
    Guid? Version = null,
    bool ShowFundName = false);

public sealed record TariffPanelsLayoutDto(
    int IrregularPaymentsWidthPercent,
    Guid Version = default);

public sealed record UpdateTariffPanelsLayoutRequest(
    int IrregularPaymentsWidthPercent,
    Guid? Version = null);

public sealed record SalaryAccrualSettingsDto(int AccrualDay, Guid Version = default);

public sealed record UpdateSalaryAccrualSettingsRequest(int AccrualDay, Guid? Version = null);

public sealed record ActionCommentSettingsDto(bool Required, Guid Version = default);

public sealed record UpdateActionCommentSettingsRequest(bool Required, Guid? Version = null);

public sealed record PayoutMutationSettingsDto(
    bool EditEnabled,
    bool DeleteEnabled,
    Guid Version = default);

public sealed record UpdatePayoutMutationSettingsRequest(
    bool EditEnabled,
    bool DeleteEnabled,
    Guid? Version = null);

public sealed record HistoricalMeterReadingCorrectionSettingsDto(bool Enabled, Guid Version = default);

public sealed record UpdateHistoricalMeterReadingCorrectionSettingsRequest(bool Enabled, Guid? Version = null);

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
