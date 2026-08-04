using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Application.Users;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Performance;

public sealed class PostgreSqlUsersAuditSearchIntegrationTests
{
    [PostgreSqlFact]
    public async Task UsersAndAuditSearchUseTrigramIndexesAndKeepLiteralSearchSemantics()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.Users.AddRange(
                Enumerable.Range(0, 250).Select(index => new AppUser
                {
                    Email = $"operator-{index}@example.test",
                    NormalizedEmail = $"OPERATOR-{index}@EXAMPLE.TEST",
                    DisplayName = index == 149 ? "Needle % Accountant" : $"Cooperative operator {index}",
                    PasswordHash = "hash"
                }));
            seedContext.AuditEvents.AddRange(
                Enumerable.Range(0, 500).Select(index => new AuditEvent
                {
                    Action = "settings.updated",
                    Section = "settings",
                    ActionKind = "update",
                    EntityType = "application_setting",
                    EntityId = $"setting-{index}",
                    RelatedGarageNumber = $"G-{index}",
                    RelatedCounterpartyName = index == 349 ? "Needle supplier" : $"Supplier {index}",
                    RelatedDocumentNumber = $"DOC-{index}",
                    Summary = index == 349 ? "Needle audit summary" : $"Changed setting {index}"
                }));
            await seedContext.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var users = new UserManagementService(
                new EfUserManagementRepository(context),
                new Pbkdf2PasswordHasher(),
                new PasswordPolicyValidator(),
                new AuditEventWriter(context),
                new NoOpUserSecurityMutationLock());
            var userPage = await users.GetUsersPageAsync("NEEDLE %", 0, 25, CancellationToken.None);
            var user = Assert.Single(userPage.Items);
            Assert.Equal("operator-149@example.test", user.Email);

            var audit = new AuditService(new EfAuditEventRepository(context));
            var auditPage = await audit.GetEventsPageAsync(
                new AuditEventListRequest(null, null, null, "needle audit", 25, "settings"),
                CancellationToken.None);
            var auditEvent = Assert.Single(auditPage.Items);
            Assert.Equal("setting-349", auditEvent.EntityId);
        }

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var indexNames = await ReadIndexNamesAsync(connection);
        Assert.Contains("IX_app_users_NormalizedEmail_trgm", indexNames);
        Assert.Contains("IX_app_users_DisplayName_trgm", indexNames);
        Assert.Contains("IX_audit_events_SearchText_trgm", indexNames);
        Assert.Contains("IX_audit_events_RelatedGarageNumber_trgm", indexNames);
        Assert.Contains("IX_audit_events_RelatedCounterpartyName_trgm", indexNames);
        Assert.Contains("IX_audit_events_RelatedDocumentNumber_trgm", indexNames);

        Assert.Contains(
            "IX_app_users_DisplayName_trgm",
            await ExplainAsync(
                connection,
                """SELECT "Id" FROM "app_users" WHERE "DisplayName" ILIKE '%Needle%' ESCAPE '\';"""),
            StringComparison.Ordinal);
        Assert.Contains(
            "IX_audit_events_SearchText_trgm",
            await ExplainAsync(
                connection,
                """SELECT "Id" FROM "audit_events" WHERE "SearchText" ILIKE '%needle audit%' ESCAPE '\';"""),
            StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task UsersAndAuditPagesHonorCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var userRepository = new EfUserManagementRepository(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => userRepository.GetUsersPageAsync(null, 0, 25, cancellation.Token));

        var auditRepository = new EfAuditEventRepository(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => auditRepository.GetEventsPageAsync(
                new AuditEventListRequest(null, null, null, null, 25),
                0,
                25,
                cancellation.Token));
    }

    private static async Task<HashSet<string>> ReadIndexNamesAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename IN ('app_users', 'audit_events');
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<string> ExplainAsync(NpgsqlConnection connection, string query)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SET enable_seqscan = off; EXPLAIN (ANALYZE, BUFFERS) {query}";
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
