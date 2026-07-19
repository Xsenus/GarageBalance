using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlGarageListProjectionIntegrationTests
{
    [PostgreSqlFact]
    public async Task GaragePage_AppliesGreenColumnFiltersBeforeCountAndPagination()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateContext())
        {
            setupContext.Garages.AddRange(
                new Garage { Number = "А-10", PeopleCount = 2, FloorCount = 1 },
                new Garage { Number = "А-20", PeopleCount = 3, FloorCount = 2, IsArchived = true },
                new Garage { Number = "Б-30", PeopleCount = 3, FloorCount = 2 },
                new Garage { Number = "А-40", PeopleCount = 5, FloorCount = 3 });
            await setupContext.SaveChangesAsync();
        }

        await using var queryContext = database.CreateContext();
        var service = DictionaryServiceTestFactory.Create(queryContext);
        var page = await service.GetGaragesPageAsync(
            null, 1, 1, "number", "asc", CancellationToken.None,
            includeArchived: true,
            number: "а-",
            peopleCountMin: 2,
            peopleCountMax: 3,
            floorCountMin: 1,
            floorCountMax: 2);

        Assert.Equal(2, page.TotalCount);
        var garage = Assert.Single(page.Items);
        Assert.Equal("А-20", garage.Number);
        Assert.True(garage.IsArchived);
    }

    [PostgreSqlFact]
    public async Task GarageListAndPage_ProjectCurrentOwnerPhoneWithoutOtherPersonalFields()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var owner = new Owner
        {
            LastName = "Иванов",
            FirstName = "Иван",
            MiddleName = "Иванович",
            Phone = "+7 900 111-22-33",
            Address = "Не должен загружаться",
            MeterNotes = "Не должны загружаться"
        };
        var garage = new Garage
        {
            Number = "PHONE-PROJECTION-1",
            PeopleCount = 1,
            FloorCount = 1,
            Owner = owner
        };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(owner, garage);
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var service = DictionaryServiceTestFactory.Create(queryContext);

        var list = await service.GetGaragesAsync("PHONE-PROJECTION", CancellationToken.None);
        var page = await service.GetGaragesPageAsync(
            "PHONE-PROJECTION",
            0,
            10,
            "phone",
            "asc",
            CancellationToken.None);

        Assert.Equal("+7 900 111-22-33", Assert.Single(list).OwnerPhone);
        Assert.Equal("+7 900 111-22-33", Assert.Single(page.Items).OwnerPhone);
        var garageQueries = capture.Commands
            .Where(command =>
                command.Contains("FROM garages AS", StringComparison.OrdinalIgnoreCase) &&
                command.Contains("Phone", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, garageQueries.Length);
        Assert.All(garageQueries, command =>
        {
            Assert.Contains("Phone", command, StringComparison.Ordinal);
            Assert.Contains("LastName", command, StringComparison.Ordinal);
            Assert.Contains("FirstName", command, StringComparison.Ordinal);
            Assert.DoesNotContain("Address", command, StringComparison.Ordinal);
            Assert.DoesNotContain("MeterNotes", command, StringComparison.Ordinal);
            Assert.DoesNotContain("CreatedAtUtc", command, StringComparison.Ordinal);
            Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);
        });
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
