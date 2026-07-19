using System.Data.Common;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Audit;

public sealed class PostgreSqlAuditEventPageQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task AuditPageLoadsCountRowsAndActorsInOneCommandForEveryPageShape()
    {
        var actor = new AppUser
        {
            Email = "audit-postgres@example.test",
            NormalizedEmail = "AUDIT-POSTGRES@EXAMPLE.TEST",
            DisplayName = "PostgreSQL audit operator",
            PasswordHash = "hash"
        };
        var testSection = $"audit-page-{Guid.NewGuid():N}";
        var firstCreatedAt = new DateTimeOffset(2044, 5, 10, 9, 0, 0, TimeSpan.Zero);
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.Users.Add(actor);
            seedContext.AuditEvents.AddRange(
                Enumerable.Range(0, 5).Select(index => new AuditEvent
                {
                    CreatedAtUtc = firstCreatedAt.AddMinutes(index),
                    ActorUserId = index == 2 ? actor.Id : null,
                    Action = $"finance.payment_{index}",
                    Section = testSection,
                    ActionKind = index == 2 ? "create" : "update",
                    EntityType = index == 2 ? "financial_operation" : "garage",
                    EntityId = $"entity-{index}",
                    EntityDisplayName = $"Entity {index}",
                    RelatedGarageId = $"garage-{index}",
                    RelatedGarageNumber = index == 2 ? "A-204" : $"B-{index}",
                    RelatedAccountingMonth = index == 2 ? "2044-05" : "2044-04",
                    RelatedCounterpartyId = $"counterparty-{index}",
                    RelatedCounterpartyName = index == 2 ? "Target supplier" : $"Supplier {index}",
                    RelatedDocumentId = $"document-{index}",
                    RelatedDocumentNumber = index == 2 ? "DOC-TARGET" : $"DOC-{index}",
                    Summary = index == 2 ? "Created target payment" : $"Updated garage {index}",
                    MetadataJson = index == 2 ? "{\"fieldName\":\"amount\"}" : null
                }));
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var service = new AuditService(new EfAuditEventRepository(context));

        var page = await service.GetEventsPageAsync(
            new AuditEventListRequest(null, null, null, null, 2, testSection, Offset: 1),
            CancellationToken.None);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(["finance.payment_3", "finance.payment_2"], page.Items.Select(item => item.Action));
        Assert.Null(page.Items[0].ActorDisplayName);
        Assert.Equal(actor.DisplayName, page.Items[1].ActorDisplayName);
        Assert.Equal(actor.Email, page.Items[1].ActorEmail);
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var beyondEnd = await service.GetEventsPageAsync(
            new AuditEventListRequest(null, null, null, null, 2, testSection, Offset: 20),
            CancellationToken.None);

        Assert.Equal(5, beyondEnd.TotalCount);
        Assert.Empty(beyondEnd.Items);
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var empty = await service.GetEventsPageAsync(
            new AuditEventListRequest(null, null, "missing.action", null, 10, testSection),
            CancellationToken.None);

        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Items);
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var filtered = await service.GetEventsPageAsync(
            new AuditEventListRequest(
                firstCreatedAt.AddMinutes(1),
                firstCreatedAt.AddMinutes(3),
                null,
                "target payment",
                10,
                testSection,
                "create",
                "financial_operation",
                actor.Id,
                null,
                0,
                "A-204",
                "2044-05",
                "Target supplier",
                "DOC-TARGET"),
            CancellationToken.None);

        var filteredEvent = Assert.Single(filtered.Items);
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal("finance.payment_2", filteredEvent.Action);
        Assert.Equal("amount", filteredEvent.FieldName);
        AssertSingleCombinedCommand(capture);
    }

    private static void AssertSingleCombinedCommand(ReaderCommandCapture capture)
    {
        var command = Assert.Single(capture.Commands);
        Assert.Contains("COUNT(*)", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("audit_events", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("app_users", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", command, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ReaderCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
