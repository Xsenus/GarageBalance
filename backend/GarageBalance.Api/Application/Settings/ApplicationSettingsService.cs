using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Settings;

namespace GarageBalance.Api.Application.Settings;

public sealed class ApplicationSettingsService(
    IApplicationSettingRepository repository,
    IAuditEventWriter auditEventWriter,
    IBusinessDateProvider businessDateProvider,
    IRegularAccrualAutomationRunner regularAccrualAutomationRunner,
    TimeProvider timeProvider,
    ILogger<ApplicationSettingsService> logger) : IApplicationSettingsService
{
    public const string ShowAllGarageOperationsKey = "payments.show_all_garage_operations_by_default";
    public const string AccrualReasonDisplayModeKey = "payments.accrual_reason_display_mode";
    public const string TariffTableVisibleColumnsKey = "tariffs.table_visible_columns";
    public const int DefaultTariffPanelsSplitPercent = 40;
    public const int MinimumTariffPanelsSplitPercent = 25;
    public const int MaximumTariffPanelsSplitPercent = 60;
    public const string SalaryAccrualDayKey = "finance.salary_accrual_day";
    public const int DefaultSalaryAccrualDay = 1;
    public const string ActionCommentsRequiredKey = "system.action_comments_required";
    public const string HistoricalMeterReadingCorrectionEnabledKey = "meter_readings.historical_correction_enabled";
    public const string BusinessDateOverrideKey = "system.business_date_override";

    public async Task<PaymentDisplaySettingsDto> GetPaymentDisplaySettingsAsync(CancellationToken cancellationToken)
    {
        var setting = await repository.FindAsync(ShowAllGarageOperationsKey, cancellationToken);
        var reasonSetting = await repository.FindAsync(AccrualReasonDisplayModeKey, cancellationToken);
        return new PaymentDisplaySettingsDto(
            setting?.BooleanValue ?? false,
            setting?.Version ?? Guid.NewGuid(),
            AccrualReasonDisplayMode: CreateAccrualReasonDisplayMode(reasonSetting?.IntegerValue),
            AccrualReasonDisplayVersion: reasonSetting?.Version ?? Guid.NewGuid());
    }

    public async Task<PaymentDisplaySettingsDto> UpdatePaymentDisplaySettingsAsync(
        UpdatePaymentDisplaySettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (!AccrualReasonDisplayModes.IsValid(request.AccrualReasonDisplayMode))
        {
            throw new AccrualReasonDisplaySettingsValidationException(
                "Режим показа причин начислений должен быть: только у штрафов, у всех начислений или не показывать.");
        }

        var setting = await repository.FindForUpdateAsync(ShowAllGarageOperationsKey, cancellationToken);
        var reasonSetting = await repository.FindForUpdateAsync(AccrualReasonDisplayModeKey, cancellationToken);
        var previousValue = setting?.BooleanValue ?? false;
        var previousReasonMode = CreateAccrualReasonDisplayMode(reasonSetting?.IntegerValue);
        var nextReasonValue = CreateAccrualReasonDisplayValue(request.AccrualReasonDisplayMode);
        if (setting is not null && request.Version.HasValue)
        {
            OptimisticConcurrencyGuard.EnsureCurrent(request.Version, setting);
        }
        if (reasonSetting is not null)
        {
            OptimisticConcurrencyGuard.EnsureCurrent(request.AccrualReasonDisplayVersion, reasonSetting);
        }

        var paymentChanged = setting is null
            ? request.ShowAllGarageOperationsByDefault
            : setting.BooleanValue != request.ShowAllGarageOperationsByDefault;
        var reasonChanged = reasonSetting is null
            ? nextReasonValue != 0
            : reasonSetting.IntegerValue != nextReasonValue;

        if (!paymentChanged && !reasonChanged)
        {
            return new PaymentDisplaySettingsDto(
                previousValue,
                setting?.Version ?? request.Version ?? Guid.NewGuid(),
                AccrualReasonDisplayMode: previousReasonMode,
                AccrualReasonDisplayVersion: reasonSetting?.Version ?? request.AccrualReasonDisplayVersion ?? Guid.NewGuid());
        }

        if (paymentChanged)
        {
            if (setting is null)
            {
                setting = new ApplicationSetting { Key = ShowAllGarageOperationsKey };
                repository.Add(setting);
            }

            setting.BooleanValue = request.ShowAllGarageOperationsByDefault;
            setting.UpdatedAtUtc = timeProvider.GetUtcNow();
            setting.UpdatedByUserId = actorUserId;

            auditEventWriter.Add(new AuditEventWriteRequest(
                actorUserId,
                "application_setting.updated",
                "application_setting",
                ShowAllGarageOperationsKey,
                Summary: request.ShowAllGarageOperationsByDefault
                    ? "Включен показ общей ведомости платежей при открытии раздела."
                    : "Отключен показ общей ведомости платежей при открытии раздела.",
                Section: "settings",
                ActionKind: "update",
                EntityDisplayName: "Отображение платежей",
                OldValues: new Dictionary<string, object?> { ["showAllGarageOperationsByDefault"] = previousValue },
                NewValues: new Dictionary<string, object?> { ["showAllGarageOperationsByDefault"] = request.ShowAllGarageOperationsByDefault },
                FieldLabels: new Dictionary<string, string> { ["showAllGarageOperationsByDefault"] = "Показывать общую ведомость платежей" }));
        }

        if (reasonChanged)
        {
            if (reasonSetting is null)
            {
                reasonSetting = new ApplicationSetting { Key = AccrualReasonDisplayModeKey };
                repository.Add(reasonSetting);
            }

            reasonSetting.IntegerValue = nextReasonValue;
            reasonSetting.UpdatedAtUtc = timeProvider.GetUtcNow();
            reasonSetting.UpdatedByUserId = actorUserId;

            auditEventWriter.Add(new AuditEventWriteRequest(
                actorUserId,
                "application_setting.accrual_reason_display_updated",
                "application_setting",
                AccrualReasonDisplayModeKey,
                Summary: $"Изменён показ причин начислений: {GetAccrualReasonDisplayLabel(request.AccrualReasonDisplayMode)}.",
                Section: "settings",
                ActionKind: "update",
                EntityDisplayName: "Показ причин начислений",
                OldValues: new Dictionary<string, object?> { ["accrualReasonDisplayMode"] = previousReasonMode },
                NewValues: new Dictionary<string, object?> { ["accrualReasonDisplayMode"] = request.AccrualReasonDisplayMode },
                FieldLabels: new Dictionary<string, string> { ["accrualReasonDisplayMode"] = "Показывать причины начислений" }));
        }

        await repository.SaveChangesAsync(cancellationToken);
        return new PaymentDisplaySettingsDto(
            setting?.BooleanValue ?? false,
            setting?.Version ?? request.Version ?? Guid.NewGuid(),
            AccrualReasonDisplayMode: request.AccrualReasonDisplayMode,
            AccrualReasonDisplayVersion: reasonSetting?.Version ?? request.AccrualReasonDisplayVersion ?? Guid.NewGuid());
    }

    public async Task<TariffTableDisplaySettingsDto> GetTariffTableDisplaySettingsAsync(CancellationToken cancellationToken)
    {
        var setting = await repository.FindAsync(TariffTableVisibleColumnsKey, cancellationToken);
        return CreateTariffTableDisplaySettingsDto(setting?.IntegerValue ?? 0, setting?.Version ?? Guid.NewGuid());
    }

    public async Task<TariffTableDisplaySettingsDto> UpdateTariffTableDisplaySettingsAsync(
        UpdateTariffTableDisplaySettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var setting = await repository.FindForUpdateAsync(TariffTableVisibleColumnsKey, cancellationToken);
        var previousMask = setting?.IntegerValue ?? 0;
        var nextMask = (request.ShowPeriodicityColumn ? 1 : 0)
            | (request.ShowAccrualMonthColumn ? 2 : 0)
            | (request.ShowFundName ? 4 : 0);
        if (setting is not null)
        {
            OptimisticConcurrencyGuard.EnsureCurrent(request.Version, setting);
        }

        if (setting is null && nextMask == 0)
        {
            return CreateTariffTableDisplaySettingsDto(0, request.Version ?? Guid.NewGuid());
        }

        if (setting is null)
        {
            setting = new ApplicationSetting { Key = TariffTableVisibleColumnsKey };
            repository.Add(setting);
        }
        else if (previousMask == nextMask)
        {
            return CreateTariffTableDisplaySettingsDto(nextMask, setting.Version);
        }

        setting.IntegerValue = nextMask;
        setting.UpdatedAtUtc = timeProvider.GetUtcNow();
        setting.UpdatedByUserId = actorUserId;

        var previous = CreateTariffTableDisplaySettingsDto(previousMask, setting.Version);
        var next = CreateTariffTableDisplaySettingsDto(nextMask, setting.Version);
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "application_setting.tariff_table_columns_updated",
            "application_setting",
            TariffTableVisibleColumnsKey,
            Summary: "Изменены параметры отображения таблицы тарифов.",
            Section: "settings",
            ActionKind: "update",
            EntityDisplayName: "Отображение таблицы тарифов",
            OldValues: new Dictionary<string, object?>
            {
                ["showPeriodicityColumn"] = previous.ShowPeriodicityColumn,
                ["showAccrualMonthColumn"] = previous.ShowAccrualMonthColumn,
                ["showFundName"] = previous.ShowFundName
            },
            NewValues: new Dictionary<string, object?>
            {
                ["showPeriodicityColumn"] = next.ShowPeriodicityColumn,
                ["showAccrualMonthColumn"] = next.ShowAccrualMonthColumn,
                ["showFundName"] = next.ShowFundName
            },
            FieldLabels: new Dictionary<string, string>
            {
                ["showPeriodicityColumn"] = "Показывать периодичность",
                ["showAccrualMonthColumn"] = "Показывать месяц начисления",
                ["showFundName"] = "Показывать название фонда"
            }));

        await repository.SaveChangesAsync(cancellationToken);
        return CreateTariffTableDisplaySettingsDto(nextMask, setting.Version);
    }

    public async Task<TariffPanelsLayoutDto> GetTariffPanelsLayoutAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var setting = await repository.FindAsync(CreateTariffPanelsLayoutKey(userId), cancellationToken);
        return CreateTariffPanelsLayoutDto(setting?.IntegerValue, setting?.Version ?? Guid.NewGuid());
    }

    public async Task<TariffPanelsLayoutDto> UpdateTariffPanelsLayoutAsync(
        UpdateTariffPanelsLayoutRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (request.IrregularPaymentsWidthPercent is < MinimumTariffPanelsSplitPercent or > MaximumTariffPanelsSplitPercent)
        {
            throw new TariffPanelsLayoutValidationException(
                $"Ширина таблицы нерегулярных платежей должна быть от {MinimumTariffPanelsSplitPercent} до {MaximumTariffPanelsSplitPercent} процентов.");
        }

        var key = CreateTariffPanelsLayoutKey(userId);
        var setting = await repository.FindForUpdateAsync(key, cancellationToken);
        if (setting is not null)
        {
            OptimisticConcurrencyGuard.EnsureCurrent(request.Version, setting);
        }

        if (setting is null && request.IrregularPaymentsWidthPercent == DefaultTariffPanelsSplitPercent)
        {
            return new TariffPanelsLayoutDto(DefaultTariffPanelsSplitPercent, request.Version ?? Guid.NewGuid());
        }

        if (setting is null)
        {
            setting = new ApplicationSetting { Key = key };
            repository.Add(setting);
        }
        else if (setting.IntegerValue == request.IrregularPaymentsWidthPercent)
        {
            return new TariffPanelsLayoutDto(request.IrregularPaymentsWidthPercent, setting.Version);
        }

        setting.IntegerValue = request.IrregularPaymentsWidthPercent;
        setting.UpdatedAtUtc = timeProvider.GetUtcNow();
        setting.UpdatedByUserId = userId;
        await repository.SaveChangesAsync(cancellationToken);
        return new TariffPanelsLayoutDto(request.IrregularPaymentsWidthPercent, setting.Version);
    }

    public async Task<SalaryAccrualSettingsDto> GetSalaryAccrualSettingsAsync(CancellationToken cancellationToken)
    {
        var setting = await repository.FindAsync(SalaryAccrualDayKey, cancellationToken);
        return new SalaryAccrualSettingsDto(NormalizeSalaryAccrualDay(setting?.IntegerValue), setting?.Version ?? Guid.NewGuid());
    }

    public async Task<SalaryAccrualSettingsDto> UpdateSalaryAccrualSettingsAsync(
        UpdateSalaryAccrualSettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (request.AccrualDay is < 1 or > 28)
        {
            throw new SalaryAccrualSettingsValidationException("День начисления зарплаты должен быть от 1 до 28.");
        }

        var setting = await repository.FindForUpdateAsync(SalaryAccrualDayKey, cancellationToken);
        var previousValue = NormalizeSalaryAccrualDay(setting?.IntegerValue);
        if (setting is not null)
        {
            OptimisticConcurrencyGuard.EnsureCurrent(request.Version, setting);
        }
        if (setting is null && request.AccrualDay == DefaultSalaryAccrualDay)
        {
            return new SalaryAccrualSettingsDto(DefaultSalaryAccrualDay, request.Version ?? Guid.NewGuid());
        }

        if (setting is null)
        {
            setting = new ApplicationSetting { Key = SalaryAccrualDayKey };
            repository.Add(setting);
        }
        else if (setting.IntegerValue == request.AccrualDay)
        {
            return new SalaryAccrualSettingsDto(request.AccrualDay, setting.Version);
        }

        setting.IntegerValue = request.AccrualDay;
        setting.UpdatedAtUtc = timeProvider.GetUtcNow();
        setting.UpdatedByUserId = actorUserId;
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "application_setting.salary_accrual_day_updated",
            "application_setting",
            SalaryAccrualDayKey,
            Summary: $"День автоматического начисления зарплаты изменён с {previousValue} на {request.AccrualDay}.",
            Section: "settings",
            ActionKind: "update",
            EntityDisplayName: "Автоматическое начисление зарплаты",
            OldValues: new Dictionary<string, object?> { ["accrualDay"] = previousValue },
            NewValues: new Dictionary<string, object?> { ["accrualDay"] = request.AccrualDay },
            FieldLabels: new Dictionary<string, string> { ["accrualDay"] = "День начисления" }));

        await repository.SaveChangesAsync(cancellationToken);
        return new SalaryAccrualSettingsDto(request.AccrualDay, setting.Version);
    }

    public async Task<ActionCommentSettingsDto> GetActionCommentSettingsAsync(CancellationToken cancellationToken)
    {
        var setting = await repository.FindAsync(ActionCommentsRequiredKey, cancellationToken);
        return new ActionCommentSettingsDto(setting?.BooleanValue ?? false, setting?.Version ?? Guid.NewGuid());
    }

    public async Task<ActionCommentSettingsDto> UpdateActionCommentSettingsAsync(
        UpdateActionCommentSettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var setting = await repository.FindForUpdateAsync(ActionCommentsRequiredKey, cancellationToken);
        var previousValue = setting?.BooleanValue ?? false;
        if (setting is not null)
        {
            OptimisticConcurrencyGuard.EnsureCurrent(request.Version, setting);
        }

        if (setting is null && !request.Required)
        {
            return new ActionCommentSettingsDto(false, request.Version ?? Guid.NewGuid());
        }

        if (setting is null)
        {
            setting = new ApplicationSetting { Key = ActionCommentsRequiredKey };
            repository.Add(setting);
        }
        else if (setting.BooleanValue == request.Required)
        {
            return new ActionCommentSettingsDto(setting.BooleanValue, setting.Version);
        }

        setting.BooleanValue = request.Required;
        setting.UpdatedAtUtc = timeProvider.GetUtcNow();
        setting.UpdatedByUserId = actorUserId;
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "application_setting.action_comments_updated",
            "application_setting",
            ActionCommentsRequiredKey,
            Summary: request.Required
                ? "Включено обязательное заполнение комментариев к действиям."
                : "Отключено обязательное заполнение комментариев к действиям.",
            Section: "settings",
            ActionKind: "update",
            EntityDisplayName: "Комментарии к действиям",
            OldValues: new Dictionary<string, object?> { ["required"] = previousValue },
            NewValues: new Dictionary<string, object?> { ["required"] = request.Required },
            FieldLabels: new Dictionary<string, string> { ["required"] = "Требовать комментарий" }));

        await repository.SaveChangesAsync(cancellationToken);
        return new ActionCommentSettingsDto(setting.BooleanValue, setting.Version);
    }

    public async Task<HistoricalMeterReadingCorrectionSettingsDto> GetHistoricalMeterReadingCorrectionSettingsAsync(
        CancellationToken cancellationToken)
    {
        var setting = await repository.FindAsync(HistoricalMeterReadingCorrectionEnabledKey, cancellationToken);
        return new HistoricalMeterReadingCorrectionSettingsDto(setting?.BooleanValue ?? false, setting?.Version ?? Guid.NewGuid());
    }

    public async Task<HistoricalMeterReadingCorrectionSettingsDto> UpdateHistoricalMeterReadingCorrectionSettingsAsync(
        UpdateHistoricalMeterReadingCorrectionSettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var setting = await repository.FindForUpdateAsync(HistoricalMeterReadingCorrectionEnabledKey, cancellationToken);
        var previousValue = setting?.BooleanValue ?? false;
        if (setting is not null)
        {
            OptimisticConcurrencyGuard.EnsureCurrent(request.Version, setting);
        }

        if (setting is null && !request.Enabled)
        {
            return new HistoricalMeterReadingCorrectionSettingsDto(false, request.Version ?? Guid.NewGuid());
        }

        if (setting is null)
        {
            setting = new ApplicationSetting { Key = HistoricalMeterReadingCorrectionEnabledKey };
            repository.Add(setting);
        }
        else if (setting.BooleanValue == request.Enabled)
        {
            return new HistoricalMeterReadingCorrectionSettingsDto(setting.BooleanValue, setting.Version);
        }

        setting.BooleanValue = request.Enabled;
        setting.UpdatedAtUtc = timeProvider.GetUtcNow();
        setting.UpdatedByUserId = actorUserId;
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "application_setting.historical_meter_reading_correction_updated",
            "application_setting",
            HistoricalMeterReadingCorrectionEnabledKey,
            Summary: request.Enabled
                ? "Включено изменение существующих показаний за другие месяцы."
                : "Отключено изменение существующих показаний за другие месяцы.",
            Section: "settings",
            ActionKind: "update",
            EntityDisplayName: "Изменение показаний за другие месяцы",
            OldValues: new Dictionary<string, object?> { ["enabled"] = previousValue },
            NewValues: new Dictionary<string, object?> { ["enabled"] = request.Enabled },
            FieldLabels: new Dictionary<string, string> { ["enabled"] = "Разрешить изменение существующих показаний" }));

        await repository.SaveChangesAsync(cancellationToken);
        return new HistoricalMeterReadingCorrectionSettingsDto(setting.BooleanValue, setting.Version);
    }

    public async Task<BusinessDateSettingsDto> GetBusinessDateSettingsAsync(CancellationToken cancellationToken)
    {
        var setting = await repository.FindAsync(BusinessDateOverrideKey, cancellationToken);
        return CreateBusinessDateDto(setting, automation: null);
    }

    public async Task<BusinessDateChangePreviewDto> PreviewBusinessDateChangeAsync(
        PreviewBusinessDateRequest request,
        CancellationToken cancellationToken)
    {
        ValidateBusinessDate(request.OverrideDate);
        var setting = await repository.FindAsync(BusinessDateOverrideKey, cancellationToken);
        if (setting is not null)
        {
            OptimisticConcurrencyGuard.EnsureCurrent(request.Version, setting);
        }

        var proposedDate = request.OverrideDate ?? businessDateProvider.SystemDate;
        var automation = await regularAccrualAutomationRunner.PreviewForDateAsync(proposedDate, cancellationToken);
        return new BusinessDateChangePreviewDto(
            businessDateProvider.SystemDate,
            businessDateProvider.Today,
            proposedDate,
            request.OverrideDate,
            setting?.DateValue != request.OverrideDate,
            automation,
            setting?.Version ?? request.Version ?? Guid.NewGuid());
    }

    public async Task<BusinessDateSettingsDto> UpdateBusinessDateSettingsAsync(
        UpdateBusinessDateRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        ValidateBusinessDate(request.OverrideDate);
        var setting = await repository.FindForUpdateAsync(BusinessDateOverrideKey, cancellationToken);
        var previousValue = setting?.DateValue;
        if (setting is not null)
        {
            OptimisticConcurrencyGuard.EnsureCurrent(request.Version, setting);
        }

        if (previousValue == request.OverrideDate)
        {
            businessDateProvider.SetOverride(request.OverrideDate);
            return CreateBusinessDateDto(setting, automation: null);
        }

        if (setting is null)
        {
            setting = new ApplicationSetting { Key = BusinessDateOverrideKey };
            repository.Add(setting);
        }

        setting.DateValue = request.OverrideDate;
        setting.UpdatedAtUtc = timeProvider.GetUtcNow();
        setting.UpdatedByUserId = actorUserId;

        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "application_setting.business_date_updated",
            "application_setting",
            BusinessDateOverrideKey,
            Summary: request.OverrideDate is { } date
                ? $"Установлена тестовая рабочая дата {date:dd.MM.yyyy}."
                : "Восстановлена автоматическая системная дата.",
            Section: "settings",
            ActionKind: "update",
            EntityDisplayName: "Рабочая дата",
            OldValues: new Dictionary<string, object?> { ["businessDate"] = previousValue },
            NewValues: new Dictionary<string, object?> { ["businessDate"] = request.OverrideDate },
            FieldLabels: new Dictionary<string, string> { ["businessDate"] = "Рабочая дата" }));

        await repository.SaveChangesAsync(cancellationToken);
        businessDateProvider.SetOverride(request.OverrideDate);

        RegularAccrualAutomationSummaryDto automation;
        try
        {
            var run = await regularAccrualAutomationRunner.RunForDateAsync(
                businessDateProvider.Today,
                actorUserId,
                cancellationToken);
            automation = new RegularAccrualAutomationSummaryDto(
                run.Succeeded,
                run.CreatedCount,
                run.SkippedCount,
                run.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Immediate regular accrual automation failed after changing the business date.");
            automation = new RegularAccrualAutomationSummaryDto(
                false,
                0,
                0,
                "Рабочая дата сохранена, но автоматическое начисление завершилось ошибкой. Фоновая задача повторит попытку.");
        }

        return CreateBusinessDateDto(setting, automation);
    }

    private BusinessDateSettingsDto CreateBusinessDateDto(
        ApplicationSetting? setting,
        RegularAccrualAutomationSummaryDto? automation) =>
        new(
            businessDateProvider.SystemDate,
            businessDateProvider.Today,
            businessDateProvider.OverrideDate,
            businessDateProvider.OverrideDate.HasValue,
            setting?.UpdatedAtUtc,
            automation,
            setting?.Version ?? Guid.NewGuid());

    private void ValidateBusinessDate(DateOnly? value)
    {
        if (value is null)
        {
            return;
        }

        var systemDate = businessDateProvider.SystemDate;
        if (value < systemDate.AddYears(-10) || value > systemDate.AddYears(10))
        {
            throw new BusinessDateValidationException(
                $"Рабочая дата должна быть в диапазоне от {systemDate.AddYears(-10):dd.MM.yyyy} до {systemDate.AddYears(10):dd.MM.yyyy}.");
        }
    }

    private static int NormalizeSalaryAccrualDay(int? value) =>
        value is >= 1 and <= 28 ? value.Value : DefaultSalaryAccrualDay;

    private static TariffTableDisplaySettingsDto CreateTariffTableDisplaySettingsDto(int mask, Guid version) =>
        new((mask & 1) != 0, (mask & 2) != 0, version, (mask & 4) != 0);

    private static string CreateAccrualReasonDisplayMode(int? value) => value switch
    {
        1 => AccrualReasonDisplayModes.All,
        2 => AccrualReasonDisplayModes.Hidden,
        _ => AccrualReasonDisplayModes.PenaltiesOnly
    };

    private static int CreateAccrualReasonDisplayValue(string mode) => mode switch
    {
        AccrualReasonDisplayModes.All => 1,
        AccrualReasonDisplayModes.Hidden => 2,
        _ => 0
    };

    private static string GetAccrualReasonDisplayLabel(string mode) => mode switch
    {
        AccrualReasonDisplayModes.All => "у всех начислений",
        AccrualReasonDisplayModes.Hidden => "не показывать",
        _ => "только у штрафов"
    };

    private static string CreateTariffPanelsLayoutKey(Guid userId) =>
        $"users.{userId:N}.tariffs.bottom_panels_split";

    private static TariffPanelsLayoutDto CreateTariffPanelsLayoutDto(int? value, Guid version) =>
        new(value is >= MinimumTariffPanelsSplitPercent and <= MaximumTariffPanelsSplitPercent
            ? value.Value
            : DefaultTariffPanelsSplitPercent, version);
}

public sealed class BusinessDateValidationException(string message) : Exception(message);

public sealed class SalaryAccrualSettingsValidationException(string message) : Exception(message);

public sealed class TariffPanelsLayoutValidationException(string message) : Exception(message);

public sealed class AccrualReasonDisplaySettingsValidationException(string message) : Exception(message);
