using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Common;

internal sealed class PostgreSqlTestDatabase : IAsyncDisposable
{
    private static readonly SemaphoreSlim TemplateGate = new(1, 1);
    private static string? templateAdminConnectionString;
    private static string? templateDatabaseName;
    private static int activeTemplateClones;

    private readonly string adminConnectionString;
    private readonly string databaseName;
    private readonly bool isTemplateClone;

    private PostgreSqlTestDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString,
        bool isTemplateClone)
    {
        this.adminConnectionString = adminConnectionString;
        this.databaseName = databaseName;
        this.isTemplateClone = isTemplateClone;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static Task<PostgreSqlTestDatabase> CreateAsync(CancellationToken cancellationToken = default) =>
        CreateFromMigratedTemplateAsync(cancellationToken);

    public static async Task<PostgreSqlTestDatabase> CreateAsync(
        string? targetMigration,
        CancellationToken cancellationToken = default)
    {
        if (targetMigration is null)
        {
            return await CreateFromMigratedTemplateAsync(cancellationToken);
        }

        var baseConnectionString = GetBaseConnectionString();
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        var databaseName = $"garagebalance_it_{Guid.NewGuid():N}";
        await CreateDatabaseAsync(adminBuilder.ConnectionString, databaseName, templateName: null, cancellationToken);

        var testBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };
        var database = new PostgreSqlTestDatabase(
            adminBuilder.ConnectionString,
            databaseName,
            testBuilder.ConnectionString,
            isTemplateClone: false);

        try
        {
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync(targetMigration, cancellationToken);
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
        try
        {
            await DropDatabaseAsync(adminConnectionString, databaseName, CancellationToken.None);
        }
        finally
        {
            if (isTemplateClone)
            {
                await ReleaseTemplateCloneAsync();
            }
        }
    }

    private static async Task<PostgreSqlTestDatabase> CreateFromMigratedTemplateAsync(
        CancellationToken cancellationToken)
    {
        var baseConnectionString = GetBaseConnectionString();
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };

        await TemplateGate.WaitAsync(cancellationToken);
        try
        {
            var templateName = await EnsureMigratedTemplateAsync(
                adminBuilder.ConnectionString,
                baseConnectionString,
                cancellationToken);
            var databaseName = $"garagebalance_it_{Guid.NewGuid():N}";
            await CreateDatabaseAsync(
                adminBuilder.ConnectionString,
                databaseName,
                templateName,
                cancellationToken);

            var testBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName,
                Pooling = false
            };
            activeTemplateClones++;
            return new PostgreSqlTestDatabase(
                adminBuilder.ConnectionString,
                databaseName,
                testBuilder.ConnectionString,
                isTemplateClone: true);
        }
        finally
        {
            TemplateGate.Release();
        }
    }

    private static async Task<string> EnsureMigratedTemplateAsync(
        string adminConnectionString,
        string baseConnectionString,
        CancellationToken cancellationToken)
    {
        if (templateDatabaseName is not null)
        {
            if (!string.Equals(
                    templateAdminConnectionString,
                    adminConnectionString,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "PostgreSQL test connection cannot change after the migrated template is created.");
            }

            return templateDatabaseName;
        }

        var templateSuffix = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..20];
        var candidateName = $"garagebalance_it_template_{Environment.ProcessId}_{templateSuffix}";
        await CreateDatabaseAsync(
            adminConnectionString,
            candidateName,
            templateName: null,
            cancellationToken);

        try
        {
            var templateBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = candidateName,
                Pooling = false
            };
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseNpgsql(templateBuilder.ConnectionString)
                .Options;
            await using (var context = new GarageBalanceDbContext(options))
            {
                await context.Database.MigrateAsync(cancellationToken);
            }

            templateAdminConnectionString = adminConnectionString;
            templateDatabaseName = candidateName;
            return candidateName;
        }
        catch
        {
            await DropDatabaseAsync(adminConnectionString, candidateName, CancellationToken.None);
            throw;
        }
    }

    private static async Task CreateDatabaseAsync(
        string adminConnectionString,
        string databaseName,
        string? templateName,
        CancellationToken cancellationToken)
    {
        await using var adminConnection = new NpgsqlConnection(adminConnectionString);
        await adminConnection.OpenAsync(cancellationToken);
        await using var createCommand = adminConnection.CreateCommand();
        createCommand.CommandText = templateName is null
            ? $"CREATE DATABASE \"{databaseName}\""
            : $"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{templateName}\"";
        await createCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DropDatabaseAsync(
        string adminConnectionString,
        string databaseName,
        CancellationToken cancellationToken)
    {
        await using var adminConnection = new NpgsqlConnection(adminConnectionString);
        await adminConnection.OpenAsync(cancellationToken);
        await using var dropCommand = adminConnection.CreateCommand();
        dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
        await dropCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GetBaseConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlFactAttribute.ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Environment variable {PostgreSqlFactAttribute.ConnectionStringEnvironmentVariable} is not configured.");
        }

        return connectionString;
    }

    private static async Task ReleaseTemplateCloneAsync()
    {
        await TemplateGate.WaitAsync();
        try
        {
            activeTemplateClones--;
            if (activeTemplateClones < 0)
            {
                throw new InvalidOperationException("PostgreSQL template clone counter became negative.");
            }

            if (activeTemplateClones > 0 ||
                templateAdminConnectionString is null ||
                templateDatabaseName is null)
            {
                return;
            }

            await DropDatabaseAsync(
                templateAdminConnectionString,
                templateDatabaseName,
                CancellationToken.None);
            templateAdminConnectionString = null;
            templateDatabaseName = null;
        }
        finally
        {
            TemplateGate.Release();
        }
    }
}
