using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class DbContextPoolIsolationTests
{
    [Fact]
    public void PooledDbContext_ClearsTrackedEntitiesBeforeTheNextRequestScope()
    {
        var services = new ServiceCollection();
        services.AddDbContextPool<GarageBalanceDbContext>(
            options => options.UseSqlite("Data Source=:memory:"),
            poolSize: 1);
        using var provider = services.BuildServiceProvider();

        GarageBalanceDbContext firstContext;
        using (var firstScope = provider.CreateScope())
        {
            firstContext = firstScope.ServiceProvider.GetRequiredService<GarageBalanceDbContext>();
            firstContext.Owners.Add(new Owner
            {
                LastName = "Тестов",
                FirstName = "Иван"
            });
            Assert.Single(firstContext.ChangeTracker.Entries());
        }

        using var secondScope = provider.CreateScope();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<GarageBalanceDbContext>();

        Assert.Same(firstContext, secondContext);
        Assert.Empty(secondContext.ChangeTracker.Entries());
    }
}
