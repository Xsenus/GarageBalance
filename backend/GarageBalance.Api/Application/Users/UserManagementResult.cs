namespace GarageBalance.Api.Application.Users;

public sealed record UserManagementResult<T>(bool Succeeded, T? Value, string? ErrorCode, string? ErrorMessage)
{
    public static UserManagementResult<T> Success(T value) => new(true, value, null, null);

    public static UserManagementResult<T> Failure(string code, string message) => new(false, default, code, message);
}
