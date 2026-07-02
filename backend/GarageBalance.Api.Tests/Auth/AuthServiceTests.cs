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
    public async Task BootstrapAdminAsync_RejectsWeakPassword()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateService(repository);

        var result = await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("admin@example.com", "password", "Администратор"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("password_policy_violation", result.ErrorCode);
        Assert.Empty(repository.Users);
        Assert.Empty(repository.AuditEvents);
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
        var audit = Assert.Single(repository.AuditEvents, item => item.Action == "auth.login_failed");
        Assert.Equal(repository.Users[0].Id, audit.ActorUserId);
        Assert.Equal("user", audit.EntityType);
        Assert.DoesNotContain("wrong", audit.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_WritesAuditForUnknownEmailWithoutPlainEmail()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateService(repository);

        var result = await service.LoginAsync(new LoginRequest("missing@example.com", "wrong"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_credentials", result.ErrorCode);
        var audit = Assert.Single(repository.AuditEvents, item => item.Action == "auth.login_failed");
        Assert.Null(audit.ActorUserId);
        Assert.Equal("login_email", audit.EntityType);
        Assert.StartsWith("email-sha256:", audit.EntityId, StringComparison.Ordinal);
        Assert.DoesNotContain("missing@example.com", audit.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("missing@example.com", audit.EntityId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wrong", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_RateLimitsAfterFailedAttempts()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateService(repository);
        await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Администратор"),
            CancellationToken.None);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await service.LoginAsync(new LoginRequest("admin@example.com", $"wrong-{attempt}"), CancellationToken.None);
            Assert.False(failed.Succeeded);
            Assert.Equal("invalid_credentials", failed.ErrorCode);
        }

        var result = await service.LoginAsync(new LoginRequest("admin@example.com", "StrongPass123"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("too_many_login_attempts", result.ErrorCode);
        Assert.Null(repository.Users[0].LastLoginAtUtc);
        Assert.Equal(5, repository.AuditEvents.Count(item => item.Action == "auth.login_failed"));
        Assert.Contains(repository.AuditEvents, item => item.Action == "auth.login_rate_limited");
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
        Assert.Contains(repository.AuditEvents, item => item.Action == "auth.login_inactive");
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

    [Fact]
    public async Task ChangeOwnPasswordAsync_ChangesPasswordAndWritesAudit()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateService(repository);
        await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Администратор"),
            CancellationToken.None);
        var user = repository.Users[0];
        var oldHash = user.PasswordHash;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
            "Test"));

        var result = await service.ChangeOwnPasswordAsync(
            principal,
            new ChangeOwnPasswordRequest("StrongPass123", "NewStrongPass123"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(user.Id, result.Value!.Id);
        Assert.NotEqual(oldHash, user.PasswordHash);
        Assert.Contains(repository.AuditEvents, item =>
            item.Action == "auth.password_changed" &&
            item.ActorUserId == user.Id &&
            !item.Summary.Contains("StrongPass123", StringComparison.OrdinalIgnoreCase) &&
            !item.Summary.Contains("NewStrongPass123", StringComparison.OrdinalIgnoreCase));

        var oldLogin = await service.LoginAsync(new LoginRequest("admin@example.com", "StrongPass123"), CancellationToken.None);
        Assert.False(oldLogin.Succeeded);

        var newLogin = await service.LoginAsync(new LoginRequest("admin@example.com", "NewStrongPass123"), CancellationToken.None);
        Assert.True(newLogin.Succeeded);
    }

    [Fact]
    public async Task ChangeOwnPasswordAsync_RejectsInvalidCurrentPasswordWithoutChangingHash()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateService(repository);
        await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Администратор"),
            CancellationToken.None);
        var user = repository.Users[0];
        var oldHash = user.PasswordHash;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
            "Test"));

        var result = await service.ChangeOwnPasswordAsync(
            principal,
            new ChangeOwnPasswordRequest("WrongPass123", "NewStrongPass123"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_current_password", result.ErrorCode);
        Assert.Equal(oldHash, user.PasswordHash);
        Assert.Contains(repository.AuditEvents, item =>
            item.Action == "auth.password_change_failed" &&
            item.ActorUserId == user.Id &&
            !item.Summary.Contains("WrongPass123", StringComparison.OrdinalIgnoreCase) &&
            !item.Summary.Contains("NewStrongPass123", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChangeOwnPasswordAsync_RejectsWeakNewPasswordWithoutChangingHash()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateService(repository);
        await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Администратор"),
            CancellationToken.None);
        var user = repository.Users[0];
        var oldHash = user.PasswordHash;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
            "Test"));

        var result = await service.ChangeOwnPasswordAsync(
            principal,
            new ChangeOwnPasswordRequest("StrongPass123", "password"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("password_policy_violation", result.ErrorCode);
        Assert.Equal(oldHash, user.PasswordHash);
        Assert.DoesNotContain(repository.AuditEvents, item => item.Action == "auth.password_changed");
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
        return new AuthService(
            repository,
            new Pbkdf2PasswordHasher(),
            new PasswordPolicyValidator(),
            new JwtTokenService(jwtOptions),
            new InMemoryAuditEventWriter(repository));
    }
}
