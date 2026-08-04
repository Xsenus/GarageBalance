using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Application.Users;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Auth;

public sealed class PostgreSqlUserSecurityMutationTests
{
    [PostgreSqlFact]
    public async Task ConcurrentBootstrap_CreatesExactlyOneAdministrator()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstService = CreateAuthService(firstContext);
        var secondService = CreateAuthService(secondContext);

        var first = firstService.BootstrapAdminAsync(
            new BootstrapAdminRequest("first@example.test", "StrongPass123", "Первый администратор"),
            CancellationToken.None);
        var second = secondService.BootstrapAdminAsync(
            new BootstrapAdminRequest("second@example.test", "StrongPass123", "Второй администратор"),
            CancellationToken.None);
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result.Succeeded);
        var rejected = Assert.Single(results, result => !result.Succeeded);
        Assert.Equal("bootstrap_closed", rejected.ErrorCode);
        await using var verificationContext = database.CreateContext();
        Assert.Equal(1, await verificationContext.Users.CountAsync());
        Assert.Equal(1, await verificationContext.UserRoles.CountAsync(userRole => userRole.Role.Code == SystemRoles.Administrator));
    }

    [PostgreSqlFact]
    public async Task ConcurrentDeactivation_KeepsOneActiveAdministrator()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var firstAdminId = Guid.NewGuid();
        var secondAdminId = Guid.NewGuid();
        await SeedAdministratorsAsync(database, firstAdminId, secondAdminId);
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstService = CreateUserManagementService(firstContext);
        var secondService = CreateUserManagementService(secondContext);

        var first = firstService.UpdateUserAsync(
            firstAdminId,
            new UpdateManagedUserRequest("Первый администратор", [SystemRoles.Administrator], false, null, "Проверка конкурентного отключения"),
            secondAdminId,
            CancellationToken.None);
        var second = secondService.UpdateUserAsync(
            secondAdminId,
            new UpdateManagedUserRequest("Второй администратор", [SystemRoles.Administrator], false, null, "Проверка конкурентного отключения"),
            firstAdminId,
            CancellationToken.None);
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result.Succeeded);
        var rejected = Assert.Single(results, result => !result.Succeeded);
        Assert.Equal("last_admin_required", rejected.ErrorCode);
        await using var verificationContext = database.CreateContext();
        var activeAdministratorCount = await verificationContext.Users
            .CountAsync(user => user.IsActive && user.UserRoles.Any(userRole => userRole.Role.Code == SystemRoles.Administrator));
        Assert.Equal(1, activeAdministratorCount);
    }

    private static AuthService CreateAuthService(GarageBalanceDbContext context) =>
        new(
            new EfUserRepository(context),
            new Pbkdf2PasswordHasher(),
            new PasswordPolicyValidator(),
            new JwtTokenService(Options.Create(new JwtOptions
            {
                Issuer = "GarageBalance.Tests",
                Audience = "GarageBalance.Tests",
                SigningKey = "test-signing-key-that-is-long-enough-32"
            })),
            new AuditEventWriter(context),
            new UserSecurityMutationLock(context));

    private static UserManagementService CreateUserManagementService(GarageBalanceDbContext context) =>
        new(
            new EfUserManagementRepository(context),
            new Pbkdf2PasswordHasher(),
            new PasswordPolicyValidator(),
            new AuditEventWriter(context),
            new UserSecurityMutationLock(context));

    private static async Task SeedAdministratorsAsync(
        PostgreSqlTestDatabase database,
        Guid firstAdminId,
        Guid secondAdminId)
    {
        await using var context = database.CreateContext();
        var administratorRole = new AppRole
        {
            Code = SystemRoles.Administrator,
            Name = "Администратор",
            Permissions = SystemPermissions.Administrator.ToList()
        };
        var firstAdmin = new AppUser
        {
            Id = firstAdminId,
            Email = "first@example.test",
            NormalizedEmail = "FIRST@EXAMPLE.TEST",
            DisplayName = "Первый администратор",
            PasswordHash = "hash"
        };
        var secondAdmin = new AppUser
        {
            Id = secondAdminId,
            Email = "second@example.test",
            NormalizedEmail = "SECOND@EXAMPLE.TEST",
            DisplayName = "Второй администратор",
            PasswordHash = "hash"
        };
        firstAdmin.UserRoles.Add(new AppUserRole { User = firstAdmin, Role = administratorRole });
        secondAdmin.UserRoles.Add(new AppUserRole { User = secondAdmin, Role = administratorRole });
        context.AddRange(administratorRole, firstAdmin, secondAdmin);
        await context.SaveChangesAsync();
    }
}
