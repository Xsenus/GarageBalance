using GarageBalance.Api.Domain.Users;

namespace GarageBalance.Api.Application.Auth;

public interface ITokenService
{
    AuthResponse CreateToken(AppUser user, IReadOnlyList<string> roles, IReadOnlyList<string> permissions);
}
