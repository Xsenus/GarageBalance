using GarageBalance.Api.Infrastructure.Data;
using Npgsql;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class NpgsqlConnectionStringFactoryTests
{
    [Fact]
    public void Create_PreservesEndpointAndAppliesBoundedPoolSettings()
    {
        var connectionString = NpgsqlConnectionStringFactory.Create(
            "Host=127.0.0.1;Port=5433;Database=garagebalance;Username=app;Password=secret",
            new DatabaseConnectionPoolSettings(32, 2, 300, 10, 0, 3));

        var result = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal("127.0.0.1", result.Host);
        Assert.Equal(5433, result.Port);
        Assert.Equal("garagebalance", result.Database);
        Assert.Equal("app", result.Username);
        Assert.Equal("secret", result.Password);
        Assert.True(result.Pooling);
        Assert.Equal(32, result.MaxPoolSize);
        Assert.Equal(2, result.MinPoolSize);
        Assert.Equal(300, result.ConnectionIdleLifetime);
        Assert.Equal(10, result.ConnectionPruningInterval);
        Assert.Equal(0, result.KeepAlive);
        Assert.Equal(3, result.Timeout);
    }

    [Fact]
    public void Create_ClampsUnsafePoolAndKeepAliveValues()
    {
        var connectionString = NpgsqlConnectionStringFactory.Create(
            "Host=localhost;Database=garagebalance;Username=app;Password=secret",
            new DatabaseConnectionPoolSettings(500, 300, 5, 500, 900, 500));

        var result = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal(128, result.MaxPoolSize);
        Assert.Equal(128, result.MinPoolSize);
        Assert.Equal(30, result.ConnectionIdleLifetime);
        Assert.Equal(30, result.ConnectionPruningInterval);
        Assert.Equal(300, result.KeepAlive);
        Assert.Equal(30, result.Timeout);
    }

    [Fact]
    public void Create_AllowsSmallestPoolAndDisabledKeepAlive()
    {
        var connectionString = NpgsqlConnectionStringFactory.Create(
            "Host=localhost;Database=garagebalance;Username=app;Password=secret",
            new DatabaseConnectionPoolSettings(1, -10, 5000, 0, -1, 0));

        var result = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal(8, result.MaxPoolSize);
        Assert.Equal(0, result.MinPoolSize);
        Assert.Equal(1800, result.ConnectionIdleLifetime);
        Assert.Equal(1, result.ConnectionPruningInterval);
        Assert.Equal(0, result.KeepAlive);
        Assert.Equal(1, result.Timeout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsMissingConnectionString(string? connectionString)
    {
        var action = () => NpgsqlConnectionStringFactory.Create(
            connectionString,
            new DatabaseConnectionPoolSettings(32, 2, 300, 10, 0, 3));

        var error = Assert.Throws<InvalidOperationException>(action);
        Assert.Contains("DefaultConnection", error.Message, StringComparison.Ordinal);
    }
}
