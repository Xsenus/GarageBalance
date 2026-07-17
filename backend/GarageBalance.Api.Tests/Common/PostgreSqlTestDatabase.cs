using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Common;

internal sealed class PostgreSqlTestDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;
    private readonly string databaseName;

    private PostgreSqlTestDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString)
    {
        this.adminConnectionString = adminConnectionString;
        this.databaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<PostgreSqlTestDatabase> CreateAsync(CancellationToken cancellationToken = default)
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            PostgreSqlFactAttribute.ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                $"Environment variable {PostgreSqlFactAttribute.ConnectionStringEnvironmentVariable} is not configured.");
        }

        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        var databaseName = $"garagebalance_it_{Guid.NewGuid():N}";
        await using (var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await adminConnection.OpenAsync(cancellationToken);
            await using var createCommand = adminConnection.CreateCommand();
            createCommand.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await createCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var testBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };
        var database = new PostgreSqlTestDatabase(
            adminBuilder.ConnectionString,
            databaseName,
            testBuilder.ConnectionString);

        try
        {
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync(cancellationToken);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public GarageBalanceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new GarageBalanceDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var adminConnection = new NpgsqlConnection(adminConnectionString);
        await adminConnection.OpenAsync();
        await using var dropCommand = adminConnection.CreateCommand();
        dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
        await dropCommand.ExecuteNonQueryAsync();
    }
}
