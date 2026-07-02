using GarageBalance.Api.Domain.Users;

namespace GarageBalance.Api.Application.Auth;

public interface IUserRepository
{
    Task<bool> HasAnyUsersAsync(CancellationToken cancellationToken);
    Task<AppUser?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<AppUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AppRole>> GetRolesByCodesAsync(IReadOnlyList<string> roleCodes, CancellationToken cancellationToken);
    Task<IReadOnlyList<AppRole>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken);
    Task<int> CountAuditEventsAsync(string action, string entityType, string? entityId, DateTimeOffset createdSinceUtc, CancellationToken cancellationToken);
    Task EnsureSystemRolesAsync(CancellationToken cancellationToken);
    Task AddUserAsync(AppUser user, IReadOnlyList<AppRole> roles, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
