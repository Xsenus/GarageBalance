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

public sealed record ManagedUsersPageDto(
    IReadOnlyList<ManagedUserDto> Items,
    int TotalCount,
    int Offset,
    int Limit);

public sealed record CreateManagedUserRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MaxLength(200)] string DisplayName,
    [Required, MinLength(8), MaxLength(200)] string Password,
    [MinLength(1)] IReadOnlyList<string> RoleCodes,
    bool IsActive = true);

public sealed record UpdateManagedUserRequest(
    [Required, MaxLength(200)] string DisplayName,
    [MinLength(1)] IReadOnlyList<string> RoleCodes,
    bool IsActive,
    [MinLength(8), MaxLength(200)] string? NewPassword,
    [MaxLength(1000)] string? DeactivationReason = null);
