using System.Security.Claims;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Domain.Users;

namespace GarageBalance.Api.Application.Auth;

public sealed class AuthService(IUserRepository users, IPasswordHasher passwordHasher, ITokenService tokenService) : IAuthService
{
    public async Task<AuthResult<AuthResponse>> BootstrapAdminAsync(BootstrapAdminRequest request, CancellationToken cancellationToken)
    {
        if (await users.HasAnyUsersAsync(cancellationToken))
        {
            return AuthResult<AuthResponse>.Failure("bootstrap_closed", "Первый администратор уже создан.");
        }

        await users.EnsureSystemRolesAsync(cancellationToken);
        var adminRoles = await users.GetRolesByCodesAsync([SystemRoles.Administrator], cancellationToken);
        var adminRole = adminRoles.SingleOrDefault();
        if (adminRole is null)
        {
            return AuthResult<AuthResponse>.Failure("role_missing", "Системная роль администратора не создана.");
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var admin = new AppUser
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = passwordHasher.HashPassword(request.Password)
        };

        var audit = new AuditEvent
        {
            ActorUserId = admin.Id,
            Action = "auth.bootstrap_admin_created",
            EntityType = "user",
            EntityId = admin.Id.ToString(),
            Summary = $"Создан первый администратор {admin.Email}."
        };

        await users.AddUserAsync(admin, [adminRole], audit, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);

        return AuthResult<AuthResponse>.Success(tokenService.CreateToken(admin, [adminRole.Code], adminRole.Permissions));
    }

    public async Task<AuthResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await users.FindUserByEmailAsync(normalizedEmail, cancellationToken);
        if (user is null || !passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return AuthResult<AuthResponse>.Failure("invalid_credentials", "Неверный email или пароль.");
        }

        if (!user.IsActive)
        {
            return AuthResult<AuthResponse>.Failure("user_inactive", "Пользователь отключен.");
        }

        user.LastLoginAtUtc = DateTimeOffset.UtcNow;
        var roles = user.UserRoles.Select(userRole => userRole.Role).ToList();
        var roleCodes = roles.Select(role => role.Code).Distinct(StringComparer.Ordinal).OrderBy(role => role).ToArray();
        var permissions = roles
            .SelectMany(role => role.Permissions)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(permission => permission)
            .ToArray();

        await users.AddAuditEventAsync(new AuditEvent
        {
            ActorUserId = user.Id,
            Action = "auth.login_success",
            EntityType = "user",
            EntityId = user.Id.ToString(),
            Summary = $"Пользователь {user.Email} вошел в систему."
        }, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);

        return AuthResult<AuthResponse>.Success(tokenService.CreateToken(user, roleCodes, permissions));
    }

    public async Task<AuthResult<CurrentUserDto>> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return AuthResult<CurrentUserDto>.Failure("invalid_token", "Не удалось определить пользователя.");
        }

        var user = await users.FindUserByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return AuthResult<CurrentUserDto>.Failure("user_not_found", "Пользователь не найден или отключен.");
        }

        var roles = user.UserRoles.Select(userRole => userRole.Role).ToList();
        var dto = new CurrentUserDto(
            user.Id,
            user.Email,
            user.DisplayName,
            roles.Select(role => role.Code).Distinct(StringComparer.Ordinal).OrderBy(role => role).ToArray(),
            roles.SelectMany(role => role.Permissions).Distinct(StringComparer.Ordinal).OrderBy(permission => permission).ToArray());

        return AuthResult<CurrentUserDto>.Success(dto);
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToUpperInvariant();
    }
}
