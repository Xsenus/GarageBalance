namespace GarageBalance.Api.Application.Releases;

public sealed class AppReleaseResult<T>
{
    private AppReleaseResult(T? value, string? errorCode, string? errorMessage)
    {
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded => ErrorCode is null;

    public T? Value { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static AppReleaseResult<T> Success(T value) => new(value, null, null);

    public static AppReleaseResult<T> Failure(string errorCode, string errorMessage) => new(default, errorCode, errorMessage);
}
