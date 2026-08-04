using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Auth;

public sealed class JwtSessionValidationEventsTests
{
    [Fact]
    public void CreateToken_IncludesCurrentSessionVersion()
    {
        var user = CreateUser();
        user.SessionVersion = 7;
        var tokenService = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "GarageBalance.Tests",
            Audience = "GarageBalance.Tests",
            SigningKey = "test-signing-key-that-is-long-enough-32",
            AccessTokenMinutes = 15
        }));

        var response = tokenService.CreateToken(user, ["operator"], ["payments.read"]);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);

        Assert.Equal("7", token.Claims.Single(claim => claim.Type == JwtClaimNames.SessionVersion).Value);
    }

    [Fact]
    public async Task TokenValidated_AcceptsOnlyMatchingActiveSession()
    {
        var repository = new InMemoryUserRepository();
        var user = CreateUser();
        repository.Users.Add(user);
        var events = new JwtSessionValidationEvents(repository);

        var validContext = CreateContext(user.Id, user.SessionVersion);
        await events.TokenValidated(validContext);
        Assert.Null(validContext.Result);

        var staleContext = CreateContext(user.Id, user.SessionVersion - 1);
        await events.TokenValidated(staleContext);
        Assert.NotNull(staleContext.Result?.Failure);

        user.IsActive = false;
        var inactiveContext = CreateContext(user.Id, user.SessionVersion);
        await events.TokenValidated(inactiveContext);
        Assert.NotNull(inactiveContext.Result?.Failure);
    }

    [Theory]
    [InlineData(null, "1")]
    [InlineData("not-a-guid", "1")]
    [InlineData("00000000-0000-0000-0000-000000000001", null)]
    [InlineData("00000000-0000-0000-0000-000000000001", "0")]
    [InlineData("00000000-0000-0000-0000-000000000001", "not-a-number")]
    public async Task TokenValidated_RejectsMissingOrInvalidSessionClaims(string? userId, string? sessionVersion)
    {
        var context = CreateContext(userId, sessionVersion);

        await new JwtSessionValidationEvents(new InMemoryUserRepository()).TokenValidated(context);

        Assert.NotNull(context.Result?.Failure);
    }

    private static TokenValidatedContext CreateContext(Guid userId, long sessionVersion) =>
        CreateContext(userId.ToString(), sessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static TokenValidatedContext CreateContext(string? userId, string? sessionVersion)
    {
        var claims = new List<Claim>();
        if (userId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        }

        if (sessionVersion is not null)
        {
            claims.Add(new Claim(JwtClaimNames.SessionVersion, sessionVersion));
        }

        var context = new TokenValidatedContext(
            new DefaultHttpContext(),
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme))
        };
        return context;
    }

    private static AppUser CreateUser() => new()
    {
        Email = "operator@example.test",
        NormalizedEmail = "OPERATOR@EXAMPLE.TEST",
        DisplayName = "Оператор",
        PasswordHash = "hash"
    };
}
