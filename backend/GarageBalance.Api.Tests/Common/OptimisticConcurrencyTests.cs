using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Common;

public sealed class OptimisticConcurrencyTests
{
    [Fact]
    public void Guard_AllowsCurrentAndInternalVersionButRejectsEmptyAndStaleTokens()
    {
        var garage = new Garage { Number = "1" };

        OptimisticConcurrencyGuard.EnsureCurrent(null, garage);
        OptimisticConcurrencyGuard.EnsureCurrent(garage.Version, garage);

        Assert.Throws<OptimisticConcurrencyException>(() =>
            OptimisticConcurrencyGuard.EnsureCurrent(Guid.Empty, garage));
        Assert.Throws<OptimisticConcurrencyException>(() =>
            OptimisticConcurrencyGuard.EnsureCurrent(Guid.NewGuid(), garage));
    }

    [Fact]
    public void EditableAggregates_AreConcurrencyTokensWithDatabaseGeneratedDefaults()
    {
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options;
        using var context = new GarageBalanceDbContext(options);
        var aggregateTypes = new[]
        {
            typeof(Garage),
            typeof(Supplier),
            typeof(Tariff),
            typeof(ChargeServiceSetting),
            typeof(Fund),
            typeof(AppUser),
            typeof(ApplicationSetting)
        };

        foreach (var aggregateType in aggregateTypes)
        {
            var version = context.Model.FindEntityType(aggregateType)?.FindProperty("Version");
            Assert.NotNull(version);
            Assert.True(version!.IsConcurrencyToken);
            Assert.Equal("gen_random_uuid()", version.GetDefaultValueSql());
        }
    }

    [PostgreSqlFact]
    public async Task StaleGarageUpdate_IsRejectedAndKeepsFirstCommittedValue()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var garage = new Garage { Number = "CONCURRENCY-1", PeopleCount = 1, FloorCount = 1 };
        await using (var setup = database.CreateContext())
        {
            setup.Garages.Add(garage);
            await setup.SaveChangesAsync();
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstCopy = await first.Garages.SingleAsync(item => item.Id == garage.Id);
        var staleCopy = await second.Garages.SingleAsync(item => item.Id == garage.Id);

        firstCopy.PeopleCount = 2;
        await first.SaveChangesAsync();
        staleCopy.PeopleCount = 3;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verification = database.CreateContext();
        Assert.Equal(2, await verification.Garages.Where(item => item.Id == garage.Id).Select(item => item.PeopleCount).SingleAsync());
    }
}
