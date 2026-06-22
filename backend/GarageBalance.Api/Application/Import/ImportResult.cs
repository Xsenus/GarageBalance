namespace GarageBalance.Api.Application.Import;

public sealed class ImportResult<T>
{
    private ImportResult(bool succeeded, T? value, string? errorCode, string? errorMessage)
    {
        Succeeded = succeeded;
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }
    public T? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static ImportResult<T> Success(T value)
    {
        return new ImportResult<T>(true, value, null, null);
    }

    public static ImportResult<T> Failure(string errorCode, string errorMessage)
    {
        return new ImportResult<T>(false, default, errorCode, errorMessage);
    }
}
