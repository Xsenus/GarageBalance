using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace GarageBalance.Api.Tests.Audit;

public sealed class AuditServiceTests
{
    [Fact]
    public async Task GetEventsAsync_ReturnsLatestEventsFirstAndFiltersBySearch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        database.Context.AuditEvents.AddRange(
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero),
                Action = "finance.income_created",
                EntityType = "financial_operation",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Поступление по гаражу 12."
            },
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero),
                Action = "import.access_dry_run",
                EntityType = "access_import_run",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Проверка файла Access."
            });
        await database.Context.SaveChangesAsync();

        var result = await service.GetEventsAsync(new AuditEventListRequest(null, null, null, "access"), CancellationToken.None);

        var auditEvent = Assert.Single(result);
        Assert.Equal("import.access_dry_run", auditEvent.Action);
        Assert.Equal("Проверка файла Access.", auditEvent.Summary);
    }

    [Fact]
    public async Task GetEventsAsync_FiltersByDateAndAction()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        database.Context.AuditEvents.AddRange(
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero),
                Action = "auth.login_success",
                EntityType = "user",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Вход пользователя."
            },
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero),
                Action = "users.user_updated",
                EntityType = "app_user",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Пользователь обновлен."
            });
        await database.Context.SaveChangesAsync();

        var result = await service.GetEventsAsync(
            new AuditEventListRequest(
                new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero),
                "users.user_updated",
                null),
            CancellationToken.None);

        var auditEvent = Assert.Single(result);
        Assert.Equal("users.user_updated", auditEvent.Action);
    }

    [Fact]
    public async Task GetEventsAsync_FiltersBySectionActionKindAndEntityType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        database.Context.AuditEvents.AddRange(
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero),
                Action = "dictionary.owner_updated",
                EntityType = "owner",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Изменен владелец: было Иванов; стало Петров."
            },
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero),
                Action = "dictionary.owner_created",
                EntityType = "owner",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Создан владелец."
            },
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero),
                Action = "finance.income_updated",
                EntityType = "financial_operation",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Изменено поступление."
            });
        await database.Context.SaveChangesAsync();

        var result = await service.GetEventsAsync(
            new AuditEventListRequest(null, null, null, null, null, "dictionary", "update", "owner"),
            CancellationToken.None);

        var auditEvent = Assert.Single(result);
        Assert.Equal("dictionary.owner_updated", auditEvent.Action);
        Assert.Equal("owner", auditEvent.EntityType);
    }

    [Fact]
    public async Task GetEventsAsync_FiltersByActorAndQuickFilter()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        var targetActorId = Guid.NewGuid();
        var anotherActorId = Guid.NewGuid();
        database.Context.AuditEvents.AddRange(
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero),
                ActorUserId = targetActorId,
                Action = "dictionary.owner_archived",
                EntityType = "owner",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Владелец архивирован."
            },
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero),
                ActorUserId = anotherActorId,
                Action = "dictionary.owner_archived",
                EntityType = "owner",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Владелец архивирован другим пользователем."
            },
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero),
                ActorUserId = targetActorId,
                Action = "finance.income_created",
                EntityType = "financial_operation",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Поступление создано."
            });
        await database.Context.SaveChangesAsync();

        var deletedEvents = await service.GetEventsAsync(
            new AuditEventListRequest(null, null, null, null, null, null, null, null, targetActorId, "deletions"),
            CancellationToken.None);
        var financialEvents = await service.GetEventsAsync(
            new AuditEventListRequest(null, null, null, null, null, null, null, null, targetActorId, "financial"),
            CancellationToken.None);

        var deletedEvent = Assert.Single(deletedEvents);
        Assert.Equal("dictionary.owner_archived", deletedEvent.Action);
        Assert.Equal(targetActorId, deletedEvent.ActorUserId);
        var financialEvent = Assert.Single(financialEvents);
        Assert.Equal("finance.income_created", financialEvent.Action);
        Assert.Equal(targetActorId, financialEvent.ActorUserId);
    }

    [Fact]
    public async Task GetEventsAsync_AppliesLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        for (var index = 0; index < 4; index++)
        {
            database.Context.AuditEvents.Add(new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 20 + index, 10, 0, 0, TimeSpan.Zero),
                Action = $"audit.event_{index + 1}",
                EntityType = "audit_event",
                EntityId = Guid.NewGuid().ToString(),
                Summary = $"Событие {index + 1}."
            });
        }

        await database.Context.SaveChangesAsync();

        var result = await service.GetEventsAsync(new AuditEventListRequest(null, null, null, null, 2), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("audit.event_4", result[0].Action);
        Assert.Equal("audit.event_3", result[1].Action);
    }

    [Fact]
    public async Task GetEventsPageAsync_ReturnsTotalCountAndRequestedPage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        for (var index = 0; index < 5; index++)
        {
            database.Context.AuditEvents.Add(new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 20 + index, 10, 0, 0, TimeSpan.Zero),
                Action = $"finance.income_created_{index + 1}",
                EntityType = "financial_operation",
                EntityId = Guid.NewGuid().ToString(),
                Summary = $"Поступление {index + 1}."
            });
        }

        await database.Context.SaveChangesAsync();

        var page = await service.GetEventsPageAsync(new AuditEventListRequest(null, null, null, null, 2, "finance", null, null, null, null, 2), CancellationToken.None);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Offset);
        Assert.Equal(2, page.Limit);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("finance.income_created_3", page.Items[0].Action);
        Assert.Equal("finance.income_created_2", page.Items[1].Action);
    }

    [Fact]
    public async Task GetEventsAsync_MasksSensitiveValuesInSummaryAndEntityId()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        database.Context.AuditEvents.Add(new AuditEvent
        {
            CreatedAtUtc = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero),
            Action = "auth.login_failed",
            EntityType = "login_email",
            EntityId = "owner@example.com",
            Summary = "Login owner@example.com failed: password=Secret123, token: abc.def.ghi, card 40817810507220051060, Bearer eyJhbGciOi."
        });
        await database.Context.SaveChangesAsync();

        var result = await service.GetEventsAsync(new AuditEventListRequest(null, null, null, "owner@example.com"), CancellationToken.None);

        var auditEvent = Assert.Single(result);
        Assert.Equal("[email скрыт]", auditEvent.EntityId);
        Assert.Contains("[email скрыт]", auditEvent.Summary);
        Assert.Contains("password=[секрет скрыт]", auditEvent.Summary);
        Assert.Contains("token: [секрет скрыт]", auditEvent.Summary);
        Assert.Contains("[номер скрыт]", auditEvent.Summary);
        Assert.Contains("Bearer [token скрыт]", auditEvent.Summary);
        Assert.DoesNotContain("owner@example.com", auditEvent.Summary);
        Assert.DoesNotContain("40817810507220051060", auditEvent.Summary);
        Assert.DoesNotContain("Secret123", auditEvent.Summary);
    }

    [Fact]
    public async Task GetEventsAsync_ReturnsStructuredAuditFieldsFromMaskedSummary()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        database.Context.AuditEvents.Add(new AuditEvent
        {
            CreatedAtUtc = new DateTimeOffset(2026, 6, 24, 10, 0, 0, TimeSpan.Zero),
            Action = "finance.income_updated",
            EntityType = "financial_operation",
            EntityId = Guid.NewGuid().ToString(),
            Summary = "Изменено поступление: поле Сумма: было 300,00; стало 450,00. Причина: Исправлена сумма."
        });
        await database.Context.SaveChangesAsync();

        var storedEvent = await database.Context.AuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("finance", storedEvent.Section);
        Assert.Equal("update", storedEvent.ActionKind);

        var result = await service.GetEventsAsync(new AuditEventListRequest(null, null, null, null), CancellationToken.None);

        var auditEvent = Assert.Single(result);
        Assert.Equal("finance", auditEvent.Section);
        Assert.Equal("update", auditEvent.ActionKind);
        Assert.Equal("Сумма", auditEvent.FieldName);
        Assert.Equal("300,00", auditEvent.OldValue);
        Assert.Equal("450,00", auditEvent.NewValue);
        Assert.Equal("Исправлена сумма", auditEvent.Reason);
    }

    [Fact]
    public async Task GetEventsAsync_ReturnsMaskedMetadata()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        database.Context.AuditEvents.Add(new AuditEvent
        {
            CreatedAtUtc = new DateTimeOffset(2026, 6, 24, 11, 0, 0, TimeSpan.Zero),
            Action = "auth.login_failed",
            EntityType = "login_email",
            EntityId = "owner@example.com",
            Summary = "Неуспешный вход.",
            MetadataJson = """
            {
              "reason": "invalid_password",
              "entityDisplayName": "Garage 12",
              "garageId": "garage-12",
              "garageNumber": "12",
              "accountingMonth": "2026-06",
              "supplierId": "supplier-1",
              "supplierName": "Energy Supplier",
              "paymentId": "payment-1",
              "paymentNumber": "PAY-2026-06-12",
              "email": "owner@example.com",
              "failedAttempts": 3,
              "apiToken": "secret-token-value"
            }
            """
        });
        await database.Context.SaveChangesAsync();

        var storedEvent = await database.Context.AuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("Garage 12", storedEvent.EntityDisplayName);
        Assert.Equal("garage-12", storedEvent.RelatedGarageId);
        Assert.Equal("12", storedEvent.RelatedGarageNumber);
        Assert.Equal("2026-06", storedEvent.RelatedAccountingMonth);
        Assert.Equal("supplier-1", storedEvent.RelatedCounterpartyId);
        Assert.Equal("Energy Supplier", storedEvent.RelatedCounterpartyName);
        Assert.Equal("payment-1", storedEvent.RelatedDocumentId);
        Assert.Equal("PAY-2026-06-12", storedEvent.RelatedDocumentNumber);

        var result = await service.GetEventsAsync(new AuditEventListRequest(null, null, null, null), CancellationToken.None);

        var auditEvent = Assert.Single(result);
        Assert.NotNull(auditEvent.Metadata);
        Assert.Equal("Garage 12", auditEvent.EntityDisplayName);
        Assert.Equal("garage-12", auditEvent.RelatedGarageId);
        Assert.Equal("12", auditEvent.RelatedGarageNumber);
        Assert.Equal("2026-06", auditEvent.RelatedAccountingMonth);
        Assert.Equal("supplier-1", auditEvent.RelatedCounterpartyId);
        Assert.Equal("Energy Supplier", auditEvent.RelatedCounterpartyName);
        Assert.Equal("payment-1", auditEvent.RelatedDocumentId);
        Assert.Equal("PAY-2026-06-12", auditEvent.RelatedDocumentNumber);
        Assert.Equal("invalid_password", auditEvent.Metadata["reason"]);
        Assert.Equal("[email скрыт]", auditEvent.Metadata["email"]);
        Assert.Equal("3", auditEvent.Metadata["failedAttempts"]);
        Assert.Equal("[секрет скрыт]", auditEvent.Metadata["apiToken"]);
        Assert.DoesNotContain("owner@example.com", auditEvent.Metadata.Values);
        Assert.DoesNotContain("secret-token-value", auditEvent.Metadata.Values);
    }

    [Fact]
    public async Task GetEventsAsync_PrefersStoredRelatedFieldsOverMetadataFallback()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        database.Context.AuditEvents.Add(new AuditEvent
        {
            CreatedAtUtc = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero),
            Action = "finance.payment_updated",
            Section = "stored-section",
            ActionKind = "stored-kind",
            EntityType = "financial_operation",
            EntityId = Guid.NewGuid().ToString(),
            EntityDisplayName = "Stored operation",
            RelatedGarageId = "stored-garage",
            RelatedGarageNumber = "101",
            RelatedAccountingMonth = "2026-07",
            RelatedCounterpartyId = "stored-counterparty",
            RelatedCounterpartyName = "Stored counterparty",
            RelatedDocumentId = "stored-payment",
            RelatedDocumentNumber = "PAY-STORED",
            Summary = "Operation updated.",
            MetadataJson = """
            {
              "entityDisplayName": "Metadata operation",
              "garageId": "metadata-garage",
              "garageNumber": "12",
              "accountingMonth": "2026-06",
              "supplierId": "metadata-counterparty",
              "supplierName": "Metadata counterparty",
              "paymentId": "metadata-payment",
              "paymentNumber": "PAY-METADATA"
            }
            """
        });
        await database.Context.SaveChangesAsync();

        var result = await service.GetEventsAsync(new AuditEventListRequest(null, null, null, null), CancellationToken.None);

        var auditEvent = Assert.Single(result);
        Assert.Equal("stored-section", auditEvent.Section);
        Assert.Equal("stored-kind", auditEvent.ActionKind);
        Assert.Equal("Stored operation", auditEvent.EntityDisplayName);
        Assert.Equal("stored-garage", auditEvent.RelatedGarageId);
        Assert.Equal("101", auditEvent.RelatedGarageNumber);
        Assert.Equal("2026-07", auditEvent.RelatedAccountingMonth);
        Assert.Equal("stored-counterparty", auditEvent.RelatedCounterpartyId);
        Assert.Equal("Stored counterparty", auditEvent.RelatedCounterpartyName);
        Assert.Equal("stored-payment", auditEvent.RelatedDocumentId);
        Assert.Equal("PAY-STORED", auditEvent.RelatedDocumentNumber);
    }

    [Fact]
    public async Task GetEventAsync_ReturnsMaskedEventById()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        var id = Guid.NewGuid();
        database.Context.AuditEvents.Add(new AuditEvent
        {
            Id = id,
            CreatedAtUtc = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero),
            Action = "auth.login_failed",
            EntityType = "login_email",
            EntityId = "owner@example.com",
            Summary = "Login owner@example.com failed: password=Secret123"
        });
        await database.Context.SaveChangesAsync();

        var auditEvent = await service.GetEventAsync(id, CancellationToken.None);

        Assert.NotNull(auditEvent);
        Assert.Equal(id, auditEvent.Id);
        Assert.Equal("[email скрыт]", auditEvent.EntityId);
        Assert.Contains("[email скрыт]", auditEvent.Summary);
        Assert.DoesNotContain("owner@example.com", auditEvent.Summary);
        Assert.DoesNotContain("Secret123", auditEvent.Summary);
    }

    [Fact]
    public async Task GetEventAsync_ReturnsNullWhenEventDoesNotExist()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);

        var auditEvent = await service.GetEventAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(auditEvent);
    }

    [Fact]
    public async Task ExportEventsCsvAsync_UsesFiltersAndMaskedCsvValues()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        database.Context.AuditEvents.AddRange(
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero),
                Action = "auth.login_failed",
                EntityType = "login_email",
                EntityId = "owner@example.com",
                Summary = "Login owner@example.com failed, password=Secret123 \"quoted\""
            },
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 23, 9, 0, 0, TimeSpan.Zero),
                Action = "finance.income_created",
                EntityType = "financial_operation",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Поступление по гаражу 12."
            });
        await database.Context.SaveChangesAsync();

        var export = await service.ExportEventsCsvAsync(new AuditEventListRequest(null, null, "auth.login_failed", null), CancellationToken.None);
        var csv = Encoding.UTF8.GetString(export.Content);

        Assert.StartsWith("audit-events-", export.FileName, StringComparison.Ordinal);
        Assert.EndsWith(".csv", export.FileName, StringComparison.Ordinal);
        Assert.Equal("text/csv; charset=utf-8", export.ContentType);
        Assert.Contains("createdAtUtc,actorUserId,section,actionKind,action,entityType,entityId,entityDisplayName,relatedGarageId,relatedGarageNumber,relatedAccountingMonth,relatedCounterpartyId,relatedCounterpartyName,relatedDocumentId,relatedDocumentNumber,fieldName,oldValue,newValue,reason,metadata,summary", csv);
        Assert.Contains("auth,fail,auth.login_failed", csv);
        Assert.Contains("auth.login_failed", csv);
        Assert.Contains("\"Login [email скрыт] failed, password=[секрет скрыт] \"\"quoted\"\"\"", csv);
        Assert.DoesNotContain("finance.income_created", csv);
        Assert.DoesNotContain("owner@example.com", csv);
        Assert.DoesNotContain("Secret123", csv);
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
