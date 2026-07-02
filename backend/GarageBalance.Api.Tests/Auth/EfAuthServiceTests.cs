using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Auth;

public sealed class EfAuthServiceTests
{
    [Fact]
    public async Task BootstrapAdminAsync_SeedsSystemRolesBeforeCreatingAdministrator()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var service = new AuthService(
            new EfUserRepository(context),
            new Pbkdf2PasswordHasher(),
            new PasswordPolicyValidator(),
            new JwtTokenService(Options.Create(new JwtOptions { SigningKey = "test-signing-key-with-more-than-32-symbols" })),
            new AuditEventWriter(context));

        var result = await service.BootstrapAdminAsync(
            new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Администратор"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains(SystemRoles.Administrator, result.Value!.User.Roles);
        Assert.Contains(SystemPermissions.UsersManage, result.Value.User.Permissions);
        Assert.Equal(4, await context.Roles.CountAsync());
        Assert.Equal(1, await context.Users.CountAsync());
        var auditEvent = await context.AuditEvents.SingleAsync();
        Assert.Equal("auth.bootstrap_admin_created", auditEvent.Action);
        Assert.Equal("auth", auditEvent.Section);
        Assert.Equal("create", auditEvent.ActionKind);
        Assert.DoesNotContain("admin@example.com", auditEvent.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
