using GarageBalance.Api.Tests.Common;
using GarageBalance.PerformanceSeed;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Performance;

public sealed class PostgreSqlPerformanceDataSeederIntegrationTests
{
    [PostgreSqlFact]
    public async Task Seed_CreatesDeterministicRealisticDatasetAndIsIdempotent()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var seeder = new PerformanceDataSeeder(context);
        var options = new PerformanceSeedOptions(12, 12);

        var created = await seeder.SeedAsync(options, CancellationToken.None);
        var repeated = await seeder.SeedAsync(options, CancellationToken.None);

        Assert.False(created.AlreadyPresent);
        Assert.True(repeated.AlreadyPresent);
        Assert.Equal(12, created.GarageCount);
        Assert.Equal(144, created.AccrualCount);
        Assert.Equal(144, created.PaymentCount);
        Assert.Equal(144, created.MeterReadingCount);
        Assert.Equal(created.GarageCount, repeated.GarageCount);
        Assert.Equal(created.AccrualCount, repeated.AccrualCount);
        Assert.Equal(created.PaymentCount, repeated.PaymentCount);
        Assert.Equal(created.MeterReadingCount, repeated.MeterReadingCount);
        Assert.Equal(12, await context.Garages.CountAsync(item => item.Number.StartsWith("PERF-")));
        Assert.Equal(12, await context.Owners.CountAsync(item => item.LastName.StartsWith("Тестовый")));
        Assert.Equal(
            1,
            await context.IncomeTypes.CountAsync(item => item.Code == PerformanceDataSeeder.MarkerCode));
        Assert.DoesNotContain(
            await context.Owners.Select(item => item.LastName).ToListAsync(),
            value => value.Contains("Иванов", StringComparison.OrdinalIgnoreCase));
    }
}
