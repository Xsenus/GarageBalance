using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class AccountingSavepointTests
{
    [Fact]
    public async Task Savepoint_RollbackPreservesEarlierOuterWritesAndLeavesOuterTransactionUsable()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertRollbackAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_SavepointRollbackPreservesOuterTransaction()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertRollbackAsync(context);
    }

    private static async Task AssertRollbackAsync(GarageBalanceDbContext context)
    {
        await using var outer = await context.Database.BeginTransactionAsync();
        context.Garages.Add(new Garage { Number = "outer-before" });
        await context.SaveChangesAsync();
        await using (var inner = await new EfMeterReadingRepository(context).BeginConnectionWriteAsync(CancellationToken.None, true))
        {
            context.Garages.Add(new Garage { Number = "inner-rollback" });
            await context.SaveChangesAsync();
            await inner.RollbackAsync();
        }
        Assert.Same(outer, context.Database.CurrentTransaction);
        Assert.Equal("outer-before", (await context.Garages.SingleAsync()).Number);
        context.Garages.Add(new Garage { Number = "outer-after" });
        await context.SaveChangesAsync();
        await outer.CommitAsync();
        Assert.Equal(2, await context.Garages.CountAsync());
    }

    [Fact]
    public async Task ReleasedSavepoint_DoesNotCommitOuterTransaction()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        await using (var outer = await context.Database.BeginTransactionAsync())
        {
            await using (var inner = await new EfMeterReadingRepository(context).BeginConnectionWriteAsync(CancellationToken.None, true))
            {
                context.Garages.Add(new Garage { Number = "outer-rollback" });
                await context.SaveChangesAsync();
                await inner.CommitAsync(CancellationToken.None);
            }
            Assert.Same(outer, context.Database.CurrentTransaction);
            await outer.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        Assert.Empty(await context.Garages.ToListAsync());
    }

    [Fact]
    public async Task OrdinaryConnectionWrite_DoesNotSilentlyJoinAnOuterTransaction()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var outer = await database.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfMeterReadingRepository(database.Context).BeginConnectionWriteAsync(CancellationToken.None));
        Assert.Same(outer, database.Context.Database.CurrentTransaction);
    }
}
