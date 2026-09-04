using Npgsql;

namespace GarageBalance.Api.Tests.Common;

public sealed class PostgreSqlTestDatabaseTests
{
    [PostgreSqlFact]
    public async Task FullyMigratedDatabases_AreClonedQuicklyAndRemainIsolated()
    {
        await using var first = await PostgreSqlTestDatabase.CreateAsync();
        await using var second = await PostgreSqlTestDatabase.CreateAsync();

        await using (var firstConnection = new NpgsqlConnection(first.ConnectionString))
        {
            await firstConnection.OpenAsync();
            await using var createMarker = firstConnection.CreateCommand();
            createMarker.CommandText = "CREATE TABLE test_clone_marker (id integer PRIMARY KEY)";
            await createMarker.ExecuteNonQueryAsync();
        }

        await using var secondConnection = new NpgsqlConnection(second.ConnectionString);
        await secondConnection.OpenAsync();
        await using var findMarker = secondConnection.CreateCommand();
        findMarker.CommandText = "SELECT to_regclass('public.test_clone_marker') IS NULL";

        Assert.True((bool)(await findMarker.ExecuteScalarAsync())!);
    }
}
