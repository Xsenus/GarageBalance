using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Domain.Users;

namespace GarageBalance.Api.Tests.Auth;

internal sealed class InMemoryUserRepository : IUserRepository
{
    public List<AppUser> Users { get; } = [];
    public List<AppRole> Roles { get; } = [];
    public List<AuditEvent> AuditEvents { get; } = [];

    public Task<bool> HasAnyUsersAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Users.Count > 0);
    }

    public Task<AppUser?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        return Task.FromResult(Users.SingleOrDefault(user => user.NormalizedEmail == normalizedEmail));
    }

    public Task<AppUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Users.SingleOrDefault(user => user.Id == userId));
    }

    public Task<IReadOnlyList<AppRole>> GetRolesByCodesAsync(IReadOnlyList<string> roleCodes, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<AppRole>>(Roles.Where(role => roleCodes.Contains(role.Code)).ToList());
    }

    public Task<IReadOnlyList<AppRole>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = Users.Single(item => item.Id == userId);
        return Task.FromResult<IReadOnlyList<AppRole>>(user.UserRoles.Select(userRole => userRole.Role).ToList());
    }

    public Task<int> CountAuditEventsAsync(string action, string entityType, string? entityId, DateTimeOffset createdSinceUtc, CancellationToken cancellationToken)
    {
        var count = AuditEvents.Count(auditEvent =>
            auditEvent.Action == action &&
            auditEvent.EntityType == entityType &&
            auditEvent.EntityId == entityId &&
            auditEvent.CreatedAtUtc >= createdSinceUtc);

        return Task.FromResult(count);
    }

    public Task EnsureSystemRolesAsync(CancellationToken cancellationToken)
    {
        EnsureRole(SystemRoles.Administrator, "Администратор", SystemPermissions.Administrator);
        EnsureRole(SystemRoles.Accountant, "Бухгалтер", SystemPermissions.Accountant);
        EnsureRole(SystemRoles.Operator, "Оператор", SystemPermissions.Operator);
        EnsureRole(SystemRoles.ReportsViewer, "Просмотр отчетов", SystemPermissions.ReportsViewer);
        return Task.CompletedTask;
    }

    public Task AddUserAsync(AppUser user, IReadOnlyList<AppRole> roles, AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        foreach (var role in roles)
        {
            user.UserRoles.Add(new AppUserRole
            {
                User = user,
                UserId = user.Id,
                Role = role,
                RoleId = role.Id
            });
        }

        Users.Add(user);
        AuditEvents.Add(auditEvent);
        return Task.CompletedTask;
    }

    public Task AddAuditEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        AuditEvents.Add(auditEvent);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void EnsureRole(string code, string name, IReadOnlyList<string> permissions)
    {
        var role = Roles.SingleOrDefault(item => item.Code == code);
        if (role is null)
        {
            Roles.Add(new AppRole
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
}
