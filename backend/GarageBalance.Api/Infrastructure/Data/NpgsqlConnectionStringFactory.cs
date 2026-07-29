using Npgsql;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed record DatabaseConnectionPoolSettings(
    int MaximumPoolSize,
    int MinimumPoolSize,
    int ConnectionIdleLifetimeSeconds,
    int ConnectionPruningIntervalSeconds,
    int KeepAliveSeconds);

public static class NpgsqlConnectionStringFactory
{
    public static string Create(string? connectionString, DatabaseConnectionPoolSettings settings)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
        }

        var maximumPoolSize = Math.Clamp(settings.MaximumPoolSize, 8, 128);
        var minimumPoolSize = Math.Clamp(settings.MinimumPoolSize, 0, maximumPoolSize);
        var idleLifetimeSeconds = Math.Clamp(settings.ConnectionIdleLifetimeSeconds, 30, 1800);
        var pruningIntervalSeconds = Math.Clamp(
            settings.ConnectionPruningIntervalSeconds,
            1,
            Math.Min(idleLifetimeSeconds, 60));
        var keepAliveSeconds = Math.Clamp(settings.KeepAliveSeconds, 0, 300);

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
            MaxPoolSize = maximumPoolSize,
            MinPoolSize = minimumPoolSize,
            ConnectionIdleLifetime = idleLifetimeSeconds,
            ConnectionPruningInterval = pruningIntervalSeconds,
            KeepAlive = keepAliveSeconds
        };

        return builder.ConnectionString;
    }
}
