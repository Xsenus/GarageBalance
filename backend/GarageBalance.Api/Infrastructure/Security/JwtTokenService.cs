using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GarageBalance.Api.Infrastructure.Security;

public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    public AuthResponse CreateToken(AppUser user, IReadOnlyList<string> roles, IReadOnlyList<string> permissions)
    {
        var jwt = options.Value;
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expires,
            new CurrentUserDto(user.Id, user.Email, user.DisplayName, roles, permissions));
    }
}
