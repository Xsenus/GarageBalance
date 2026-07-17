namespace GarageBalance.Api.Tests.Common;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class PostgreSqlFactAttribute : FactAttribute
{
    public const string ConnectionStringEnvironmentVariable = "GARAGEBALANCE_POSTGRES_TEST_CONNECTION";

    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)))
        {
            Skip = $"Set {ConnectionStringEnvironmentVariable} to run PostgreSQL integration tests.";
        }
    }
}
