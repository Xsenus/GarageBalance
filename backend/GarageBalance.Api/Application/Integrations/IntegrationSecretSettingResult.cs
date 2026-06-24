namespace GarageBalance.Api.Application.Integrations;

public sealed record IntegrationSecretSettingResult<T>(bool Succeeded, T? Value, string? ErrorCode, string? ErrorMessage)
{
    public static IntegrationSecretSettingResult<T> Success(T value) => new(true, value, null, null);

    public static IntegrationSecretSettingResult<T> Failure(string errorCode, string errorMessage) => new(false, default, errorCode, errorMessage);
}
