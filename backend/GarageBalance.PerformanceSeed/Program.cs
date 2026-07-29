using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.PerformanceSeed;
using Microsoft.EntityFrameworkCore;

try
{
    var options = PerformanceSeedOptions.Parse(args);
    var allowTestDatabase = string.Equals(
        Environment.GetEnvironmentVariable("GARAGEBALANCE_ALLOW_PERFORMANCE_TEST_DATABASE"),
        "true",
        StringComparison.OrdinalIgnoreCase);
    var connectionString = PerformanceDatabaseGuard.ValidateConnectionString(
        Environment.GetEnvironmentVariable("GARAGEBALANCE_PERFORMANCE_CONNECTION") ?? string.Empty,
        allowTestDatabase);
    var dbOptions = new DbContextOptionsBuilder<GarageBalanceDbContext>()
        .UseNpgsql(connectionString)
        .Options;
    await using var context = new GarageBalanceDbContext(dbOptions);
    await context.Database.MigrateAsync();
    var result = await new PerformanceDataSeeder(context).SeedAsync(options, CancellationToken.None);

    Console.WriteLine(
        $"performanceSeed=ready; alreadyPresent={result.AlreadyPresent.ToString().ToLowerInvariant()}; " +
        $"garages={result.GarageCount}; accruals={result.AccrualCount}; payments={result.PaymentCount}; " +
        $"meterReadings={result.MeterReadingCount}; elapsedMilliseconds={result.Elapsed.TotalMilliseconds:F0}");
    return 0;
}
catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"performanceSeed=refused; reason={exception.Message}");
    return 2;
}
