using GarageBalance.Api.Domain.Common;

namespace GarageBalance.Api.Domain.Users;

public sealed class AppRole : IOptimisticConcurrencyEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    public List<string> Permissions { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<AppUserRole> UserRoles { get; set; } = [];
}
