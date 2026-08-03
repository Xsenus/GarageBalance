using GarageBalance.Api.Infrastructure.Diagnostics;
using GarageBalance.Api.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageBalance.Api.Tests.Diagnostics;

public sealed class DatabaseReadinessServiceTests
{
    [Fact]
    public async Task IsReadyAsync_ReturnsTrueForAvailableDatabase()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = new DatabaseReadinessService(database.Context, NullLogger<DatabaseReadinessService>.Instance);

        Assert.True(await service.IsReadyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task IsReadyAsync_ReturnsFalseWhenDatabaseContextIsUnavailable()
    {
        var database = await SqliteTestDatabase.CreateAsync();
        var service = new DatabaseReadinessService(database.Context, NullLogger<DatabaseReadinessService>.Instance);
        await database.DisposeAsync();

        Assert.False(await service.IsReadyAsync(CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task IsReadyAsync_ChecksRealPostgreSqlConnection()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var service = new DatabaseReadinessService(context, NullLogger<DatabaseReadinessService>.Instance);

        Assert.True(await service.IsReadyAsync(CancellationToken.None));
    }
}
