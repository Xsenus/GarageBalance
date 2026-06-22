namespace GarageBalance.Api.Domain.Users;

public sealed class AppUserRole
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public Guid RoleId { get; set; }
    public AppRole Role { get; set; } = null!;
}
