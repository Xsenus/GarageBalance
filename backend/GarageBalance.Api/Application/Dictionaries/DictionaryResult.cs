namespace GarageBalance.Api.Application.Dictionaries;

public sealed record DictionaryResult<T>(bool Succeeded, T? Value, string? ErrorCode, string? ErrorMessage)
{
    public static DictionaryResult<T> Success(T value) => new(true, value, null, null);
    public static DictionaryResult<T> Failure(string code, string message) => new(false, default, code, message);
}
