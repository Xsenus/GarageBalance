namespace GarageBalance.Api.Application.Funds;

public sealed class FundResult<T>
{
    private FundResult(T? value, string? errorCode, string? errorMessage)
    {
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded => ErrorCode is null;
    public T? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static FundResult<T> Success(T value)
    {
        return new FundResult<T>(value, null, null);
    }

    public static FundResult<T> Failure(string errorCode, string errorMessage)
    {
        return new FundResult<T>(default, errorCode, errorMessage);
    }
}
