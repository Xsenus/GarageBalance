using Npgsql;

namespace GarageBalance.PerformanceSeed;

public static class PerformanceDatabaseGuard
{
    private const string AllowedPrefix = "garagebalance_performance";
    private const string TestPrefix = "garagebalance_it_";

    public static string ValidateConnectionString(string connectionString, bool allowTestDatabase = false)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set GARAGEBALANCE_PERFORMANCE_CONNECTION to a dedicated performance database.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database?.Trim() ?? string.Empty;
        var isPerformanceDatabase = databaseName.Equals(AllowedPrefix, StringComparison.Ordinal)
            || databaseName.StartsWith($"{AllowedPrefix}_", StringComparison.Ordinal);
        var testOverride = allowTestDatabase
            && databaseName.StartsWith(TestPrefix, StringComparison.Ordinal);

        if (!isPerformanceDatabase && !testOverride)
        {
            throw new InvalidOperationException(
                $"Refusing to seed database '{databaseName}'. Its name must start with '{AllowedPrefix}'.");
        }

        builder.Pooling = false;
        return builder.ConnectionString;
    }
}
