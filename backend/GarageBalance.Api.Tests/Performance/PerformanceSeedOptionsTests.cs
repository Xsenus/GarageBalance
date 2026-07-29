using GarageBalance.PerformanceSeed;
using Npgsql;

namespace GarageBalance.Api.Tests.Performance;

public sealed class PerformanceSeedOptionsTests
{
    [Fact]
    public void Parse_UsesRealisticDefaultsAndAcceptsExplicitBounds()
    {
        Assert.Equal(
            new PerformanceSeedOptions(500, 60),
            PerformanceSeedOptions.Parse([]));
        Assert.Equal(
            new PerformanceSeedOptions(10, 120),
            PerformanceSeedOptions.Parse(["--garages", "10", "--months", "120"]));
    }

    [Theory]
    [InlineData("--garages", "9")]
    [InlineData("--garages", "5001")]
    [InlineData("--months", "11")]
    [InlineData("--months", "121")]
    [InlineData("--garages", "not-a-number")]
    [InlineData("--unknown", "12")]
    public void Parse_RejectsUnsafeOrUnknownArguments(string argument, string value)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            PerformanceSeedOptions.Parse([argument, value]));
    }

    [Fact]
    public void DatabaseGuard_AllowsOnlyDedicatedPerformanceDatabaseByDefault()
    {
        var safe = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = "garagebalance_performance_local",
            Username = "test",
            Password = "not-a-real-secret"
        }.ConnectionString;
        var production = new NpgsqlConnectionStringBuilder(safe)
        {
            Database = "garagebalance_staging"
        }.ConnectionString;
        var integration = new NpgsqlConnectionStringBuilder(safe)
        {
            Database = "garagebalance_it_0123456789abcdef"
        }.ConnectionString;

        var normalized = new NpgsqlConnectionStringBuilder(
            PerformanceDatabaseGuard.ValidateConnectionString(safe));

        Assert.False(normalized.Pooling);
        Assert.Throws<InvalidOperationException>(() =>
            PerformanceDatabaseGuard.ValidateConnectionString(production));
        Assert.Throws<InvalidOperationException>(() =>
            PerformanceDatabaseGuard.ValidateConnectionString(integration));
        Assert.Equal(
            "garagebalance_it_0123456789abcdef",
            new NpgsqlConnectionStringBuilder(
                PerformanceDatabaseGuard.ValidateConnectionString(integration, allowTestDatabase: true)).Database);
    }

    [Fact]
    public void DatabaseGuard_RejectsMissingConnectionString()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PerformanceDatabaseGuard.ValidateConnectionString(string.Empty));
    }
}
