using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class TariffEarlierDateTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("regular", false)]
    [InlineData("metered", false)]
    [InlineData("metered_tiered", false)]
    [InlineData("regular", true)]
    public async Task EarlierDateRejectsAllModesBeforeAnyMutation(string? mode, bool missingRequestedTariff)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyRejectedDateAsync(database.Context, mode, missingRequestedTariff);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlEarlierDatePreservesPeriodsFundsAndAudit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        foreach (var mode in new string?[] { null, "regular", "metered", "metered_tiered" })
        {
            await VerifyRejectedDateAsync(context, mode);
        }
    }

    private static async Task VerifyRejectedDateAsync(GarageBalanceDbContext context, string? mode, bool missingRequestedTariff = false)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var originalFund = new Fund { Name = $"Исходный {suffix}", NormalizedName = $"ИСХОДНЫЙ {suffix}", SortOrder = 80, Balance = 100m };
        var requestedFund = new Fund { Name = $"Другой {suffix}", NormalizedName = $"ДРУГОЙ {suffix}", SortOrder = 81, Balance = 200m };
        var income = new IncomeType { Name = $"Услуга {suffix}", DestinationFundId = originalFund.Id };
        var tariff = new Tariff
        {
            Name = $"Тариф {suffix}",
            CalculationBase = "meter_water",
            Rate = 100m,
            EffectiveFrom = new DateOnly(2026, 8, 1)
        };
        var setting = new ChargeServiceSetting
        {
            Name = $"Услуга {suffix}",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            OverdueGraceDays = 30,
            IsMetered = true,
            UnitName = "м³",
            IncomeTypeId = income.Id,
            TariffId = tariff.Id
        };
        var period = new ChargeServiceTariffVersion
        {
            ChargeServiceSettingId = setting.Id,
            TariffId = tariff.Id,
            EffectiveFrom = tariff.EffectiveFrom
        };
        var archivedPeriod = new ChargeServiceTariffVersion
        {
            ChargeServiceSettingId = setting.Id,
            TariffId = tariff.Id,
            EffectiveFrom = new DateOnly(2026, 7, 31),
            EffectiveTo = new DateOnly(2026, 7, 31),
            IsArchived = true
        };
        context.AddRange(originalFund, requestedFund, income, tariff, setting, period, archivedPeriod);
        await context.SaveChangesAsync();
        var oldName = setting.Name;
        var serviceVersion = setting.Version;
        var tariffVersion = tariff.Version;
        var tariffCount = await context.Tariffs.CountAsync();
        var auditCount = await context.AuditEvents.CountAsync();
        var unitCount = await context.MeasurementUnits.CountAsync();
        var isMetered = mode != "regular";
        var request = new UpdateChargeServiceWithTariffRequest(
            new UpsertChargeServiceSettingRequest(
                $"Переименованная {suffix}", true, 1, 1, 25, null, 20,
                isMetered, mode == "metered_tiered", $"новая-{suffix[..8]}", income.Id, missingRequestedTariff ? Guid.NewGuid() : tariff.Id,
                Version: serviceVersion),
            110m, TariffMode: mode, EffectiveFrom: new DateOnly(2026, 7, 31),
            TariffVersion: tariffVersion, IncomeFundId: requestedFund.Id);

        var result = await DictionaryServiceTestFactory.Create(context).UpdateChargeServiceWithTariffAsync(
            setting.Id, request, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("charge_service_tariff_date_before_source", result.ErrorCode);
        Assert.Contains("01.08.2026", result.ErrorMessage);
        Assert.Equal(0, await context.SaveChangesAsync());
        Assert.Equal(oldName, setting.Name);
        Assert.Equal(serviceVersion, setting.Version);
        Assert.Equal(tariffVersion, tariff.Version);
        Assert.Equal(100m, tariff.Rate);
        Assert.Equal(new DateOnly(2026, 8, 1), tariff.EffectiveFrom);
        Assert.Equal(tariff.EffectiveFrom, period.EffectiveFrom);
        Assert.Null(period.EffectiveTo);
        Assert.False(period.IsArchived);
        Assert.Equal(originalFund.Id, income.DestinationFundId);
        Assert.Equal(100m, originalFund.Balance);
        Assert.Equal(200m, requestedFund.Balance);
        Assert.Equal(tariffCount, await context.Tariffs.CountAsync());
        Assert.Equal(auditCount, await context.AuditEvents.CountAsync());
        Assert.Equal(unitCount, await context.MeasurementUnits.CountAsync());
        Assert.True(archivedPeriod.IsArchived);
        Assert.Single(await context.ChargeServiceTariffVersions.Where(item => item.ChargeServiceSettingId == setting.Id && !item.IsArchived).ToListAsync());
    }
}
