using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterReadingGarageScopeTests
{
    [Fact]
    public async Task SqliteYearAndMissingReadingsRespectTheConfiguredServiceScope()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyScopeAsync(database.Context, "service_11111111111111111111111111111111");
        await VerifyScopeAsync(database.Context, MeterKinds.Water);
        await VerifyScopeAsync(database.Context, MeterKinds.Electricity);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlYearAndMissingReadingsRespectTheConfiguredServiceScope()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyScopeAsync(context, "service_11111111111111111111111111111111");
        await VerifyScopeAsync(context, MeterKinds.Water);
        await VerifyScopeAsync(context, MeterKinds.Electricity);
    }

    private static async Task VerifyScopeAsync(GarageBalanceDbContext context, string kind)
    {
        context.MeterReadings.RemoveRange(context.MeterReadings);
        context.Garages.RemoveRange(context.Garages);
        context.ChargeServiceSettings.RemoveRange(context.ChargeServiceSettings);
        await context.SaveChangesAsync();
        var month = new DateOnly(2045, 6, 1);
        var first = new Garage { Number = "1", PeopleCount = 1, FloorCount = 1 };
        var second = new Garage { Number = "2", PeopleCount = 1, FloorCount = 1 };
        var third = new Garage { Number = "10", PeopleCount = 1, FloorCount = 1 };
        var archived = new Garage { Number = "3", PeopleCount = 1, FloorCount = 1, IsArchived = true };
        var setting = new ChargeServiceSetting
        {
            Name = "Коммерческий счётчик",
            IsRegular = true,
            IsMetered = true,
            MeterKind = kind,
            AppliesToSelectedGarages = true,
            GarageIds = [second.Id, third.Id, archived.Id]
        };
        context.Garages.AddRange(first, second, third, archived);
        context.ChargeServiceSettings.AddRange(setting,
            new ChargeServiceSetting { Name = "Другая услуга", IsRegular = true, IsMetered = true, MeterKind = "service_22222222222222222222222222222222" },
            new ChargeServiceSetting { Name = "Архивная услуга", IsRegular = true, IsMetered = true, MeterKind = kind, IsArchived = true },
            new ChargeServiceSetting { Name = "Не счётчик", IsRegular = true, IsMetered = false, MeterKind = kind },
            new ChargeServiceSetting { Name = "Нерегулярная услуга", IsRegular = false, IsMetered = true, MeterKind = kind });
        context.MeterReadings.AddRange(
            new MeterReading { Garage = first, MeterKind = kind, AccountingMonth = month, ReadingDate = month, CurrentValue = 20 },
            new MeterReading { Garage = second, MeterKind = kind, AccountingMonth = month, ReadingDate = month, CurrentValue = 30 },
            new MeterReading { Garage = archived, MeterKind = kind, AccountingMonth = month, ReadingDate = month, CurrentValue = 40 });
        await context.SaveChangesAsync();
        var repository = new EfMeterReadingRepository(context);
        var missingQuery = new EfMissingMeterReadingQuery(context);

        var page = await repository.GetYearPageAsync(2045, kind, 0, 1, CancellationToken.None);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(second.Id, Assert.Single(page.Garages).Id);
        Assert.Equal(second.Id, Assert.Single(page.Readings).GarageId);
        var finalPage = await repository.GetYearPageAsync(2045, kind, 1, 1, CancellationToken.None);
        Assert.Equal(2, finalPage.TotalCount);
        Assert.Equal(third.Id, Assert.Single(finalPage.Garages).Id);
        Assert.Empty(finalPage.Readings);
        var beyondEnd = await repository.GetYearPageAsync(2045, kind, 20, 1, CancellationToken.None);
        Assert.Equal(2, beyondEnd.TotalCount);
        Assert.Empty(beyondEnd.Garages);
        Assert.Empty(beyondEnd.Readings);
        Assert.Equal(third.Id, Assert.Single(await missingQuery.GetMissingAsync(month, [kind], null, 10, CancellationToken.None)).GarageId);
        Assert.Empty(await missingQuery.GetMissingAsync(month, [kind], "2", 10, CancellationToken.None));

        setting.GarageIds = [];
        await context.SaveChangesAsync();
        var empty = await repository.GetYearPageAsync(2045, kind, 0, 10, CancellationToken.None);
        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Garages);
        Assert.Empty(empty.Readings);
        Assert.Empty(await missingQuery.GetMissingAsync(month, [kind], null, 10, CancellationToken.None));

        setting.AppliesToSelectedGarages = false;
        await context.SaveChangesAsync();
        var unrestricted = await repository.GetYearPageAsync(2045, kind, 0, 10, CancellationToken.None);
        Assert.Equal(3, unrestricted.TotalCount);
        Assert.Equal(["1", "2", "10"], unrestricted.Garages.Select(garage => garage.Number));
        Assert.Equal(2, unrestricted.Readings.Count);
        Assert.Equal(third.Id, Assert.Single(await missingQuery.GetMissingAsync(month, [kind], null, 10, CancellationToken.None)).GarageId);

        setting.AppliesToSelectedGarages = true;
        setting.GarageIds = [first.Id];
        await context.SaveChangesAsync();
        var changed = await repository.GetYearPageAsync(2045, kind, 0, 10, CancellationToken.None);
        Assert.Equal(1, changed.TotalCount);
        Assert.Equal(first.Id, Assert.Single(changed.Garages).Id);
        Assert.Equal(first.Id, Assert.Single(changed.Readings).GarageId);
    }
}
