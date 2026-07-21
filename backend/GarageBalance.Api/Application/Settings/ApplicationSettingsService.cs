using GarageBalance.Api.Application.Audit;
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
    public const string BusinessDateOverrideKey = "system.business_date_override";

    public async Task<PaymentDisplaySettingsDto> GetPaymentDisplaySettingsAsync(CancellationToken cancellationToken)
    {
        var setting = await repository.FindAsync(ShowAllGarageOperationsKey, cancellationToken);
        return new PaymentDisplaySettingsDto(setting?.BooleanValue ?? false);
    }

    public async Task<PaymentDisplaySettingsDto> UpdatePaymentDisplaySettingsAsync(
        UpdatePaymentDisplaySettingsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var setting = await repository.FindForUpdateAsync(ShowAllGarageOperationsKey, cancellationToken);
        var previousValue = setting?.BooleanValue ?? false;

        if (setting is null && !request.ShowAllGarageOperationsByDefault)
        {
            return new PaymentDisplaySettingsDto(false);
        }

        if (setting is null)
        {
            setting = new ApplicationSetting { Key = ShowAllGarageOperationsKey };
            repository.Add(setting);
        }
        else if (setting.BooleanValue == request.ShowAllGarageOperationsByDefault)
        {
            return new PaymentDisplaySettingsDto(setting.BooleanValue);
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

        await repository.SaveChangesAsync(cancellationToken);
        return new PaymentDisplaySettingsDto(setting.BooleanValue);
    }

    public async Task<BusinessDateSettingsDto> GetBusinessDateSettingsAsync(CancellationToken cancellationToken)
    {
        var setting = await repository.FindAsync(BusinessDateOverrideKey, cancellationToken);
        return CreateBusinessDateDto(setting, automation: null);
    }

    public async Task<BusinessDateSettingsDto> UpdateBusinessDateSettingsAsync(
        UpdateBusinessDateRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        ValidateBusinessDate(request.OverrideDate);
        var setting = await repository.FindForUpdateAsync(BusinessDateOverrideKey, cancellationToken);
        var previousValue = setting?.DateValue;

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
            automation);

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
}

public sealed class BusinessDateValidationException(string message) : Exception(message);
