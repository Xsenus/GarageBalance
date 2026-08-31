using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlOwnerListProjectionIntegrationTests
{
    [PostgreSqlFact]
    public async Task GetListAsync_UsesOneBoundedCompactProjectionForDisplayedOwnerData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var firstOwner = new Owner
        {
            LastName = "Альфа %_ 88421",
            FirstName = "Анна",
            MiddleName = "Ивановна",
            Phone = "+7 900 111-22-33",
            Address = "Отображаемый адрес",
            MeterNotes = "Отображаемая заметка"
        };
        var activeGarage = new Garage
        {
            Number = "OWN-COMPACT-02",
            PeopleCount = 7,
            FloorCount = 3,
            StartingBalance = 900m,
            StartingOverdueDebt = 120m,
            InitialWaterMeterValue = 45m,
            InitialElectricityMeterValue = 67m,
            Comment = "Не должен загружаться",
            Owner = firstOwner
        };
        var archivedGarage = new Garage
        {
            Number = "OWN-COMPACT-01",
            PeopleCount = 5,
            IsArchived = true,
            Owner = firstOwner
        };
        var secondOwner = new Owner { LastName = "Бета 88421", FirstName = "Борис" };
        var excludedByLimitOwner = new Owner { LastName = "Вега 88421", FirstName = "Виктор" };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(firstOwner, secondOwner, excludedByLimitOwner, activeGarage, archivedGarage);
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfOwnerRepository(queryContext);

        var result = await repository.GetListAsync(null, false, 2, CancellationToken.None);

        Assert.Equal([firstOwner.Id, secondOwner.Id], result.Select(owner => owner.Id));
        var actual = result[0];
        Assert.Equal("Альфа %_ 88421", actual.LastName);
        Assert.Equal("Анна", actual.FirstName);
        Assert.Equal("Ивановна", actual.MiddleName);
        Assert.Equal("+7 900 111-22-33", actual.Phone);
        Assert.Equal("Отображаемый адрес", actual.Address);
        Assert.Equal("Отображаемая заметка", actual.MeterNotes);
        Assert.Equal(
            [archivedGarage.Id, activeGarage.Id],
            actual.Garages.OrderBy(garage => garage.Number).Select(garage => garage.Id));
        Assert.All(actual.Garages, garage => Assert.Same(actual, garage.Owner));
        Assert.Empty(result[1].Garages);
        Assert.Empty(queryContext.ChangeTracker.Entries());

        var command = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFT JOIN", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Number", command, StringComparison.Ordinal);
        Assert.DoesNotContain("PeopleCount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("FloorCount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("StartingBalance", command, StringComparison.Ordinal);
        Assert.DoesNotContain("StartingOverdueDebt", command, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialWaterMeterValue", command, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialElectricityMeterValue", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Comment", command, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Version", command, StringComparison.Ordinal);

        var literalSearch = await repository.GetListAsync("%_", false, 10, CancellationToken.None);

        Assert.Equal(firstOwner.Id, Assert.Single(literalSearch).Id);
        var searchCommand = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("ILIKE", searchCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE '\\'", searchCommand, StringComparison.Ordinal);
        Assert.Contains("LIMIT", searchCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PeopleCount", searchCommand, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task GetListAsync_PropagatesCancellationBeforeDatabaseRead()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfOwnerRepository(queryContext);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.GetListAsync(null, false, 10, cancellation.Token));

        Assert.Empty(capture.TakeCommandsAndClear());
        Assert.Empty(queryContext.ChangeTracker.Entries());
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        private readonly List<string> commands = [];

        public IReadOnlyList<string> TakeCommandsAndClear()
        {
            var result = commands.ToArray();
            commands.Clear();
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
