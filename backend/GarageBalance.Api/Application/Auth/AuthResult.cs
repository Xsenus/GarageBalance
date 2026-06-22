namespace GarageBalance.Api.Application.Auth;

public sealed record AuthResult<T>(bool Succeeded, T? Value, string? ErrorCode, string? ErrorMessage)
{
    public static AuthResult<T> Success(T value) => new(true, value, null, null);

    public static AuthResult<T> Failure(string code, string message) => new(false, default, code, message);
}
