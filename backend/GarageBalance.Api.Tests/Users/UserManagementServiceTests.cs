using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Application.Users;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using TestDatabase = GarageBalance.Api.Tests.Common.SqliteTestDatabase;

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
    public async Task UpdateRolePermissionsAsync_UpdatesPermissionsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        await service.GetRolesAsync(CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateRolePermissionsAsync(
            SystemRoles.Operator,
            new UpdateRolePermissionsRequest([SystemPermissions.DictionariesRead, SystemPermissions.ReportsRead]),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(SystemRoles.Operator, result.Value!.Code);
        Assert.Equal([SystemPermissions.DictionariesRead, SystemPermissions.ReportsRead], result.Value.Permissions);
        var role = database.Context.Roles.Single(item => item.Code == SystemRoles.Operator);
        Assert.Equal([SystemPermissions.DictionariesRead, SystemPermissions.ReportsRead], role.Permissions.Order(StringComparer.Ordinal));
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "users.role_permissions_updated");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal("users", auditEvent.Section);
        Assert.Equal("update", auditEvent.ActionKind);
        Assert.Equal("app_role", auditEvent.EntityType);
        Assert.Equal(SystemRoles.Operator, auditEvent.EntityId);
        Assert.Equal("Оператор", auditEvent.EntityDisplayName);
        using var metadata = JsonDocument.Parse(auditEvent.MetadataJson!);
        Assert.Equal(SystemRoles.Operator, metadata.RootElement.GetProperty("roleCode").GetString());
        Assert.Equal("dictionaries.read, reports.read", metadata.RootElement.GetProperty("permissions").GetString());
        Assert.Equal("Права", metadata.RootElement.GetProperty("fieldName").GetString());
        Assert.Equal("dictionaries.read, payments.read, payments.write", metadata.RootElement.GetProperty("oldValue").GetString());
        Assert.Equal("dictionaries.read, reports.read", metadata.RootElement.GetProperty("newValue").GetString());
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_DoesNotWriteAuditWhenPermissionsAreUnchanged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        await service.GetRolesAsync(CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateRolePermissionsAsync(
            SystemRoles.Operator,
            new UpdateRolePermissionsRequest([SystemPermissions.PaymentsWrite, SystemPermissions.DictionariesRead, SystemPermissions.PaymentsRead]),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_RejectsUnknownPermission()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.UpdateRolePermissionsAsync(
            SystemRoles.Operator,
            new UpdateRolePermissionsRequest([SystemPermissions.DictionariesRead, "unknown.permission"]),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("permission_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_RejectsAdministratorWithoutUsersManage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.UpdateRolePermissionsAsync(
            SystemRoles.Administrator,
            new UpdateRolePermissionsRequest([SystemPermissions.DictionariesRead]),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("administrator_users_manage_required", result.ErrorCode);
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
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "users.user_created");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal(result.Value.Id.ToString(), auditEvent.EntityId);
        Assert.Equal("users", auditEvent.Section);
        Assert.Equal("create", auditEvent.ActionKind);
        Assert.Equal("Оператор", auditEvent.EntityDisplayName);
        Assert.Equal(result.Value.Id.ToString(), auditEvent.RelatedCounterpartyId);
        Assert.Equal("Оператор", auditEvent.RelatedCounterpartyName);
        Assert.Contains("operator", auditEvent.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("operator@example.com", auditEvent.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operator@example.com", auditEvent.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StrongPass123", auditEvent.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("StrongPass123", auditEvent.MetadataJson, StringComparison.Ordinal);
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
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "users.user_updated");
        Assert.Equal("users", auditEvent.Section);
        Assert.Equal("update", auditEvent.ActionKind);
        Assert.Equal(created.Value.Id.ToString(), auditEvent.EntityId);
        Assert.Equal("Бухгалтер", auditEvent.EntityDisplayName);
        using var metadata = JsonDocument.Parse(auditEvent.MetadataJson!);
        var changedFields = metadata.RootElement.GetProperty("changedFields").GetString();
        Assert.Contains("Роли", changedFields, StringComparison.Ordinal);
        Assert.Contains("Смена учетных данных", changedFields, StringComparison.Ordinal);
        Assert.Equal("accountant", metadata.RootElement.GetProperty("roles").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("credentialsChanged").GetString());
        Assert.DoesNotContain("NewStrongPass123", auditEvent.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("NewStrongPass123", auditEvent.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateUserAsync_DoesNotWriteAuditWhenNormalizedValuesAreUnchanged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var created = await service.CreateUserAsync(
            new CreateManagedUserRequest("user@example.com", "User", "StrongPass123", [SystemRoles.Operator, SystemRoles.ReportsViewer]),
            null,
            CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateUserAsync(
            created.Value!.Id,
            new UpdateManagedUserRequest(" User ", [SystemRoles.ReportsViewer, SystemRoles.Operator], true, null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("User", result.Value!.DisplayName);
        Assert.Contains(SystemRoles.Operator, result.Value.Roles);
        Assert.Contains(SystemRoles.ReportsViewer, result.Value.Roles);
        Assert.Empty(database.Context.AuditEvents);
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
    public async Task UpdateUserAsync_RequiresReasonWhenDeactivatingUser()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var created = await service.CreateUserAsync(
            new CreateManagedUserRequest("user@example.com", "User", "StrongPass123", [SystemRoles.Operator]),
            null,
            CancellationToken.None);

        var result = await service.UpdateUserAsync(
            created.Value!.Id,
            new UpdateManagedUserRequest("User", [SystemRoles.Operator], false, null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("user_deactivation_reason_required", result.ErrorCode);
        Assert.True(database.Context.Users.Single().IsActive);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "users.user_updated");
    }

    [Fact]
    public async Task UpdateUserAsync_WritesDeactivationReasonToAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateUserAsync(
            new CreateManagedUserRequest("user@example.com", "User", "StrongPass123", [SystemRoles.Operator]),
            null,
            CancellationToken.None);

        var result = await service.UpdateUserAsync(
            created.Value!.Id,
            new UpdateManagedUserRequest("User", [SystemRoles.Operator], false, null, "Access no longer needed"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsActive);
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "users.user_updated");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Contains("Access no longer needed", auditEvent.Summary, StringComparison.Ordinal);
        Assert.Contains("Access no longer needed", auditEvent.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreUserAsync_ReactivatesDisabledUserAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateUserAsync(
            new CreateManagedUserRequest("user@example.com", "User", "StrongPass123", [SystemRoles.Operator]),
            null,
            CancellationToken.None);
        var disabled = await service.UpdateUserAsync(
            created.Value!.Id,
            new UpdateManagedUserRequest("User", [SystemRoles.Operator], false, null, "Access no longer needed"),
            null,
            CancellationToken.None);
        Assert.True(disabled.Succeeded);

        var result = await service.RestoreUserAsync(created.Value.Id, actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsActive);
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "users.user_restored");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal("users", auditEvent.Section);
        Assert.Equal("restore", auditEvent.ActionKind);
        Assert.Equal(created.Value.Id.ToString(), auditEvent.EntityId);
        Assert.Contains("Восстановлен пользователь User", auditEvent.Summary, StringComparison.Ordinal);
        Assert.Contains("True", auditEvent.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreUserAsync_ReturnsNotFoundForActiveUser()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var created = await service.CreateUserAsync(
            new CreateManagedUserRequest("user@example.com", "User", "StrongPass123", [SystemRoles.Operator]),
            null,
            CancellationToken.None);

        var result = await service.RestoreUserAsync(created.Value!.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("user_not_found", result.ErrorCode);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "users.user_restored");
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

    [Fact]
    public async Task GetUsersPageAsync_ReturnsTotalCountAndRequestedSlice()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        for (var index = 0; index < 3; index++)
        {
            var result = await service.CreateUserAsync(
                new CreateManagedUserRequest($"user{index}@example.com", $"РЎРѕС‚СЂСѓРґРЅРёРє {index}", "StrongPass123", [SystemRoles.Operator]),
                null,
                CancellationToken.None);
            Assert.True(result.Succeeded);
        }

        var page = await service.GetUsersPageAsync(null, 1, 1, CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(1, page.Offset);
        Assert.Equal(1, page.Limit);
        var user = Assert.Single(page.Items);
        Assert.Equal("user1@example.com", user.Email);
    }

    private static UserManagementService CreateService(GarageBalanceDbContext context)
    {
        return new UserManagementService(context, new Pbkdf2PasswordHasher(), new PasswordPolicyValidator(), new AuditEventWriter(context));
    }

}
