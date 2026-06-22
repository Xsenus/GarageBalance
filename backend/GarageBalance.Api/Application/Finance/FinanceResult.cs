namespace GarageBalance.Api.Application.Finance;

public sealed class FinanceResult<T>
{
    private FinanceResult(T? value, string? errorCode, string? errorMessage)
    {
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded => ErrorCode is null;
    public T? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static FinanceResult<T> Success(T value)
    {
        return new FinanceResult<T>(value, null, null);
    }

    public static FinanceResult<T> Failure(string errorCode, string errorMessage)
    {
        return new FinanceResult<T>(default, errorCode, errorMessage);
    }
}
