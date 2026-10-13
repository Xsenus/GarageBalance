using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class GarageFreshLookupTests
{
    [Fact]
    public async Task FreshLookup_ReturnsOnlyActiveGarageAndReusesTrackedOwner()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var owner = new Owner { LastName = "Synthetic", FirstName = "Owner" };
        var garage = new Garage { Number = "fresh", Owner = owner, AccountingStartMonth = new(2026, 9, 1) };
        context.AddRange(garage, new Garage { Number = "archived", IsArchived = true });
        await context.SaveChangesAsync();
        var repository = new EfGarageRepository(context);
        var same = await repository.FindActiveFreshWithOwnerAsync(garage.Id, CancellationToken.None);
        Assert.Same(garage, same);
        Assert.Same(owner, same!.Owner);
        Assert.Equal(EntityState.Unchanged, context.Entry(garage).State);
        context.ChangeTracker.Clear();
        var loaded = await repository.FindActiveFreshWithOwnerAsync(garage.Id, CancellationToken.None);
        Assert.NotNull(loaded!.Owner);
        Assert.Equal(new DateOnly(2026, 9, 1), loaded.AccountingStartMonth);
        Assert.Equal(EntityState.Unchanged, context.Entry(loaded).State);
        var archivedId = await context.Garages.Where(row => row.IsArchived).Select(row => row.Id).SingleAsync();
        Assert.Null(await repository.FindActiveFreshWithOwnerAsync(archivedId, CancellationToken.None));
        Assert.Null(await repository.FindActiveFreshWithOwnerAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task FreshLookup_RejectsPendingChangesInsteadOfDiscardingThem()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = new Garage { Number = "unchanged" };
        database.Context.Garages.Add(garage);
        await database.Context.SaveChangesAsync();
        garage.Number = "pending";
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfGarageRepository(database.Context)
            .FindActiveFreshWithOwnerAsync(garage.Id, CancellationToken.None));
        Assert.Equal("pending", garage.Number);
        Assert.Equal("unchanged", await database.Context.Garages.AsNoTracking().Select(row => row.Number).SingleAsync());
    }

    [PostgreSqlFact]
    public async Task PostgreSql_FreshLookupUpdatesStaleTrackedMonthWithoutASecondSelect()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var first = database.CreateContext();
        var garage = new Garage { Number = "fresh-pg" };
        first.Garages.Add(garage);
        await first.SaveChangesAsync();
        await using var second = database.CreateContext();
        var stale = await second.Garages.SingleAsync(row => row.Id == garage.Id);
        garage.AccountingStartMonth = new(2026, 9, 1);
        await first.SaveChangesAsync();
        var refreshed = await new EfGarageRepository(second).FindActiveFreshWithOwnerAsync(garage.Id, CancellationToken.None);
        Assert.Same(stale, refreshed);
        Assert.Equal(garage.AccountingStartMonth, refreshed!.AccountingStartMonth);
        Assert.Equal(garage.Version, refreshed.Version);
        Assert.Equal(EntityState.Unchanged, second.Entry(refreshed).State);
        await second.SaveChangesAsync();
        Assert.Equal(garage.AccountingStartMonth, await first.Garages.AsNoTracking().Select(row => row.AccountingStartMonth).SingleAsync());
    }
}
