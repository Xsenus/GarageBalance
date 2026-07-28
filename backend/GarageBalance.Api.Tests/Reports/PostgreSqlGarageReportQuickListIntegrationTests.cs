using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Reports;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlGarageReportQuickListIntegrationTests
{
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
}
