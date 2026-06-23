using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Auth;

public sealed record BootstrapAdminRequest(
    [property: Required, EmailAddress, MaxLength(320)] string Email,
    [property: Required, MinLength(8), MaxLength(200)] string Password,
    [property: Required, MaxLength(200)] string DisplayName);

public sealed record LoginRequest(
    [property: Required, EmailAddress, MaxLength(320)] string Email,
    [property: Required, MaxLength(200)] string Password);

public sealed record ChangeOwnPasswordRequest(
    [property: Required, MaxLength(200)] string CurrentPassword,
    [property: Required, MinLength(8), MaxLength(200)] string NewPassword);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    CurrentUserDto User);

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
