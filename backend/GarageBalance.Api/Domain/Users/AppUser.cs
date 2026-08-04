using GarageBalance.Api.Domain.Common;

namespace GarageBalance.Api.Domain.Users;

public sealed class AppUser : IOptimisticConcurrencyEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public long SessionVersion { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();

    public List<AppUserRole> UserRoles { get; set; } = [];
}
