using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfUserRepository(GarageBalanceDbContext dbContext) : IUserRepository
{
    public Task<bool> HasAnyUsersAsync(CancellationToken cancellationToken)
    {
        return dbContext.Users.AnyAsync(cancellationToken);
    }

    public Task<AppUser?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    public Task<AppUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<AppRole>> GetRolesByCodesAsync(IReadOnlyList<string> roleCodes, CancellationToken cancellationToken)
    {
        return await dbContext.Roles
            .Where(role => roleCodes.Contains(role.Code))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AppRole>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role)
            .ToListAsync(cancellationToken);
    }

    public async Task EnsureSystemRolesAsync(CancellationToken cancellationToken)
    {
        await EnsureRoleAsync(SystemRoles.Administrator, "Администратор", SystemPermissions.Administrator, cancellationToken);
        await EnsureRoleAsync(SystemRoles.Accountant, "Бухгалтер", SystemPermissions.Accountant, cancellationToken);
        await EnsureRoleAsync(SystemRoles.Operator, "Оператор", SystemPermissions.Operator, cancellationToken);
        await EnsureRoleAsync(SystemRoles.ReportsViewer, "Просмотр отчетов", SystemPermissions.ReportsViewer, cancellationToken);
    }

    public async Task AddUserAsync(AppUser user, IReadOnlyList<AppRole> roles, AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        dbContext.Users.Add(user);
        foreach (var role in roles)
        {
            user.UserRoles.Add(new AppUserRole
            {
                User = user,
                Role = role
            });
        }

        dbContext.AuditEvents.Add(auditEvent);
        await Task.CompletedTask;
    }

    public async Task AddAuditEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        dbContext.AuditEvents.Add(auditEvent);
        await Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
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
}
