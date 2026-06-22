using System.Security.Claims;

namespace GarageBalance.Api.Application.Auth;

public interface IAuthService
{
    Task<AuthResult<AuthResponse>> BootstrapAdminAsync(BootstrapAdminRequest request, CancellationToken cancellationToken);
    Task<AuthResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthResult<CurrentUserDto>> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
