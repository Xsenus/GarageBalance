namespace GarageBalance.Api.Application.Reports;

public sealed class ReportResult<T>
{
    private ReportResult(bool succeeded, T? value, string? errorCode, string? errorMessage)
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

    public static ReportResult<T> Success(T value)
    {
        return new ReportResult<T>(true, value, null, null);
    }

    public static ReportResult<T> Failure(string errorCode, string errorMessage)
    {
        return new ReportResult<T>(false, default, errorCode, errorMessage);
    }
}
