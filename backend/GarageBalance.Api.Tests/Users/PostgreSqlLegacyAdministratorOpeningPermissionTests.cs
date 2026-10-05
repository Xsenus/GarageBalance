using System.Text.Json;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Data.Migrations;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GarageBalance.Api.Tests.Users;

public sealed class PostgreSqlLegacyAdministratorOpeningPermissionTests
{
    private const string PreviousMigration = "20260930235429_SeparateServiceReportColumns";
    private static readonly string[] LegacyPermissions =
    [
        "app_releases.manage", "audit.read", "dictionaries.read", "dictionaries.write", "import.run",
        "payments.meter_readings.historical_correct", "payments.read", "payments.write", "reports.read",
        "tariffs.manage", "users.manage"
    ];

    [PostgreSqlFact]
    public async Task Migration_RepairsOnlyLegacyAdministratorAndInvalidatesAssignedSessionsOnce()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(PreviousMigration);
        await using var context = database.CreateContext();
        var admin = CreateRole(SystemRoles.Administrator, LegacyPermissions);
        var accountant = CreateRole(SystemRoles.Accountant, [SystemPermissions.DictionariesRead]);
        var adminUser = CreateUser(admin, "admin@example.test");
        var otherUser = CreateUser(accountant, "other@example.test");
        context.Users.AddRange(adminUser, otherUser);
        await context.SaveChangesAsync();
        var roleVersion = admin.Version;
        var userVersion = adminUser.Version;
        var otherVersion = otherUser.Version;
        var repository = new EfUserRepository(context);
        Assert.True(await repository.IsSessionValidAsync(adminUser.Id, 1, CancellationToken.None));

        await context.Database.MigrateAsync();
        context.ChangeTracker.Clear();
        var repaired = await context.Roles.SingleAsync(role => role.Code == SystemRoles.Administrator);
        var expected = LegacyPermissions.Append(SystemPermissions.OpeningDataAdjust).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, repaired.Permissions);
        Assert.NotEqual(roleVersion, repaired.Version);
        Assert.Equal([SystemPermissions.DictionariesRead], (await context.Roles.SingleAsync(role => role.Code == SystemRoles.Accountant)).Permissions);
        var updatedUser = await context.Users.SingleAsync(user => user.Id == adminUser.Id);
        Assert.Equal(2, updatedUser.SessionVersion);
        Assert.NotEqual(userVersion, updatedUser.Version);
        Assert.Equal(otherVersion, (await context.Users.SingleAsync(user => user.Id == otherUser.Id)).Version);
        Assert.False(await repository.IsSessionValidAsync(adminUser.Id, 1, CancellationToken.None));
        Assert.True(await repository.IsSessionValidAsync(adminUser.Id, 2, CancellationToken.None));
        var loginUser = await repository.FindUserByEmailAsync(adminUser.NormalizedEmail, CancellationToken.None);
        Assert.Contains(SystemPermissions.OpeningDataAdjust, loginUser!.UserRoles.SelectMany(item => item.Role.Permissions));
        var audit = await context.AuditEvents.SingleAsync(item => item.Action == "users.legacy_opening_permission_restored");
        Assert.Equal("users.legacy_opening_permission_restored", audit.Action);
        Assert.Equal(SystemRoles.Administrator, audit.EntityId);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(1, metadata.RootElement.GetProperty("invalidatedSessions").GetInt32());

        // A deployment retry and a rollback/re-upgrade must not revoke sessions again.
        await context.Database.MigrateAsync(PreviousMigration);
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(repaired.Version, (await context.Roles.SingleAsync(role => role.Id == admin.Id)).Version);
        Assert.Equal(2, (await context.Users.SingleAsync(user => user.Id == adminUser.Id)).SessionVersion);
        Assert.Equal(1, await context.AuditEvents.CountAsync(item => item.Action == "users.legacy_opening_permission_restored"));
    }

    [PostgreSqlFact]
    public async Task Migration_PreservesExplicitRoleChoicesExistingPermissionAndOtherRoleCodes()
    {
        foreach (var scenario in new[] { "explicit-choice", "custom-grants", "already-granted", "different-role" })
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync(PreviousMigration);
            await using var context = database.CreateContext();
            var grants = scenario switch
            {
                "custom-grants" => LegacyPermissions.Where(value => value != SystemPermissions.ImportRun).ToArray(),
                "already-granted" => LegacyPermissions.Append(SystemPermissions.OpeningDataAdjust).ToArray(),
                _ => LegacyPermissions
            };
            var role = CreateRole(scenario == "different-role" ? SystemRoles.Accountant : SystemRoles.Administrator, grants);
            var user = CreateUser(role, "preserved@example.test");
            context.Users.Add(user);
            if (scenario == "explicit-choice")
            {
                context.AuditEvents.Add(new AuditEvent
                {
                    Action = "users.role_permissions_updated",
                    EntityType = "app_role",
                    EntityId = SystemRoles.Administrator,
                    Summary = "Изменены права роли"
                });
            }
            await context.SaveChangesAsync();
            var roleVersion = role.Version;
            var userVersion = user.Version;
            await context.Database.MigrateAsync();
            context.ChangeTracker.Clear();
            var retained = await context.Roles.SingleAsync();
            Assert.Equal(grants, retained.Permissions);
            Assert.Equal(roleVersion, retained.Version);
            var retainedUser = await context.Users.SingleAsync();
            Assert.Equal(userVersion, retainedUser.Version);
            Assert.Equal(1, retainedUser.SessionVersion);
            Assert.DoesNotContain(await context.AuditEvents.ToListAsync(), item => item.Action == "users.legacy_opening_permission_restored");
        }
    }

    [PostgreSqlFact]
    public async Task Migration_RollsBackRoleAndSessionChangesWhenAuditInsertFails()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(PreviousMigration);
        await using var context = database.CreateContext();
        var role = CreateRole(SystemRoles.Administrator, LegacyPermissions);
        var user = CreateUser(role, "rollback@example.test");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var auditCount = await context.AuditEvents.CountAsync();
        await context.Database.ExecuteSqlRawAsync("""
            ALTER TABLE audit_events ADD CONSTRAINT reject_permission_repair
            CHECK ("Action" <> 'users.legacy_opening_permission_restored');
            """);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.Database.MigrateAsync());
        context.ChangeTracker.Clear();
        Assert.Equal(LegacyPermissions, (await context.Roles.SingleAsync()).Permissions);
        Assert.Equal(1, (await context.Users.SingleAsync()).SessionVersion);
        Assert.Equal(auditCount, await context.AuditEvents.CountAsync());
        Assert.DoesNotContain("20261005085727_RestoreLegacyAdministratorOpeningDataPermission", await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public void Migration_ContainsOnlyAtomicDataRepairAndPreservesAccessOnDowngrade()
    {
        var migration = new RestoreLegacyAdministratorOpeningDataPermission();
        var operation = Assert.IsType<SqlOperation>(Assert.Single(migration.UpOperations));
        Assert.False(operation.SuppressTransaction);
        Assert.Empty(migration.DownOperations);
    }

    private static AppRole CreateRole(string code, IEnumerable<string> permissions) => new()
    {
        Code = code,
        Name = code,
        Permissions = permissions.ToList()
    };

    private static AppUser CreateUser(AppRole role, string email) => new()
    {
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        DisplayName = "Тестовая учётная запись",
        PasswordHash = "unused-test-hash",
        UserRoles = [new AppUserRole { Role = role }]
    };
}
