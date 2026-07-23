using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlGarageSearchRankingIntegrationTests
{
    [PostgreSqlFact]
    public async Task SearchRanking_IsAppliedInPostgreSqlBeforeLimitAndPagination()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateContext())
        {
            setupContext.Garages.AddRange(new[] { "107", "117", "7", "710", "70", "71" }
                .Select(number => new Garage { Number = number }));
            await setupContext.SaveChangesAsync();
        }

        await using var queryContext = database.CreateContext();
        var repository = new EfGarageRepository(queryContext);

        var limitedList = await repository.GetListAsync("7", false, 3, CancellationToken.None);
        var firstPage = await repository.GetPageAsync(
            "7",
            new GarageColumnFilters(null, null, null, null, null),
            false,
            false,
            0,
            4,
            "number",
            false,
            CancellationToken.None);
        var secondPage = await repository.GetPageAsync(
            "7",
            new GarageColumnFilters(null, null, null, null, null),
            false,
            false,
            4,
            4,
            "number",
            false,
            CancellationToken.None);

        Assert.Equal(["7", "70", "71"], limitedList.Select(garage => garage.Number));
        Assert.Equal(["7", "70", "71", "710"], firstPage.Items.Select(garage => garage.Number));
        Assert.Equal(["107", "117"], secondPage.Items.Select(garage => garage.Number));
        Assert.Equal(6, firstPage.TotalCount);
        Assert.Equal(6, secondPage.TotalCount);
    }
}
