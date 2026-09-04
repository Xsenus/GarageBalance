using Npgsql;

namespace GarageBalance.ShowcaseSeed;

public static class ShowcaseDatabaseGuard
{
    public const string ExpectedDatabase = "garagebalance_staging";
    public const string RequiredConfirmation = "PREPARE GARAGEBALANCE STAGING";
    public const string RequiredResetConfirmation = "RESET GARAGEBALANCE STAGING";

    public static string Validate(
        string connectionString,
        string confirmation,
        bool allowIntegrationDatabase = false)
        => Validate(connectionString, confirmation, RequiredConfirmation, allowIntegrationDatabase);

    public static string ValidateReset(
        string connectionString,
        string confirmation,
        bool allowIntegrationDatabase = false)
        => Validate(connectionString, confirmation, RequiredResetConfirmation, allowIntegrationDatabase);

    private static string Validate(
        string connectionString,
        string confirmation,
        string requiredConfirmation,
        bool allowIntegrationDatabase)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("GARAGEBALANCE_SHOWCASE_CONNECTION is required.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database?.Trim() ?? string.Empty;
        var allowed = database.Equals(ExpectedDatabase, StringComparison.Ordinal)
            || allowIntegrationDatabase && database.StartsWith("garagebalance_it_", StringComparison.Ordinal);
        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Refusing to prepare database '{database}'. Expected '{ExpectedDatabase}'.");
        }

        if (!string.Equals(confirmation, requiredConfirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The exact staging database confirmation was not supplied.");
        }

        builder.Pooling = false;
        return builder.ConnectionString;
    }
}
