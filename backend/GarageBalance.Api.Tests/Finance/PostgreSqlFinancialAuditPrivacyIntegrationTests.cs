using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFinancialAuditPrivacyIntegrationTests
{
    [PostgreSqlFact]
    public async Task FinancialCorrectionAudit_PersistsActorAndMasksPersonalAndSecretText()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        Guid incomeTypeId;
        await using (var seedContext = database.CreateContext())
        {
            var garage = new Garage { Number = "PG-AUDIT-PRIVACY", PeopleCount = 1, FloorCount = 1 };
            var incomeType = new IncomeType { Name = "Проверка аудита" };
            seedContext.AddRange(garage, incomeType);
            await seedContext.SaveChangesAsync();
            garageId = garage.Id;
            incomeTypeId = incomeType.Id;
        }

        var actorUserId = Guid.NewGuid();
        const string sensitiveComment = "Телефон +7 (999) 111-22-33; address=ул. Морская, 10; password=Secret123";
        const string sensitiveReason = "паспорт: 1234 567890; token=raw-token";
        await using (var commandContext = database.CreateContext())
        {
            var service = FinanceServiceTestFactory.Create(commandContext);
            var created = await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    garageId,
                    incomeTypeId,
                    new DateOnly(2026, 7, 19),
                    new DateOnly(2026, 7, 1),
                    100m,
                    "PG-AUDIT-1",
                    null),
                actorUserId,
                CancellationToken.None);
            Assert.True(created.Succeeded, created.ErrorMessage);

            var updated = await service.UpdateIncomeAsync(
                created.Value!.Id,
                new CreateIncomeOperationRequest(
                    garageId,
                    incomeTypeId,
                    new DateOnly(2026, 7, 19),
                    new DateOnly(2026, 7, 1),
                    120m,
                    "PG-AUDIT-1",
                    sensitiveComment),
                actorUserId,
                CancellationToken.None);
            Assert.True(updated.Succeeded, updated.ErrorMessage);

            var canceled = await service.CancelOperationAsync(
                created.Value.Id,
                new CancelFinanceEntryRequest(sensitiveReason),
                actorUserId,
                CancellationToken.None);
            Assert.True(canceled.Succeeded, canceled.ErrorMessage);

            var restored = await service.RestoreOperationAsync(created.Value.Id, actorUserId, CancellationToken.None);
            Assert.True(restored.Succeeded, restored.ErrorMessage);
        }

        await using var assertionContext = database.CreateContext();
        var correctionEvents = await assertionContext.AuditEvents
            .Where(auditEvent => auditEvent.Action == "finance.income_updated" ||
                auditEvent.Action == "finance.operation_canceled" ||
                auditEvent.Action == "finance.operation_restored")
            .OrderBy(auditEvent => auditEvent.CreatedAtUtc)
            .ToListAsync();

        Assert.Equal(3, correctionEvents.Count);
        Assert.All(correctionEvents, auditEvent =>
        {
            Assert.Equal(actorUserId, auditEvent.ActorUserId);
            var persistedText = $"{auditEvent.Summary}\n{auditEvent.MetadataJson}";
            Assert.DoesNotContain("+7 (999) 111-22-33", persistedText, StringComparison.Ordinal);
            Assert.DoesNotContain("ул. Морская", persistedText, StringComparison.Ordinal);
            Assert.DoesNotContain("1234 567890", persistedText, StringComparison.Ordinal);
            Assert.DoesNotContain("Secret123", persistedText, StringComparison.Ordinal);
            Assert.DoesNotContain("raw-token", persistedText, StringComparison.Ordinal);
        });
        Assert.Contains(correctionEvents, auditEvent => auditEvent.Summary.Contains("[телефон скрыт]", StringComparison.Ordinal));
        Assert.Contains(correctionEvents, auditEvent => auditEvent.Summary.Contains("[персональные данные скрыты]", StringComparison.Ordinal));
        Assert.Contains(correctionEvents, auditEvent => auditEvent.Summary.Contains("[секрет скрыт]", StringComparison.Ordinal));
    }
}
