using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Users;

public sealed record ManagedRoleDto(string Code, string Name, IReadOnlyList<string> Permissions);

public sealed record ManagedUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record CreateManagedUserRequest(
    [property: Required, EmailAddress, MaxLength(320)] string Email,
    [property: Required, MaxLength(200)] string DisplayName,
    [property: Required, MinLength(8), MaxLength(200)] string Password,
    [property: MinLength(1)] IReadOnlyList<string> RoleCodes,
    bool IsActive = true);

public sealed record UpdateManagedUserRequest(
    [property: Required, MaxLength(200)] string DisplayName,
    [property: MinLength(1)] IReadOnlyList<string> RoleCodes,
    bool IsActive,
    [property: MinLength(8), MaxLength(200)] string? NewPassword);
