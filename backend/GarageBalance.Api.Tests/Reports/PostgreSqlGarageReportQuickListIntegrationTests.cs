using System.Data.Common;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Reports;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlGarageReportQuickListIntegrationTests
{
    [PostgreSqlFact]
    public async Task QuickListRead_UsesOneCompactUntrackedProjection()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var actorUserId = Guid.NewGuid();
        var owner = new Owner
        {
            LastName = "Иванов",
            FirstName = "Иван",
            MiddleName = "Иванович",
            Phone = "+7 900 000-00-00",
            Address = "Не должно загружаться",
            MeterNotes = "Не должны загружаться"
        };
        var garage = new Garage
        {
            Number = "КОМПАКТ-1",
            PeopleCount = 4,
            FloorCount = 3,
            StartingBalance = 1234m,
            StartingOverdueDebt = 345m,
            InitialWaterMeterValue = 56m,
            InitialElectricityMeterValue = 789m,
            Comment = "Не должен загружаться",
            IsArchived = true,
            Owner = owner
        };
        var quickList = new GarageReportQuickList
        {
            Name = "Компактный список",
            NormalizedName = "КОМПАКТНЫЙ СПИСОК",
            UpdatedByUserId = actorUserId,
            Garages = [new GarageReportQuickListGarage { Garage = garage, GarageId = garage.Id }]
        };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.GarageReportQuickLists.Add(quickList);
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var result = await CreateService(queryContext).GetAllAsync(CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal("Компактный список", item.Name);
        Assert.Equal(actorUserId, item.UpdatedByUserId);
        var selectedGarage = Assert.Single(item.Garages);
        Assert.Equal("КОМПАКТ-1", selectedGarage.GarageNumber);
        Assert.Equal("Иванов Иван Иванович", selectedGarage.OwnerName);
        Assert.True(selectedGarage.IsArchived);
        Assert.Empty(queryContext.ChangeTracker.Entries());

        var command = Assert.Single(capture.Commands);
        Assert.Contains("garage_report_quick_lists", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("garage_report_quick_list_garages", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PeopleCount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("FloorCount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("StartingBalance", command, StringComparison.Ordinal);
        Assert.DoesNotContain("StartingOverdueDebt", command, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialWaterMeterValue", command, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialElectricityMeterValue", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Comment", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Phone", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Address", command, StringComparison.Ordinal);
        Assert.DoesNotContain("MeterNotes", command, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAtUtc", command, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task QuickList_PersistsRussianNameMembershipAndUniqueConstraint()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid firstGarageId;
        Guid secondGarageId;
        await using (var setupContext = database.CreateContext())
        {
            var garages = new[]
            {
                new Garage { Number = $"Б-{Guid.NewGuid():N}", PeopleCount = 1, FloorCount = 1 },
                new Garage { Number = $"А-{Guid.NewGuid():N}", PeopleCount = 1, FloorCount = 1 }
            };
            setupContext.Garages.AddRange(garages);
            await setupContext.SaveChangesAsync();
            firstGarageId = garages[0].Id;
            secondGarageId = garages[1].Id;
        }

        Guid quickListId;
        await using (var createContext = database.CreateContext())
        {
            var service = CreateService(createContext);
            var created = await service.CreateAsync(
                new UpsertGarageReportQuickListRequest("Просроченная задолженность", [firstGarageId, secondGarageId]),
                Guid.NewGuid(),
                CancellationToken.None);
            Assert.True(created.Succeeded, created.ErrorMessage);
            quickListId = created.Value!.Id;
        }

        await using (var verifyContext = database.CreateContext())
        {
            var quickList = await verifyContext.GarageReportQuickLists
                .Include(item => item.Garages)
                .SingleAsync(item => item.Id == quickListId);
            Assert.Equal("ПРОСРОЧЕННАЯ ЗАДОЛЖЕННОСТЬ", quickList.NormalizedName);
            Assert.Equal(2, quickList.Garages.Count);
            Assert.Single(verifyContext.AuditEvents, item => item.Action == "reports.garage_quick_list_created");

            verifyContext.GarageReportQuickLists.Add(new GarageReportQuickList
            {
                Name = "Дубликат",
                NormalizedName = quickList.NormalizedName
            });
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => verifyContext.SaveChangesAsync());
            Assert.NotNull(exception.InnerException);
        }
    }

    private static GarageReportQuickListService CreateService(GarageBalanceDbContext context)
    {
        return new GarageReportQuickListService(
            new EfGarageReportQuickListRepository(context),
            new AuditEventWriter(context));
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
