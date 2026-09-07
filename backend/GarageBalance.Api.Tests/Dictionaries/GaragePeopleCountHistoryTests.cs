using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class GaragePeopleCountHistoryTests
{
    [Fact]
    public async Task CardKeepsDailyChangesReplacesSameDayAndRejectsEarlierOrStaleEdits()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var month = new DateOnly(2026, 6, 1);
        var request = new UpsertGarageRequest("PEOPLE-HISTORY", 1, 1, null, 0, null, null, null);
        var created = await DictionaryServiceTestFactory.Create(context, month.AddDays(4)).CreateGarageAsync(request, null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var id = created.Value!.Id;
        var initial = await context.GaragePeopleCountPeriods.SingleAsync();
        Assert.Equal(month, initial.EffectiveFrom);
        Assert.Equal(1, initial.PeopleCount);

        foreach (var (day, count) in new[] { (13, 2), (13, 3), (20, 2) })
        {
            var saved = await DictionaryServiceTestFactory.Create(context, month.AddDays(day - 1))
                .UpdateGarageAsync(id, request with { PeopleCount = count }, null, CancellationToken.None);
            Assert.True(saved.Succeeded, saved.ErrorMessage);
        }
        var periods = await context.GaragePeopleCountPeriods.OrderBy(period => period.EffectiveFrom).ToArrayAsync();
        Assert.Equal([1, 13, 20], periods.Select(period => period.EffectiveFrom.Day));
        Assert.Equal([1, 3, 2], periods.Select(period => period.PeopleCount));
        var auditCount = await context.AuditEvents.CountAsync();
        var unchanged = await DictionaryServiceTestFactory.Create(context, month.AddDays(20))
            .UpdateGarageAsync(id, request with { PeopleCount = 2 }, null, CancellationToken.None);
        Assert.True(unchanged.Succeeded, unchanged.ErrorMessage);
        Assert.Equal(auditCount, await context.AuditEvents.CountAsync());
        var earlier = await DictionaryServiceTestFactory.Create(context, month.AddDays(11))
            .UpdateGarageAsync(id, request with { PeopleCount = 4 }, null, CancellationToken.None);
        Assert.False(earlier.Succeeded);
        Assert.Equal("garage_people_count_date_before_history", earlier.ErrorCode);
        Assert.False(context.ChangeTracker.HasChanges());
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => DictionaryServiceTestFactory.Create(context, month.AddDays(21))
            .UpdateGarageAsync(id, request with { PeopleCount = 4, Version = created.Value.Version }, null, CancellationToken.None));
        Assert.Equal(3, await context.GaragePeopleCountPeriods.CountAsync());
        Assert.Equal(2, (await context.Garages.SingleAsync()).PeopleCount);
    }

    [Fact]
    public async Task FirstEditOfGarageWithoutHistoryKeepsItsPreviousCountAsBaseline()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = new Garage { Number = "LEGACY-PEOPLE", PeopleCount = 4 };
        context.Add(garage);
        await context.SaveChangesAsync();
        var date = new DateOnly(2026, 6, 13);
        var result = await DictionaryServiceTestFactory.Create(context, date).UpdateGarageAsync(garage.Id,
            new UpsertGarageRequest(garage.Number, 2, 0, null, 0, null, null, null), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var history = await new EfGarageRepository(context).GetPeopleCountPeriodsAsync([garage.Id], date.AddDays(-1), date, CancellationToken.None);
        Assert.Equal([DateOnly.MinValue, date], history.Select(period => period.EffectiveFrom));
        Assert.Equal([4, 2], history.Select(period => period.PeopleCount));
    }

    [Fact]
    public async Task CancellationDoesNotCreateGarageOrPeopleHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DictionaryServiceTestFactory.Create(database.Context)
            .CreateGarageAsync(new UpsertGarageRequest("CANCELED-PEOPLE", 1, 1, null, 0, null, null, null), null, cancellation.Token));
        Assert.False(await database.Context.Garages.AnyAsync());
        Assert.False(await database.Context.GaragePeopleCountPeriods.AnyAsync());
        Assert.False(await database.Context.AuditEvents.AnyAsync());
    }
}
