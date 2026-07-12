using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Common;

public sealed class SqliteTestDatabase : IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection connection;
    private int disposed;

    private SqliteTestDatabase(SqliteConnection connection, GarageBalanceDbContext context)
    {
        this.connection = connection;
        Context = context;
    }

    public GarageBalanceDbContext Context { get; }

    public static SqliteTestDatabase Create()
    {
        return CreateAsync().GetAwaiter().GetResult();
    }

    public static async Task<SqliteTestDatabase> CreateAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(cancellationToken);

        try
        {
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new GarageBalanceDbContext(options);
            await context.Database.EnsureCreatedAsync(cancellationToken);
            return new SqliteTestDatabase(connection, context);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Context.Dispose();
        connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await Context.DisposeAsync();
        await connection.DisposeAsync();
    }
}
