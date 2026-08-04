using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Auth;

public sealed class UserSecurityMutationLockTests
{
    [Fact]
    public async Task SqliteFallback_SerializesCallersAndReleasesOnlyOnce()
    {
        await using var database = await CreateDatabaseAsync();
        var mutationLock = new UserSecurityMutationLock(database.Context);
        await using var firstLease = await mutationLock.AcquireAsync(CancellationToken.None);

        var secondAcquire = mutationLock.AcquireAsync(CancellationToken.None);
        Assert.False(await CompletesWithinAsync(secondAcquire, TimeSpan.FromMilliseconds(100)));

        await firstLease.DisposeAsync();
        await firstLease.DisposeAsync();
        await using var secondLease = await secondAcquire;
    }

    [Fact]
    public async Task SqliteFallback_HonorsCancellationWhileWaiting()
    {
        await using var database = await CreateDatabaseAsync();
        var mutationLock = new UserSecurityMutationLock(database.Context);
        await using var firstLease = await mutationLock.AcquireAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mutationLock.AcquireAsync(cancellation.Token));
    }

    private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        return ReferenceEquals(completed, task);
    }

    private static async Task<TestDatabase> CreateDatabaseAsync()
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

    private sealed class TestDatabase(SqliteConnection connection, GarageBalanceDbContext context) : IAsyncDisposable
    {
        public GarageBalanceDbContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
