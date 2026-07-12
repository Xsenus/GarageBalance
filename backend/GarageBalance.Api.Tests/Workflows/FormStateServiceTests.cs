using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Workflows;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using TestDatabase = GarageBalance.Api.Tests.Common.SqliteTestDatabase;

namespace GarageBalance.Api.Tests.Workflows;

public sealed class FormStateServiceTests
{
    [Fact]
    public async Task UpsertStateAsync_CreatesAndUpdatesStateWithAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        using var firstPayload = JsonDocument.Parse("""{"rows":[{"id":"1","value":"100"}]}""");
        using var secondPayload = JsonDocument.Parse("""{"rows":[{"id":"1","value":"150"}]}""");

        var created = await service.UpsertStateAsync(
            "payments-prototype",
            new UpsertFormStateRequest(firstPayload.RootElement, "Первое сохранение формы платежей."),
            actorUserId,
            CancellationToken.None);
        var updated = await service.UpsertStateAsync(
            "payments-prototype",
            new UpsertFormStateRequest(secondPayload.RootElement, "Повторное сохранение формы платежей."),
            actorUserId,
            CancellationToken.None);
        var loaded = await service.GetStateAsync(" payments-prototype ", CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.True(updated.Succeeded);
        Assert.Equal("150", updated.Value!.Payload.GetProperty("rows")[0].GetProperty("value").GetString());
        Assert.Equal(actorUserId, updated.Value.UpdatedByUserId);
        Assert.NotNull(loaded);
        Assert.Equal("150", loaded.Payload.GetProperty("rows")[0].GetProperty("value").GetString());
        Assert.Equal(2, await database.Context.AuditEvents.CountAsync());
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "workflows.form_state_created");
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "workflows.form_state_updated");
    }

    [Fact]
    public async Task UpsertStateAsync_RejectsInvalidScope()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        using var payload = JsonDocument.Parse("""{"ok":true}""");

        var result = await service.UpsertStateAsync(
            " ",
            new UpsertFormStateRequest(payload.RootElement),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("form_state_scope_invalid", result.ErrorCode);
        Assert.Empty(database.Context.FormStates);
    }

    [Fact]
    public async Task GetStateAsync_ReturnsNullForMissingState()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.GetStateAsync("contractors-prototype", CancellationToken.None);

        Assert.Null(result);
    }

    private static FormStateService CreateService(GarageBalanceDbContext context)
    {
        return new FormStateService(new EfFormStateRepository(context), new AuditEventWriter(context));
    }

}
