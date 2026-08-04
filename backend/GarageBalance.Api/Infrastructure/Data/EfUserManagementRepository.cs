using GarageBalance.Api.Application.Users;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfUserManagementRepository(GarageBalanceDbContext dbContext) : IUserManagementRepository
{
    public async Task<IReadOnlyList<AppRole>> GetRolesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Roles.AsNoTracking()
            .OrderBy(role => role.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AppUser>> GetUsersAsync(string? normalizedSearch, int limit, CancellationToken cancellationToken)
    {
        return await BuildUsersQuery(normalizedSearch)
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserManagementUsersPageData> GetUsersPageAsync(
        string? normalizedSearch,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = BuildUsersQuery(normalizedSearch);
        var totalCount = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new UserManagementUsersPageData(users, totalCount);
    }

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        return dbContext.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    public async Task<IReadOnlyList<AppRole>> GetRolesByCodesAsync(IReadOnlyList<string> roleCodes, CancellationToken cancellationToken)
    {
        return await dbContext.Roles
            .Where(role => roleCodes.Contains(role.Code))
            .ToListAsync(cancellationToken);
    }

    public Task<AppUser?> FindUserForUpdateAsync(Guid userId, bool inactiveOnly, CancellationToken cancellationToken)
    {
        var query = dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .Where(user => user.Id == userId);
        if (inactiveOnly)
        {
            query = query.Where(user => !user.IsActive);
        }

        return query.SingleOrDefaultAsync(cancellationToken);
    }

    public Task<AppRole?> FindRoleForUpdateAsync(string roleCode, CancellationToken cancellationToken)
    {
        return dbContext.Roles
            .Include(role => role.UserRoles)
            .ThenInclude(userRole => userRole.User)
            .SingleOrDefaultAsync(role => role.Code == roleCode, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveAdministratorIdsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .Where(user => user.IsActive)
            .Where(user => user.UserRoles.Any(userRole => userRole.Role.Code == SystemRoles.Administrator))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task EnsureRoleAsync(string code, string name, IReadOnlyList<string> permissions, CancellationToken cancellationToken)
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
    }

    public void AddUser(AppUser user)
    {
        dbContext.Users.Add(user);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<AppUser> BuildUsersQuery(string? normalizedSearch)
    {
        var query = dbContext.Users.AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            if (IsNpgsqlProvider())
            {
                var pattern = $"%{EscapeLikePattern(normalizedSearch)}%";
                query = query.Where(user =>
                    EF.Functions.ILike(user.NormalizedEmail, pattern, @"\") ||
                    EF.Functions.ILike(user.DisplayName, pattern, @"\"));
            }
            else
            {
                query = query.Where(user =>
                    user.NormalizedEmail.ToLower().Contains(normalizedSearch) ||
                    user.DisplayName.ToLower().Contains(normalizedSearch));
            }
        }

        return query;
    }

    private bool IsNpgsqlProvider() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static string EscapeLikePattern(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
