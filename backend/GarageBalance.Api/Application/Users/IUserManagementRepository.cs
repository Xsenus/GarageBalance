using GarageBalance.Api.Domain.Users;

namespace GarageBalance.Api.Application.Users;

public interface IUserManagementRepository
{
    Task<IReadOnlyList<AppRole>> GetRolesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AppUser>> GetUsersAsync(string? normalizedSearch, int limit, CancellationToken cancellationToken);
    Task<UserManagementUsersPageData> GetUsersPageAsync(string? normalizedSearch, int offset, int limit, CancellationToken cancellationToken);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<IReadOnlyList<AppRole>> GetRolesByCodesAsync(IReadOnlyList<string> roleCodes, CancellationToken cancellationToken);
    Task<AppUser?> FindUserForUpdateAsync(Guid userId, bool inactiveOnly, CancellationToken cancellationToken);
    Task<AppRole?> FindRoleForUpdateAsync(string roleCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetActiveAdministratorIdsAsync(CancellationToken cancellationToken);
    Task EnsureRoleAsync(string code, string name, IReadOnlyList<string> permissions, CancellationToken cancellationToken);
    void AddUser(AppUser user);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record UserManagementUsersPageData(IReadOnlyList<AppUser> Users, int TotalCount);
