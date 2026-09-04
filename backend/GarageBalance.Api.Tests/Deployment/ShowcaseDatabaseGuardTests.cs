using GarageBalance.ShowcaseSeed;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class ShowcaseDatabaseGuardTests
{
    [Fact]
    public void Validate_AllowsOnlyExactStagingDatabaseWithExactConfirmation()
    {
        var validated = ShowcaseDatabaseGuard.Validate(
            "Host=localhost;Database=garagebalance_staging;Username=demo;Password=secret",
            ShowcaseDatabaseGuard.RequiredConfirmation);

        Assert.Contains("Database=garagebalance_staging", validated, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pooling=False", validated, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("garagebalance")]
    [InlineData("garagebalance_local")]
    [InlineData("garagebalance_staging_copy")]
    [InlineData("postgres")]
    public void Validate_RefusesEveryOtherDatabase(string database)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ShowcaseDatabaseGuard.Validate(
            $"Host=localhost;Database={database};Username=demo;Password=secret",
            ShowcaseDatabaseGuard.RequiredConfirmation));

        Assert.Contains("Refusing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RefusesMissingOrInexactConfirmation()
    {
        Assert.Throws<InvalidOperationException>(() => ShowcaseDatabaseGuard.Validate(
            "Host=localhost;Database=garagebalance_staging;Username=demo;Password=secret",
            "prepare"));
    }

    [Fact]
    public void ValidateReset_AllowsOnlyExactStagingDatabaseAndResetConfirmation()
    {
        var validated = ShowcaseDatabaseGuard.ValidateReset(
            "Host=localhost;Database=garagebalance_staging;Username=demo;Password=secret",
            ShowcaseDatabaseGuard.RequiredResetConfirmation);

        Assert.Contains("Database=garagebalance_staging", validated, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => ShowcaseDatabaseGuard.ValidateReset(
            "Host=localhost;Database=garagebalance;Username=demo;Password=secret",
            ShowcaseDatabaseGuard.RequiredResetConfirmation));
        Assert.Throws<InvalidOperationException>(() => ShowcaseDatabaseGuard.ValidateReset(
            "Host=localhost;Database=garagebalance_staging;Username=demo;Password=secret",
            ShowcaseDatabaseGuard.RequiredConfirmation));
    }
}
