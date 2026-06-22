using System.Security.Claims;
using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Auth;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task BootstrapAdminAsync_CreatesFirstAdministratorWithPermissions()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateService(repository);

        var result = await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Главный администратор"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var user = Assert.Single(repository.Users);
        Assert.Equal("ADMIN@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Contains(user.UserRoles, role => role.Role.Code == SystemRoles.Administrator);
        Assert.Contains(SystemPermissions.UsersManage, result.Value!.User.Permissions);
        Assert.Contains(repository.AuditEvents, item => item.Action == "auth.bootstrap_admin_created");
        Assert.False(string.IsNullOrWhiteSpace(result.Value.AccessToken));
    }

    [Fact]
    public async Task BootstrapAdminAsync_RejectsSecondAdministrator()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateService(repository);
        await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Администратор"),
            CancellationToken.None);

        var result = await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("second@example.com", "StrongPass123", "Второй администратор"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("bootstrap_closed", result.ErrorCode);
        Assert.Single(repository.Users);
    }

    [Fact]
    public async Task LoginAsync_ReturnsTokenAndWritesAuditForValidPassword()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateService(repository);
        await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Администратор"),
            CancellationToken.None);

        var result = await service.LoginAsync(new LoginRequest("admin@example.com", "StrongPass123"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(repository.Users[0].LastLoginAtUtc);
        Assert.Contains(repository.AuditEvents, item => item.Action == "auth.login_success");
        Assert.Contains(SystemRoles.Administrator, result.Value!.User.Roles);
    }

    [Fact]
    public async Task LoginAsync_RejectsInvalidPassword()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateService(repository);
        await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Администратор"),
            CancellationToken.None);

        var result = await service.LoginAsync(new LoginRequest("admin@example.com", "wrong"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_credentials", result.ErrorCode);
    }

    [Fact]
    public async Task LoginAsync_RejectsInactiveUser()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateService(repository);
        await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Администратор"),
            CancellationToken.None);
        repository.Users[0].IsActive = false;

        var result = await service.LoginAsync(new LoginRequest("admin@example.com", "StrongPass123"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("user_inactive", result.ErrorCode);
    }

    [Fact]
    public async Task GetCurrentUserAsync_ReturnsUserFromClaims()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateService(repository);
        await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Администратор"),
            CancellationToken.None);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, repository.Users[0].Id.ToString())],
            "Test"));

        var result = await service.GetCurrentUserAsync(principal, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(repository.Users[0].Id, result.Value!.Id);
        Assert.Contains(SystemPermissions.UsersManage, result.Value.Permissions);
    }

    [Fact]
    public async Task GetCurrentUserAsync_RejectsInvalidClaims()
    {
        var service = CreateService(new InMemoryUserRepository());
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await service.GetCurrentUserAsync(principal, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_token", result.ErrorCode);
    }

    private static AuthService CreateService(InMemoryUserRepository repository)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "GarageBalance.Tests",
            Audience = "GarageBalance.Tests",
            SigningKey = "test-signing-key-that-is-long-enough-32",
            AccessTokenMinutes = 15
        });
        return new AuthService(repository, new Pbkdf2PasswordHasher(), new JwtTokenService(jwtOptions));
    }
}
