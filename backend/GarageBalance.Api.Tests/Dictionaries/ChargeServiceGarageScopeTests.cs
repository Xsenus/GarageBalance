using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class ChargeServiceGarageScopeTests
{
    [Fact]
    public void RestrictionNeverFallsBackToGeneralTariff()
    {
        var selected = Guid.NewGuid();
        var service = new ChargeServiceSetting { Name = "Охрана", AppliesToSelectedGarages = true, GarageIds = [selected] };
        Assert.True(service.AppliesToGarage(selected));
        Assert.False(service.AppliesToGarage(Guid.NewGuid()));
        service.GarageIds = [];
        Assert.False(service.AppliesToGarage(selected));
        service.AppliesToSelectedGarages = false;
        Assert.True(service.AppliesToGarage(selected));
        Assert.True(service.AppliesToGarage(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("archived")]
    [InlineData("too_many")]
    [InlineData("general_with_selection")]
    public async Task InvalidSelectionDoesNotSaveAnything(string scenario)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = new Garage { Number = "85", IsArchived = scenario == "archived" };
        database.Context.Garages.Add(garage);
        await database.Context.SaveChangesAsync();
        Guid[] ids = scenario switch
        {
            "empty" => [],
            "duplicate" => [garage.Id, garage.Id],
            "missing" => [Guid.NewGuid()],
            "too_many" => Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray(),
            _ => [garage.Id]
        };
        var request = new UpsertChargeServiceSettingRequest("Тест", true, 1, 1, 20, null, 0, false, false, "руб.",
            AppliesToSelectedGarages: scenario != "general_with_selection", GarageIds: ids);
        var result = await DictionaryServiceTestFactory.Create(database.Context).CreateChargeServiceSettingAsync(request, null, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Empty(database.Context.ChargeServiceSettings);
        Assert.Empty(database.Context.AuditEvents);
    }

    [PostgreSqlFact]
    public async Task ScopeMigrationPreservesExistingServicesAndSupportsRollbackAndReapplication()
    {
        const string previous = "20260928122556_SeparateIndividualTariffCatalog";
        await using var database = await PostgreSqlTestDatabase.CreateAsync(previous);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var existing = await context.ChargeServiceSettings.AsNoTracking().ToListAsync();
        Assert.NotEmpty(existing);
        Assert.All(existing, service => { Assert.False(service.AppliesToSelectedGarages); Assert.Empty(service.GarageIds); });
        await context.Database.MigrateAsync(previous);
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();
        var reapplied = await context.ChargeServiceSettings.AsNoTracking().ToListAsync();
        Assert.Equal(existing.Select(service => service.Id).Order(), reapplied.Select(service => service.Id).Order());
        Assert.All(reapplied, service => { Assert.False(service.AppliesToSelectedGarages); Assert.Empty(service.GarageIds); });
    }

    [PostgreSqlFact]
    public async Task ScopeSurvivesPostgreSqlSaveAndCompactListProjection()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "85" };
        var service = new ChargeServiceSetting { Name = "Выборочная услуга", AppliesToSelectedGarages = true, GarageIds = [garage.Id] };
        context.AddRange(garage, service);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var stored = await context.ChargeServiceSettings.SingleAsync(item => item.Id == service.Id);
        Assert.True(stored.AppliesToSelectedGarages);
        Assert.Equal([garage.Id], stored.GarageIds);
        var rows = await new EfChargeServiceSettingRepository(context).GetListAsync(service.Name.ToLowerInvariant(), false, null, null, 10, new DateOnly(2026, 9, 29), CancellationToken.None);
        Assert.True(Assert.Single(rows).AppliesToGarage(garage.Id));
        Assert.False(Assert.Single(rows).AppliesToGarage(Guid.NewGuid()));
        Assert.Equal([garage.Id], Assert.Single(rows).GarageIds);
    }

    [Fact]
    public async Task CommonSavePersistsServiceScopeAndPeriodsAndRejectsAnInvalidSelectionAtomically()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = new Garage { Number = "85" };
        var fund = new Fund { Name = "Охрана", NormalizedName = "ОХРАНА" };
        context.AddRange(garage, fund);
        await context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(context);
        var created = await service.CreateChargeServiceWithTariffAsync(new CreateChargeServiceWithTariffRequest(
            new UpsertChargeServiceSettingRequest("Охрана", true, 1, 1, 20, null, 0, false, false, "руб."),
            350m, new DateOnly(2026, 1, 1), fund.Id), null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var dto = created.Value!;
        var request = new UpsertChargeServiceTariffScheduleRequest(
            [new UpsertChargeServiceTariffPeriodRequest(dto.Tariff.Id, new DateOnly(2026, 1, 1), null, 400m, dto.Tariff.Version)], true, "Проверка", dto.Service.Version,
            new UpsertChargeServiceSettingRequest("Охрана", true, 1, 1, 27, null, 0, false, false, "руб.", dto.Service.IncomeTypeId, dto.Tariff.Id,
                AppliesToSelectedGarages: true, GarageIds: [garage.Id]), fund.Id);
        var saved = await service.UpdateChargeServiceTariffScheduleAsync(dto.Service.Id, request, null, CancellationToken.None);
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        Assert.True(saved.Value!.Service.AppliesToSelectedGarages);
        Assert.Equal([garage.Id], saved.Value.Service.GarageIds);
        Assert.Equal(27, saved.Value.Service.PaymentDueDay);
        Assert.Equal(400m, saved.Value.Tariff.Rate);
        Assert.Contains(context.AuditEvents, audit => audit.Action == "dictionary.charge_service_updated");
        var rejected = await service.UpdateChargeServiceTariffScheduleAsync(dto.Service.Id, request with
        {
            ServiceVersion = saved.Value.Service.Version,
            Service = request.Service! with { GarageIds = [Guid.NewGuid()], PaymentDueDay = 15 },
            Periods = [new UpsertChargeServiceTariffPeriodRequest(saved.Value.Tariff.Id, new DateOnly(2026, 1, 1), null, 500m, saved.Value.Tariff.Version)]
        }, null, CancellationToken.None);
        Assert.False(rejected.Succeeded);
        Assert.Equal("charge_service_garage_not_found", rejected.ErrorCode);
        context.ChangeTracker.Clear();
        var stored = await context.ChargeServiceSettings.SingleAsync();
        Assert.Equal(27, stored.PaymentDueDay);
        Assert.Equal([garage.Id], stored.GarageIds);
        Assert.Equal(400m, (await context.Tariffs.SingleAsync(item => item.Id == saved.Value.Tariff.Id)).Rate);
    }
}
