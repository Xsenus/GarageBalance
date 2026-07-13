using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Settings;

namespace GarageBalance.Api.Application.Settings;

public sealed class ApplicationSettingsService(
    IApplicationSettingRepository repository,
    IAuditEventWriter auditEventWriter) : IApplicationSettingsService
{
    public const string ShowAllGarageOperationsKey = "payments.show_all_garage_operations_by_default";

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
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;
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
}
