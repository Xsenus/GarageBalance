using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Security;
using GarageBalance.Api.Domain.Integrations;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Integrations;

public sealed class IntegrationSecretSettingsService(
    GarageBalanceDbContext dbContext,
    ISensitiveDataProtector sensitiveDataProtector,
    IAuditEventWriter auditEventWriter) : IIntegrationSecretSettingsService
{
    private static readonly IReadOnlyDictionary<string, string> IntegrationSecretFieldLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["provider"] = "Провайдер",
        ["settingKey"] = "Ключ настройки",
        ["purpose"] = "Назначение защиты",
        ["protectedValueState"] = "Защищенное значение"
    };

    public async Task<IntegrationSecretSettingResult<IntegrationSecretSettingDto>> UpsertSecretAsync(
        UpsertIntegrationSecretRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(request);
        if (validation is not null)
        {
            return validation;
        }

        var provider = request.Provider.Trim();
        var settingKey = request.SettingKey.Trim();
        var normalizedProvider = Normalize(provider);
        var normalizedSettingKey = Normalize(settingKey);
        var purpose = BuildPurpose(provider, settingKey);
        var plaintextValue = request.PlaintextValue.Trim();

        var setting = await dbContext.IntegrationSecretSettings
            .FirstOrDefaultAsync(item =>
                item.NormalizedProvider == normalizedProvider &&
                item.NormalizedSettingKey == normalizedSettingKey,
                cancellationToken);

        var isNew = setting is null;
        var oldProvider = setting?.Provider;
        var oldSettingKey = setting?.SettingKey;
        var oldPurpose = setting?.Purpose;
        var oldProtectedValueState = string.IsNullOrWhiteSpace(setting?.ProtectedValue) ? null : "задано";
        var existingPlaintext = setting is null || string.IsNullOrWhiteSpace(setting.ProtectedValue)
            ? null
            : sensitiveDataProtector.Unprotect(setting.ProtectedValue, setting.Purpose);
        var protectedValueChanged = !string.Equals(existingPlaintext, plaintextValue, StringComparison.Ordinal);

        if (!isNew &&
            string.Equals(oldProvider, provider, StringComparison.Ordinal) &&
            string.Equals(oldSettingKey, settingKey, StringComparison.Ordinal) &&
            string.Equals(oldPurpose, purpose, StringComparison.Ordinal) &&
            !protectedValueChanged)
        {
            return IntegrationSecretSettingResult<IntegrationSecretSettingDto>.Success(ToDto(setting!));
        }

        var protectedValue = sensitiveDataProtector.Protect(plaintextValue, purpose);

        if (setting is null)
        {
            setting = new IntegrationSecretSetting
            {
                Provider = provider,
                SettingKey = settingKey,
                NormalizedProvider = normalizedProvider,
                NormalizedSettingKey = normalizedSettingKey
            };
            dbContext.IntegrationSecretSettings.Add(setting);
        }
        else
        {
            setting.Provider = provider;
            setting.SettingKey = settingKey;
        }

        setting.Purpose = purpose;
        setting.ProtectedValue = protectedValue;
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;
        setting.UpdatedByUserId = actorUserId;

        var protectedValueState = isNew ? "задано" : protectedValueChanged ? "обновлено" : "задано";
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "integration.secret_upserted",
            "integration_secret_setting",
            $"{provider}:{settingKey}",
            Summary: isNew
                ? $"Создана защищенная настройка интеграции {provider}:{settingKey}."
                : $"Обновлена защищенная настройка интеграции {provider}:{settingKey}.",
            ActionKind: isNew ? "create" : "update",
            EntityDisplayName: $"{provider}:{settingKey}",
            OldValues: new Dictionary<string, object?>
            {
                ["provider"] = oldProvider,
                ["settingKey"] = oldSettingKey,
                ["purpose"] = oldPurpose,
                ["protectedValueState"] = oldProtectedValueState
            },
            NewValues: new Dictionary<string, object?>
            {
                ["provider"] = provider,
                ["settingKey"] = settingKey,
                ["purpose"] = purpose,
                ["protectedValueState"] = protectedValueState
            },
            FieldLabels: IntegrationSecretFieldLabels,
            Metadata: new Dictionary<string, object?>
            {
                ["provider"] = provider,
                ["settingKey"] = settingKey,
                ["purpose"] = purpose,
                ["protectedValueState"] = protectedValueState
            }));

        await dbContext.SaveChangesAsync(cancellationToken);
        return IntegrationSecretSettingResult<IntegrationSecretSettingDto>.Success(ToDto(setting));
    }

    public async Task<IntegrationSecretSettingResult<string>> GetSecretAsync(
        string provider,
        string settingKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(settingKey))
        {
            return IntegrationSecretSettingResult<string>.Failure("integration_secret_key_required", "Provider and setting key are required.");
        }

        var normalizedProvider = Normalize(provider);
        var normalizedSettingKey = Normalize(settingKey);

        var setting = await dbContext.IntegrationSecretSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.NormalizedProvider == normalizedProvider &&
                item.NormalizedSettingKey == normalizedSettingKey,
                cancellationToken);

        if (setting is null)
        {
            return IntegrationSecretSettingResult<string>.Failure("integration_secret_not_found", "Integration secret setting was not found.");
        }

        var plaintext = sensitiveDataProtector.Unprotect(setting.ProtectedValue, setting.Purpose);
        return IntegrationSecretSettingResult<string>.Success(plaintext);
    }

    public async Task<IReadOnlyList<IntegrationSecretSettingDto>> GetSettingsAsync(
        string? provider,
        CancellationToken cancellationToken)
    {
        var query = dbContext.IntegrationSecretSettings.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(provider))
        {
            var normalizedProvider = Normalize(provider);
            query = query.Where(item => item.NormalizedProvider == normalizedProvider);
        }

        return await query
            .OrderBy(item => item.Provider)
            .ThenBy(item => item.SettingKey)
            .Select(item => ToDto(item))
            .ToListAsync(cancellationToken);
    }

    private static IntegrationSecretSettingResult<IntegrationSecretSettingDto>? ValidateRequest(UpsertIntegrationSecretRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            return IntegrationSecretSettingResult<IntegrationSecretSettingDto>.Failure("integration_provider_required", "Integration provider is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SettingKey))
        {
            return IntegrationSecretSettingResult<IntegrationSecretSettingDto>.Failure("integration_secret_key_required", "Integration setting key is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PlaintextValue))
        {
            return IntegrationSecretSettingResult<IntegrationSecretSettingDto>.Failure("integration_secret_value_required", "Integration secret value is required.");
        }

        if (request.Provider.Trim().Length > 100 || request.SettingKey.Trim().Length > 120)
        {
            return IntegrationSecretSettingResult<IntegrationSecretSettingDto>.Failure("integration_secret_key_too_long", "Integration provider or setting key is too long.");
        }

        return null;
    }

    private static IntegrationSecretSettingDto ToDto(IntegrationSecretSetting setting)
    {
        return new IntegrationSecretSettingDto(
            setting.Id,
            setting.Provider,
            setting.SettingKey,
            setting.Purpose,
            setting.UpdatedAtUtc,
            setting.UpdatedByUserId,
            !string.IsNullOrWhiteSpace(setting.ProtectedValue));
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string BuildPurpose(string provider, string settingKey)
    {
        return $"{provider.Trim()}.{settingKey.Trim()}";
    }
}
