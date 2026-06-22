using Microsoft.AspNetCore.Authorization;

namespace GarageBalance.Api.Infrastructure.Security;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
