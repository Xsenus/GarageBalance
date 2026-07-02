using System.Security.Claims;
using System.Security.Cryptography;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Domain.Users;

namespace GarageBalance.Api.Application.Auth;

public sealed class AuthService(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IPasswordPolicyValidator passwordPolicyValidator,
    ITokenService tokenService,
    IAuditEventWriter auditEventWriter) : IAuthService
{
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LoginAttemptWindow = TimeSpan.FromMinutes(15);

    public async Task<AuthResult<AuthResponse>> BootstrapAdminAsync(BootstrapAdminRequest request, CancellationToken cancellationToken)
    {
        var passwordPolicy = passwordPolicyValidator.Validate(request.Password);
        if (!passwordPolicy.Succeeded)
        {
            return AuthResult<AuthResponse>.Failure("password_policy_violation", passwordPolicy.ErrorMessage!);
        }

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

        await users.AddUserAsync(admin, [adminRole], cancellationToken);
        auditEventWriter.Add(new AuditEventWriteRequest(
            admin.Id,
            "auth.bootstrap_admin_created",
            "user",
            admin.Id.ToString(),
            $"Создан первый администратор {admin.Email}.",
            EntityDisplayName: admin.DisplayName,
            Metadata: new Dictionary<string, object?>
            {
                ["email"] = admin.Email,
                ["role"] = adminRole.Code
            }));
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
            AddLoginRateLimitedAudit(user, attemptEntityType, attemptEntityId);
            await users.SaveChangesAsync(cancellationToken);

            return AuthResult<AuthResponse>.Failure("too_many_login_attempts", "Слишком много неуспешных попыток входа. Повторите позже.");
        }

        if (user is null || !passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            AddLoginFailedAudit(user, attemptEntityType, attemptEntityId);
            await users.SaveChangesAsync(cancellationToken);

            return AuthResult<AuthResponse>.Failure("invalid_credentials", "Неверный email или пароль.");
        }

        if (!user.IsActive)
        {
            auditEventWriter.Add(new AuditEventWriteRequest(
                user.Id,
                "auth.login_inactive",
                "user",
                user.Id.ToString(),
                $"Отклонен вход отключенного пользователя {user.Email}.",
                EntityDisplayName: user.DisplayName,
                Metadata: new Dictionary<string, object?>
                {
                    ["email"] = user.Email,
                    ["reason"] = "inactive_user"
                }));
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

        auditEventWriter.Add(new AuditEventWriteRequest(
            user.Id,
            "auth.login_success",
            "user",
            user.Id.ToString(),
            $"Пользователь {user.Email} вошел в систему.",
            EntityDisplayName: user.DisplayName,
            Metadata: new Dictionary<string, object?>
            {
                ["email"] = user.Email,
                ["roles"] = string.Join(", ", roleCodes)
            }));
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
            auditEventWriter.Add(new AuditEventWriteRequest(
                user.Id,
                "auth.password_change_failed",
                "user",
                user.Id.ToString(),
                $"Отклонена смена пароля пользователя {user.Email}: неверный текущий пароль.",
                EntityDisplayName: user.DisplayName,
                Metadata: new Dictionary<string, object?>
                {
                    ["email"] = user.Email,
                    ["reason"] = "invalid_current_password"
                }));
            await users.SaveChangesAsync(cancellationToken);

            return AuthResult<CurrentUserDto>.Failure("invalid_current_password", "Текущий пароль указан неверно.");
        }

        if (passwordHasher.VerifyPassword(request.NewPassword, user.PasswordHash))
        {
            return AuthResult<CurrentUserDto>.Failure("new_password_same_as_current", "Новый пароль должен отличаться от текущего.");
        }

        var passwordPolicy = passwordPolicyValidator.Validate(request.NewPassword);
        if (!passwordPolicy.Succeeded)
        {
            return AuthResult<CurrentUserDto>.Failure("password_policy_violation", passwordPolicy.ErrorMessage!);
        }

        user.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
        auditEventWriter.Add(new AuditEventWriteRequest(
            user.Id,
            "auth.password_changed",
            "user",
            user.Id.ToString(),
            $"Пользователь {user.Email} сменил пароль.",
            EntityDisplayName: user.DisplayName,
            Metadata: new Dictionary<string, object?>
            {
                ["email"] = user.Email
            }));
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

    private void AddLoginFailedAudit(AppUser? user, string entityType, string entityId)
    {
        var summary = user is null
            ? "Неуспешная попытка входа: пользователь не найден."
            : $"Неуспешная попытка входа пользователя {user.Email}: неверный пароль.";

        auditEventWriter.Add(new AuditEventWriteRequest(
            user?.Id,
            "auth.login_failed",
            entityType,
            entityId,
            summary,
            EntityDisplayName: user?.DisplayName,
            Metadata: new Dictionary<string, object?>
            {
                ["email"] = user?.Email,
                ["reason"] = user is null ? "unknown_user" : "invalid_password"
            }));
    }

    private void AddLoginRateLimitedAudit(AppUser? user, string entityType, string entityId)
    {
        auditEventWriter.Add(new AuditEventWriteRequest(
            user?.Id,
            "auth.login_rate_limited",
            entityType,
            entityId,
            user is null
                ? "Вход временно ограничен после серии неуспешных попыток для указанного email."
                : $"Вход пользователя {user.Email} временно ограничен после серии неуспешных попыток.",
            EntityDisplayName: user?.DisplayName,
            Metadata: new Dictionary<string, object?>
            {
                ["email"] = user?.Email,
                ["failedAttempts"] = MaxFailedLoginAttempts,
                ["windowMinutes"] = (int)LoginAttemptWindow.TotalMinutes
            }));
    }

    private static string CreateLoginEmailHash(string normalizedEmail)
    {
        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalizedEmail));
        return $"email-sha256:{Convert.ToHexString(hashBytes).ToLowerInvariant()}";
    }
}
