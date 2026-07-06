namespace GarageBalance.Api.Application.Workflows;

public sealed class FormStateResult<T>
{
    private FormStateResult(T? value, string? errorCode, string? errorMessage)
    {
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded => ErrorCode is null;
    public T? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static FormStateResult<T> Success(T value)
    {
        return new FormStateResult<T>(value, null, null);
    }

    public static FormStateResult<T> Failure(string errorCode, string errorMessage)
    {
        return new FormStateResult<T>(default, errorCode, errorMessage);
    }
}
