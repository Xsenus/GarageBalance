using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using TestDatabase = GarageBalance.Api.Tests.Common.SqliteTestDatabase;

namespace GarageBalance.Api.Tests.Reports;

public sealed class GarageReportQuickListServiceTests
{
    [Fact]
    public async Task CreateUpdateDelete_PersistsMembershipAndAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var garages = await AddGaragesAsync(database.Context, "12", "7", "101");
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();

        var created = await service.CreateAsync(
            new UpsertGarageReportQuickListRequest("  Северный   ряд  ", [garages[0].Id, garages[1].Id, garages[1].Id]),
            actorUserId,
            CancellationToken.None);
        var updated = await service.UpdateAsync(
            created.Value!.Id,
            new UpsertGarageReportQuickListRequest("Северные гаражи", [garages[1].Id, garages[2].Id]),
            actorUserId,
            CancellationToken.None);
        var loaded = await service.GetAllAsync(CancellationToken.None);
        var deleted = await service.DeleteAsync(
            created.Value.Id,
            new DeleteGarageReportQuickListRequest("Список больше не используется"),
            actorUserId,
            CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.Equal("Северный ряд", created.Value.Name);
        Assert.Equal(2, created.Value.Garages.Count);
        Assert.True(updated.Succeeded);
        Assert.Equal("Северные гаражи", updated.Value!.Name);
        Assert.Equal([garages[2].Id, garages[1].Id], updated.Value.Garages.Select(item => item.GarageId));
        var loadedItem = Assert.Single(loaded);
        Assert.Equal(updated.Value.Id, loadedItem.Id);
        Assert.Equal(updated.Value.Name, loadedItem.Name);
        Assert.Equal(updated.Value.Garages, loadedItem.Garages);
        Assert.True(deleted.Succeeded);
        Assert.Empty(await service.GetAllAsync(CancellationToken.None));
        var archived = Assert.Single(database.Context.GarageReportQuickLists);
        Assert.True(archived.IsArchived);
        Assert.Equal(actorUserId, archived.ArchivedByUserId);
        Assert.Equal(3, await database.Context.AuditEvents.CountAsync());
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "reports.garage_quick_list_created");
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "reports.garage_quick_list_updated");
        Assert.Contains(database.Context.AuditEvents, item =>
            item.Action == "reports.garage_quick_list_deleted"
            && item.Summary.Contains("Список больше не используется"));
    }

    [Theory]
    [InlineData("", "garage_quick_list_name_required")]
    [InlineData("   ", "garage_quick_list_name_required")]
    public async Task Create_RejectsEmptyName(string name, string expectedCode)
    {
        await using var database = await TestDatabase.CreateAsync();
        var garage = Assert.Single(await AddGaragesAsync(database.Context, "1"));
        var service = CreateService(database.Context);

        var result = await service.CreateAsync(
            new UpsertGarageReportQuickListRequest(name, [garage.Id]),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Empty(database.Context.GarageReportQuickLists);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task Create_RejectsDuplicateNameAndMissingOrArchivedGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var garages = await AddGaragesAsync(database.Context, "1", "2");
        garages[1].IsArchived = true;
        await database.Context.SaveChangesAsync();
        var service = CreateService(database.Context);
        var first = await service.CreateAsync(
            new UpsertGarageReportQuickListRequest("Должники", [garages[0].Id]),
            null,
            CancellationToken.None);

        var duplicate = await service.CreateAsync(
            new UpsertGarageReportQuickListRequest(" должники ", [garages[0].Id]),
            null,
            CancellationToken.None);
        var archived = await service.CreateAsync(
            new UpsertGarageReportQuickListRequest("Архив", [garages[1].Id]),
            null,
            CancellationToken.None);
        var missing = await service.CreateAsync(
            new UpsertGarageReportQuickListRequest("Неизвестный", [Guid.NewGuid()]),
            null,
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.Equal("garage_quick_list_name_conflict", duplicate.ErrorCode);
        Assert.Equal("garage_quick_list_garage_invalid", archived.ErrorCode);
        Assert.Equal("garage_quick_list_garage_invalid", missing.ErrorCode);
        Assert.Single(database.Context.GarageReportQuickLists);
    }

    [Fact]
    public async Task UpdateAndDelete_ReturnNotFoundWithoutAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var garage = Assert.Single(await AddGaragesAsync(database.Context, "1"));
        var service = CreateService(database.Context);

        var updated = await service.UpdateAsync(
            Guid.NewGuid(),
            new UpsertGarageReportQuickListRequest("Список", [garage.Id]),
            null,
            CancellationToken.None);
        var deleted = await service.DeleteAsync(
            Guid.NewGuid(),
            new DeleteGarageReportQuickListRequest("Проверка отсутствующего списка"),
            null,
            CancellationToken.None);

        Assert.Equal("garage_quick_list_not_found", updated.ErrorCode);
        Assert.Equal("garage_quick_list_not_found", deleted.ErrorCode);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task Delete_RejectsEmptyReasonAndKeepsList()
    {
        await using var database = await TestDatabase.CreateAsync();
        var garage = Assert.Single(await AddGaragesAsync(database.Context, "1"));
        var service = CreateService(database.Context);
        var created = await service.CreateAsync(
            new UpsertGarageReportQuickListRequest("Должники", [garage.Id]),
            null,
            CancellationToken.None);

        var deleted = await service.DeleteAsync(
            created.Value!.Id,
            new DeleteGarageReportQuickListRequest("  "),
            null,
            CancellationToken.None);

        Assert.Equal("garage_quick_list_delete_reason_required", deleted.ErrorCode);
        Assert.Single(await service.GetAllAsync(CancellationToken.None));
        Assert.Single(database.Context.AuditEvents);
    }

    [Fact]
    public async Task Delete_RejectsReasonLongerThanLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var garage = Assert.Single(await AddGaragesAsync(database.Context, "1"));
        var service = CreateService(database.Context);
        var created = await service.CreateAsync(
            new UpsertGarageReportQuickListRequest("Должники", [garage.Id]),
            null,
            CancellationToken.None);

        var deleted = await service.DeleteAsync(
            created.Value!.Id,
            new DeleteGarageReportQuickListRequest(new string('а', 1001)),
            null,
            CancellationToken.None);

        Assert.Equal("garage_quick_list_delete_reason_too_long", deleted.ErrorCode);
        Assert.Single(await service.GetAllAsync(CancellationToken.None));
        Assert.Single(database.Context.AuditEvents);
    }

    [Fact]
    public async Task Delete_AllowsArchivedNameToBeReused()
    {
        await using var database = await TestDatabase.CreateAsync();
        var garage = Assert.Single(await AddGaragesAsync(database.Context, "1"));
        var service = CreateService(database.Context);
        var first = await service.CreateAsync(
            new UpsertGarageReportQuickListRequest("Должники", [garage.Id]),
            null,
            CancellationToken.None);
        await service.DeleteAsync(
            first.Value!.Id,
            new DeleteGarageReportQuickListRequest("Состав списка устарел"),
            null,
            CancellationToken.None);

        var recreated = await service.CreateAsync(
            new UpsertGarageReportQuickListRequest("Должники", [garage.Id]),
            null,
            CancellationToken.None);

        Assert.True(recreated.Succeeded, recreated.ErrorMessage);
        Assert.NotEqual(first.Value.Id, recreated.Value!.Id);
        Assert.Equal(2, database.Context.GarageReportQuickLists.Count());
        Assert.Single(await service.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAll_PropagatesCancellationBeforeReadingQuickLists()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(database.Context).GetAllAsync(cancellation.Token));
    }

    private static GarageReportQuickListService CreateService(GarageBalanceDbContext context)
    {
        return new GarageReportQuickListService(
            new EfGarageReportQuickListRepository(context),
            new AuditEventWriter(context));
    }

    private static async Task<Garage[]> AddGaragesAsync(GarageBalanceDbContext context, params string[] numbers)
    {
        var garages = numbers.Select(number => new Garage
        {
            Number = number,
            PeopleCount = 1,
            FloorCount = 1
        }).ToArray();
        context.Garages.AddRange(garages);
        await context.SaveChangesAsync();
        return garages;
    }
}
