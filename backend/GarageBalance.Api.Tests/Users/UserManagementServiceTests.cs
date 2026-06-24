using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Application.Users;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Users;

public sealed class UserManagementServiceTests
{
    [Fact]
    public async Task GetRolesAsync_ReturnsSystemRolesWithPermissions()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var roles = await service.GetRolesAsync(CancellationToken.None);

        Assert.Contains(roles, role => role.Code == SystemRoles.Administrator && role.Permissions.Contains(SystemPermissions.UsersManage));
        Assert.Contains(roles, role => role.Code == SystemRoles.Operator && role.Permissions.Contains(SystemPermissions.DictionariesRead));
        Assert.Contains(roles, role => role.Code == SystemRoles.Accountant && role.Permissions.Contains(SystemPermissions.TariffsManage) && role.Permissions.Contains(SystemPermissions.ImportRun));
        Assert.Contains(roles, role => role.Code == SystemRoles.ReportsViewer && role.Permissions.Contains(SystemPermissions.ReportsRead) && !role.Permissions.Contains(SystemPermissions.PaymentsWrite));
    }

    [Fact]
    public async Task CreateUserAsync_CreatesUserWithRolesAndAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateUserAsync(
            new CreateManagedUserRequest("operator@example.com", "Оператор", "StrongPass123", [SystemRoles.Operator]),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("OPERATOR@EXAMPLE.COM", database.Context.Users.Single().NormalizedEmail);
        Assert.Contains(SystemRoles.Operator, result.Value!.Roles);
        Assert.DoesNotContain("StrongPass123", database.Context.Users.Single().PasswordHash);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "users.user_created" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task CreateUserAsync_RejectsDuplicateEmail()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        await service.CreateUserAsync(
            new CreateManagedUserRequest("operator@example.com", "Оператор", "StrongPass123", [SystemRoles.Operator]),
            null,
            CancellationToken.None);

        var result = await service.CreateUserAsync(
            new CreateManagedUserRequest(" OPERATOR@example.com ", "Другой", "StrongPass123", [SystemRoles.Operator]),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("user_email_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateUserAsync_RejectsWeakPassword()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.CreateUserAsync(
            new CreateManagedUserRequest("operator@example.com", "Оператор", "password", [SystemRoles.Operator]),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("password_policy_violation", result.ErrorCode);
        Assert.Empty(database.Context.Users);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateUserAsync_RejectsUnknownRole()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.CreateUserAsync(
            new CreateManagedUserRequest("user@example.com", "Пользователь", "StrongPass123", ["missing-role"]),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("role_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateUserAsync_ChangesRolesAndPassword()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var created = await service.CreateUserAsync(
            new CreateManagedUserRequest("user@example.com", "Пользователь", "StrongPass123", [SystemRoles.Operator]),
            null,
            CancellationToken.None);
        var oldHash = database.Context.Users.Single().PasswordHash;

        var result = await service.UpdateUserAsync(
            created.Value!.Id,
            new UpdateManagedUserRequest("Бухгалтер", [SystemRoles.Accountant], true, "NewStrongPass123"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Бухгалтер", result.Value!.DisplayName);
        Assert.Contains(SystemRoles.Accountant, result.Value.Roles);
        Assert.NotEqual(oldHash, database.Context.Users.Single().PasswordHash);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "users.user_updated");
    }

    [Fact]
    public async Task UpdateUserAsync_PreventsDisablingLastActiveAdministrator()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var admin = await service.CreateUserAsync(
            new CreateManagedUserRequest("admin@example.com", "Администратор", "StrongPass123", [SystemRoles.Administrator]),
            null,
            CancellationToken.None);

        var result = await service.UpdateUserAsync(
            admin.Value!.Id,
            new UpdateManagedUserRequest("Администратор", [SystemRoles.Administrator], false, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("last_admin_required", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateUserAsync_RejectsWeakNewPasswordWithoutChangingHash()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var created = await service.CreateUserAsync(
            new CreateManagedUserRequest("user@example.com", "Пользователь", "StrongPass123", [SystemRoles.Operator]),
            null,
            CancellationToken.None);
        var oldHash = database.Context.Users.Single().PasswordHash;

        var result = await service.UpdateUserAsync(
            created.Value!.Id,
            new UpdateManagedUserRequest("Пользователь", [SystemRoles.Operator], true, "password"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("password_policy_violation", result.ErrorCode);
        Assert.Equal(oldHash, database.Context.Users.Single().PasswordHash);
    }

    [Fact]
    public async Task GetUsersAsync_SearchesByEmailAndDisplayName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        await service.CreateUserAsync(new CreateManagedUserRequest("operator@example.com", "Оператор", "StrongPass123", [SystemRoles.Operator]), null, CancellationToken.None);
        await service.CreateUserAsync(new CreateManagedUserRequest("accountant@example.com", "Бухгалтер", "StrongPass123", [SystemRoles.Accountant]), null, CancellationToken.None);

        var users = await service.GetUsersAsync("account", CancellationToken.None);

        var user = Assert.Single(users);
        Assert.Equal("accountant@example.com", user.Email);
    }

    [Fact]
    public async Task GetUsersAsync_AppliesExplicitLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        for (var index = 0; index < 3; index++)
        {
            var result = await service.CreateUserAsync(
                new CreateManagedUserRequest($"user{index}@example.com", $"Сотрудник {index}", "StrongPass123", [SystemRoles.Operator]),
                null,
                CancellationToken.None);
            Assert.True(result.Succeeded);
        }

        var users = await service.GetUsersAsync(null, CancellationToken.None, 2);

        Assert.Equal(2, users.Count);
    }

    private static UserManagementService CreateService(GarageBalanceDbContext context)
    {
        return new UserManagementService(context, new Pbkdf2PasswordHasher(), new PasswordPolicyValidator());
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestDatabase(SqliteConnection connection, GarageBalanceDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        public GarageBalanceDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new GarageBalanceDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
