using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Users;

public sealed class UserManagementService(
    GarageBalanceDbContext dbContext,
    IPasswordHasher passwordHasher,
    IPasswordPolicyValidator passwordPolicyValidator,
    IAuditEventWriter auditEventWriter) : IUserManagementService
{
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;
    private const int DefaultPageLimit = 25;

    public async Task<IReadOnlyList<ManagedRoleDto>> GetRolesAsync(CancellationToken cancellationToken)
    {
        await EnsureSystemRolesAsync(cancellationToken);

        return await dbContext.Roles.AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new ManagedRoleDto(role.Code, role.Name, role.Permissions))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ManagedUserDto>> GetUsersAsync(string? search, CancellationToken cancellationToken, int? limit = null)
    {
        await EnsureSystemRolesAsync(cancellationToken);

        var users = await BuildUsersQuery(search)
            .OrderBy(user => user.DisplayName)
            .Take(NormalizeListLimit(limit))
            .ToListAsync(cancellationToken);

        return users.Select(ToDto).ToList();
    }

    public async Task<ManagedUsersPageDto> GetUsersPageAsync(string? search, int offset, int limit, CancellationToken cancellationToken)
    {
        await EnsureSystemRolesAsync(cancellationToken);

        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizePageLimit(limit);
        var query = BuildUsersQuery(search);
        var totalCount = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(user => user.DisplayName)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);

        return new ManagedUsersPageDto(users.Select(ToDto).ToList(), totalCount, normalizedOffset, normalizedLimit);
    }

    private IQueryable<AppUser> BuildUsersQuery(string? search)
    {
        IQueryable<AppUser> query = dbContext.Users.AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role);

        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(user =>
                user.NormalizedEmail.Contains(normalizedSearch) ||
                user.DisplayName.ToLower().Contains(normalizedSearch));
        }

        return query;
    }

    private static int NormalizeListLimit(int? limit)
    {
        if (limit is null or <= 0)
        {
            return DefaultListLimit;
        }

        return Math.Min(limit.Value, MaxListLimit);
    }

    private static int NormalizePageLimit(int limit)
    {
        return limit <= 0 ? DefaultPageLimit : Math.Min(limit, MaxListLimit);
    }

    private static int NormalizeListOffset(int offset)
    {
        return Math.Max(0, offset);
    }

    public async Task<UserManagementResult<ManagedUserDto>> CreateUserAsync(CreateManagedUserRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        await EnsureSystemRolesAsync(cancellationToken);

        var normalizedEmail = NormalizeEmail(request.Email);
        if (await dbContext.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            return UserManagementResult<ManagedUserDto>.Failure("user_email_duplicate", "Пользователь с таким email уже существует.");
        }

        var passwordPolicy = passwordPolicyValidator.Validate(request.Password);
        if (!passwordPolicy.Succeeded)
        {
            return UserManagementResult<ManagedUserDto>.Failure("password_policy_violation", passwordPolicy.ErrorMessage!);
        }

        var rolesResult = await GetRolesOrFailureAsync(request.RoleCodes, cancellationToken);
        if (!rolesResult.Succeeded)
        {
            return UserManagementResult<ManagedUserDto>.Failure(rolesResult.ErrorCode!, rolesResult.ErrorMessage!);
        }

        var user = new AppUser
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = passwordHasher.HashPassword(request.Password),
            IsActive = request.IsActive
        };

        foreach (var role in rolesResult.Value!)
        {
            user.UserRoles.Add(new AppUserRole
            {
                User = user,
                Role = role
            });
        }

        dbContext.Users.Add(user);
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "users.user_created",
            "app_user",
            user.Id.ToString(),
            Summary: $"Создан пользователь {user.DisplayName}.",
            ActionKind: "create",
            EntityDisplayName: user.DisplayName,
            RelatedCounterpartyId: user.Id.ToString(),
            RelatedCounterpartyName: user.DisplayName,
            Metadata: new Dictionary<string, object?>
            {
                ["displayName"] = user.DisplayName,
                ["isActive"] = user.IsActive,
                ["roles"] = FormatRoleCodes(rolesResult.Value!)
            }));
        await dbContext.SaveChangesAsync(cancellationToken);

        return UserManagementResult<ManagedUserDto>.Success(ToDto(user));
    }

    public async Task<UserManagementResult<ManagedUserDto>> UpdateUserAsync(Guid userId, UpdateManagedUserRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        await EnsureSystemRolesAsync(cancellationToken);

        var user = await dbContext.Users
            .Include(item => item.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
        {
            return UserManagementResult<ManagedUserDto>.Failure("user_not_found", "Пользователь не найден.");
        }

        var rolesResult = await GetRolesOrFailureAsync(request.RoleCodes, cancellationToken);
        if (!rolesResult.Succeeded)
        {
            return UserManagementResult<ManagedUserDto>.Failure(rolesResult.ErrorCode!, rolesResult.ErrorMessage!);
        }

        var keepsAdministratorRole = rolesResult.Value!.Any(role => role.Code == SystemRoles.Administrator);
        if ((!request.IsActive || !keepsAdministratorRole) && await IsLastActiveAdministratorAsync(user.Id, cancellationToken))
        {
            return UserManagementResult<ManagedUserDto>.Failure("last_admin_required", "Нельзя отключить или лишить роли последнего активного администратора.");
        }

        var deactivatingUser = user.IsActive && !request.IsActive;
        var deactivationReason = string.Empty;
        if (deactivatingUser && ValidateDeactivationReason(request.DeactivationReason, out deactivationReason) is { } reasonError)
        {
            return UserManagementResult<ManagedUserDto>.Failure(reasonError.Code, reasonError.Message);
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["displayName"] = user.DisplayName,
            ["isActive"] = user.IsActive,
            ["roles"] = FormatRoleCodes(user.UserRoles.Select(userRole => userRole.Role)),
            ["credentialsChanged"] = false
        };
        var passwordChanged = false;

        user.DisplayName = request.DisplayName.Trim();
        user.IsActive = request.IsActive;
        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            var passwordPolicy = passwordPolicyValidator.Validate(request.NewPassword);
            if (!passwordPolicy.Succeeded)
            {
                return UserManagementResult<ManagedUserDto>.Failure("password_policy_violation", passwordPolicy.ErrorMessage!);
            }

            user.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
            passwordChanged = true;
        }

        user.UserRoles.Clear();
        foreach (var role in rolesResult.Value!)
        {
            user.UserRoles.Add(new AppUserRole
            {
                User = user,
                Role = role
            });
        }

        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "users.user_updated",
            "app_user",
            user.Id.ToString(),
            Summary: $"Обновлен пользователь {user.DisplayName}.",
            ActionKind: "update",
            EntityDisplayName: user.DisplayName,
            OldValues: oldValues,
            NewValues: new Dictionary<string, object?>
            {
                ["displayName"] = user.DisplayName,
                ["isActive"] = user.IsActive,
                ["roles"] = FormatRoleCodes(rolesResult.Value!),
                ["credentialsChanged"] = passwordChanged
            },
            FieldLabels: UserAuditFieldLabels,
            Reason: deactivatingUser ? deactivationReason : null,
            RelatedCounterpartyId: user.Id.ToString(),
            RelatedCounterpartyName: user.DisplayName,
            Metadata: new Dictionary<string, object?>
            {
                ["roles"] = FormatRoleCodes(rolesResult.Value!),
                ["isActive"] = user.IsActive,
                ["credentialsChanged"] = passwordChanged
            }));
        await dbContext.SaveChangesAsync(cancellationToken);

        return UserManagementResult<ManagedUserDto>.Success(ToDto(user));
    }

    private static (string Code, string Message)? ValidateDeactivationReason(string? reason, out string normalizedReason)
    {
        normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length == 0)
        {
            return ("user_deactivation_reason_required", "Укажите причину отключения пользователя.");
        }

        if (normalizedReason.Length > 1000)
        {
            return ("user_deactivation_reason_too_long", "Причина отключения пользователя не должна быть длиннее 1000 символов.");
        }

        return null;
    }

    private async Task<UserManagementResult<IReadOnlyList<AppRole>>> GetRolesOrFailureAsync(IReadOnlyList<string> roleCodes, CancellationToken cancellationToken)
    {
        var normalizedRoleCodes = roleCodes
            .Select(code => code.Trim())
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedRoleCodes.Length == 0)
        {
            return UserManagementResult<IReadOnlyList<AppRole>>.Failure("roles_required", "Нужно выбрать хотя бы одну роль.");
        }

        var roles = await dbContext.Roles
            .Where(role => normalizedRoleCodes.Contains(role.Code))
            .ToListAsync(cancellationToken);

        if (roles.Count != normalizedRoleCodes.Length)
        {
            return UserManagementResult<IReadOnlyList<AppRole>>.Failure("role_not_found", "Одна или несколько ролей не найдены.");
        }

        return UserManagementResult<IReadOnlyList<AppRole>>.Success(roles);
    }

    private async Task<bool> IsLastActiveAdministratorAsync(Guid userId, CancellationToken cancellationToken)
    {
        var activeAdministrators = await dbContext.Users
            .Where(user => user.IsActive)
            .Where(user => user.UserRoles.Any(userRole => userRole.Role.Code == SystemRoles.Administrator))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        return activeAdministrators.Count == 1 && activeAdministrators[0] == userId;
    }

    private async Task EnsureSystemRolesAsync(CancellationToken cancellationToken)
    {
        await EnsureRoleAsync(SystemRoles.Administrator, "Администратор", SystemPermissions.Administrator, cancellationToken);
        await EnsureRoleAsync(SystemRoles.Accountant, "Бухгалтер", SystemPermissions.Accountant, cancellationToken);
        await EnsureRoleAsync(SystemRoles.Operator, "Оператор", SystemPermissions.Operator, cancellationToken);
        await EnsureRoleAsync(SystemRoles.ReportsViewer, "Просмотр отчетов", SystemPermissions.ReportsViewer, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureRoleAsync(string code, string name, IReadOnlyList<string> permissions, CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles.SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (role is null)
        {
            dbContext.Roles.Add(new AppRole
            {
                Code = code,
                Name = name,
                Permissions = permissions.ToList()
            });
            return;
        }

        role.Name = name;
        role.Permissions = permissions.ToList();
    }

    private static readonly IReadOnlyDictionary<string, string> UserAuditFieldLabels = new Dictionary<string, string>
    {
        ["displayName"] = "Имя",
        ["isActive"] = "Активность",
        ["roles"] = "Роли",
        ["credentialsChanged"] = "Смена учетных данных"
    };

    private static string FormatRoleCodes(IEnumerable<AppRole> roles)
    {
        return string.Join(", ", roles.Select(role => role.Code).Order(StringComparer.Ordinal));
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToUpperInvariant();
    }

    private static string? NormalizeSearch(string? search)
    {
        return string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToUpperInvariant();
    }

    private static ManagedUserDto ToDto(AppUser user)
    {
        var roles = user.UserRoles
            .Select(userRole => userRole.Role)
            .OrderBy(role => role.Name)
            .ToArray();

        var permissions = roles
            .SelectMany(role => role.Permissions)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(permission => permission)
            .ToArray();

        return new ManagedUserDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.IsActive,
            user.CreatedAtUtc,
            user.LastLoginAtUtc,
            roles.Select(role => role.Code).ToArray(),
            permissions);
    }
}
