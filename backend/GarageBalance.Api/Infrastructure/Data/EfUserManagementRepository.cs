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
        var query = BuildUserFilterQuery(normalizedSearch);
        if (IsNpgsqlProvider())
        {
            return await GetPostgresUsersPageAsync(query, offset, limit, cancellationToken);
        }

        var queryWithRoles = query
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role);
        var totalCount = await queryWithRoles.CountAsync(cancellationToken);
        var users = await queryWithRoles
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new UserManagementUsersPageData(users, totalCount);
    }

    private async Task<UserManagementUsersPageData> GetPostgresUsersPageAsync(
        IQueryable<AppUser> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageUsers = query
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Skip(offset)
            .Take(limit);
        var pageRows =
            from user in pageUsers
            join userRole in dbContext.UserRoles.AsNoTracking()
                on user.Id equals userRole.UserId into userRoles
            from userRole in userRoles.DefaultIfEmpty()
            join role in dbContext.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id into roles
            from role in roles.DefaultIfEmpty()
            select new
            {
                Category = PageCategory,
                UserId = (Guid?)user.Id,
                Email = (string?)user.Email,
                DisplayName = (string?)user.DisplayName,
                IsActive = (bool?)user.IsActive,
                CreatedAtUtc = (DateTimeOffset?)user.CreatedAtUtc,
                LastLoginAtUtc = user.LastLoginAtUtc,
                Version = (Guid?)user.Version,
                RoleId = role == null ? null : (Guid?)role.Id,
                RoleCode = role == null ? null : role.Code,
                RoleName = role == null ? null : role.Name,
                Permissions = role == null ? null : role.Permissions,
                TotalCount = 0
            };
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new
            {
                Category = TotalsCategory,
                UserId = (Guid?)null,
                Email = (string?)null,
                DisplayName = (string?)null,
                IsActive = (bool?)null,
                CreatedAtUtc = (DateTimeOffset?)null,
                LastLoginAtUtc = (DateTimeOffset?)null,
                Version = (Guid?)null,
                RoleId = (Guid?)null,
                RoleCode = (string?)null,
                RoleName = (string?)null,
                Permissions = (List<string>?)null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenBy(row => row.DisplayName)
            .ThenBy(row => row.UserId)
            .ThenBy(row => row.RoleName)
            .ThenBy(row => row.RoleId)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var users = rows
            .Where(row => row.Category == PageCategory)
            .GroupBy(row => row.UserId!.Value)
            .Select(group =>
            {
                var first = group.First();
                var user = new AppUser
                {
                    Id = first.UserId!.Value,
                    Email = first.Email!,
                    NormalizedEmail = string.Empty,
                    DisplayName = first.DisplayName!,
                    PasswordHash = string.Empty,
                    IsActive = first.IsActive!.Value,
                    CreatedAtUtc = first.CreatedAtUtc!.Value,
                    LastLoginAtUtc = first.LastLoginAtUtc,
                    Version = first.Version!.Value
                };
                user.UserRoles = group
                    .Where(row => row.RoleId.HasValue)
                    .GroupBy(row => row.RoleId!.Value)
                    .Select(roleGroup => roleGroup.First())
                    .Select(row => new AppUserRole
                    {
                        UserId = user.Id,
                        User = user,
                        RoleId = row.RoleId!.Value,
                        Role = new AppRole
                        {
                            Id = row.RoleId.Value,
                            Code = row.RoleCode!,
                            Name = row.RoleName!,
                            Permissions = row.Permissions ?? []
                        }
                    })
                    .ToList();
                return user;
            })
            .ToList();
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

    private IQueryable<AppUser> BuildUsersQuery(string? normalizedSearch) =>
        BuildUserFilterQuery(normalizedSearch)
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role);

    private IQueryable<AppUser> BuildUserFilterQuery(string? normalizedSearch)
    {
        var query = dbContext.Users.AsNoTracking()
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            if (IsNpgsqlProvider())
            {
                var pattern = $"%{EscapeLikePattern(normalizedSearch)}%";
                query = query.Where(user =>
                    EF.Functions.ILike(user.NormalizedEmail, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\") ||
                    EF.Functions.ILike(user.DisplayName, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\"));
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
