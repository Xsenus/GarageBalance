using System.Security.Cryptography;
using System.Text.Json;
using System.Security.Claims;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Domain.Users;

namespace GarageBalance.Api.Application.Auth;

public sealed class AuthService(IUserRepository users, IPasswordHasher passwordHasher, ITokenService tokenService) : IAuthService
{
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LoginAttemptWindow = TimeSpan.FromMinutes(15);

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

        var attemptEntityType = user is null ? "login_email" : "user";
        var attemptEntityId = user?.Id.ToString() ?? CreateLoginEmailHash(normalizedEmail);
        if (await IsLoginRateLimitedAsync(attemptEntityType, attemptEntityId, cancellationToken))
        {
            await AddLoginRateLimitedAuditAsync(user, attemptEntityType, attemptEntityId, cancellationToken);
            await users.SaveChangesAsync(cancellationToken);

            return AuthResult<AuthResponse>.Failure("too_many_login_attempts", "Слишком много неуспешных попыток входа. Повторите позже.");
        }

        if (user is null || !passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            await AddLoginFailedAuditAsync(user, attemptEntityType, attemptEntityId, cancellationToken);
            await users.SaveChangesAsync(cancellationToken);

            return AuthResult<AuthResponse>.Failure("invalid_credentials", "Неверный email или пароль.");
        }

        if (!user.IsActive)
        {
            await users.AddAuditEventAsync(new AuditEvent
            {
                ActorUserId = user.Id,
                Action = "auth.login_inactive",
                EntityType = "user",
                EntityId = user.Id.ToString(),
                Summary = $"Отклонен вход отключенного пользователя {user.Email}."
            }, cancellationToken);
            await users.SaveChangesAsync(cancellationToken);

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

    public async Task<AuthResult<CurrentUserDto>> ChangeOwnPasswordAsync(ClaimsPrincipal principal, ChangeOwnPasswordRequest request, CancellationToken cancellationToken)
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

        if (!passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            await users.AddAuditEventAsync(new AuditEvent
            {
                ActorUserId = user.Id,
                Action = "auth.password_change_failed",
                EntityType = "user",
                EntityId = user.Id.ToString(),
                Summary = $"Отклонена смена пароля пользователя {user.Email}: неверный текущий пароль."
            }, cancellationToken);
            await users.SaveChangesAsync(cancellationToken);

            return AuthResult<CurrentUserDto>.Failure("invalid_current_password", "Текущий пароль указан неверно.");
        }

        if (passwordHasher.VerifyPassword(request.NewPassword, user.PasswordHash))
        {
            return AuthResult<CurrentUserDto>.Failure("new_password_same_as_current", "Новый пароль должен отличаться от текущего.");
        }

        user.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
        await users.AddAuditEventAsync(new AuditEvent
        {
            ActorUserId = user.Id,
            Action = "auth.password_changed",
            EntityType = "user",
            EntityId = user.Id.ToString(),
            Summary = $"Пользователь {user.Email} сменил пароль."
        }, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);

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

    private async Task<bool> IsLoginRateLimitedAsync(string entityType, string entityId, CancellationToken cancellationToken)
    {
        var createdSinceUtc = DateTimeOffset.UtcNow.Subtract(LoginAttemptWindow);
        var failedAttempts = await users.CountAuditEventsAsync("auth.login_failed", entityType, entityId, createdSinceUtc, cancellationToken);

        return failedAttempts >= MaxFailedLoginAttempts;
    }

    private Task AddLoginFailedAuditAsync(AppUser? user, string entityType, string entityId, CancellationToken cancellationToken)
    {
        var summary = user is null
            ? "Неуспешная попытка входа: пользователь не найден."
            : $"Неуспешная попытка входа пользователя {user.Email}: неверный пароль.";

        return users.AddAuditEventAsync(new AuditEvent
        {
            ActorUserId = user?.Id,
            Action = "auth.login_failed",
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            MetadataJson = JsonSerializer.Serialize(new
            {
                reason = user is null ? "unknown_user" : "invalid_password"
            })
        }, cancellationToken);
    }

    private Task AddLoginRateLimitedAuditAsync(AppUser? user, string entityType, string entityId, CancellationToken cancellationToken)
    {
        return users.AddAuditEventAsync(new AuditEvent
        {
            ActorUserId = user?.Id,
            Action = "auth.login_rate_limited",
            EntityType = entityType,
            EntityId = entityId,
            Summary = user is null
                ? "Вход временно ограничен после серии неуспешных попыток для указанного email."
                : $"Вход пользователя {user.Email} временно ограничен после серии неуспешных попыток.",
            MetadataJson = JsonSerializer.Serialize(new
            {
                failedAttempts = MaxFailedLoginAttempts,
                windowMinutes = (int)LoginAttemptWindow.TotalMinutes
            })
        }, cancellationToken);
    }

    private static string CreateLoginEmailHash(string normalizedEmail)
    {
        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalizedEmail));
        return $"email-sha256:{Convert.ToHexString(hashBytes).ToLowerInvariant()}";
    }
}
