namespace GarageBalance.Api.Application.Users;

public interface IUserManagementService
{
    Task<IReadOnlyList<ManagedRoleDto>> GetRolesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ManagedUserDto>> GetUsersAsync(string? search, CancellationToken cancellationToken, int? limit = null);

    Task<ManagedUsersPageDto> GetUsersPageAsync(string? search, int offset, int limit, CancellationToken cancellationToken);

    Task<UserManagementResult<ManagedUserDto>> CreateUserAsync(CreateManagedUserRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<UserManagementResult<ManagedUserDto>> UpdateUserAsync(Guid userId, UpdateManagedUserRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<UserManagementResult<ManagedUserDto>> RestoreUserAsync(Guid userId, Guid? actorUserId, CancellationToken cancellationToken);

    Task<UserManagementResult<ManagedRoleDto>> UpdateRolePermissionsAsync(string roleCode, UpdateRolePermissionsRequest request, Guid? actorUserId, CancellationToken cancellationToken);
}
