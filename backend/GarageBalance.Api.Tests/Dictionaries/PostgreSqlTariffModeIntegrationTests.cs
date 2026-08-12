using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlTariffModeIntegrationTests
{
    [PostgreSqlFact]
    public async Task TariffModeChange_CreatesVersionAndSwitchesServiceAtomicallyOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var sourceTariff = new Tariff
        {
            Name = "Электроэнергия — обычный",
            CalculationBase = "fixed",
            Rate = 7.47m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        ChargeServiceSetting setting;
        Guid incomeTypeId;

        await using (var setupContext = database.CreateContext())
        {
            incomeTypeId = await setupContext.IncomeTypes
                .Where(item => item.Code == "electricity" && !item.IsArchived)
                .Select(item => item.Id)
                .SingleAsync();
            setting = new ChargeServiceSetting
            {
                Name = "Электроэнергия — интеграционный тест",
                IsRegular = true,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                PaymentDueDay = 30,
                OverdueGraceDays = 30,
                IncomeTypeId = incomeTypeId,
                TariffId = sourceTariff.Id,
                UnitName = "руб."
            };
            setupContext.AddRange(sourceTariff, setting);
            await setupContext.SaveChangesAsync();
        }

        Guid versionTariffId;
        await using (var commandContext = database.CreateContext())
        {
            var service = DictionaryServiceTestFactory.Create(commandContext);
            var result = await service.UpdateChargeServiceWithTariffAsync(
                setting.Id,
                new UpdateChargeServiceWithTariffRequest(
                    new UpsertChargeServiceSettingRequest(
                        setting.Name,
                        true,
                        1,
                        1,
                        30,
                        null,
                        30,
                        true,
                        true,
                        "кВт·ч",
                        incomeTypeId,
                        sourceTariff.Id),
                    7.47m,
                    "metered_tiered",
                    new DateOnly(2026, 8, 1),
                    [
                        new(null, "Первый порог", 1000m, 7.47m),
                        new(null, "Второй порог", 1500m, 10.17m),
                        new(null, "Без верхней границы", null, 14.88m)
                    ],
                    "Переход на пороговый счетчик",
                    "meter_electricity"),
                Guid.NewGuid(),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            versionTariffId = result.Value!.Tariff.Id;
            Assert.NotEqual(sourceTariff.Id, versionTariffId);

            var measurementUnits = new EfMeasurementUnitRepository(commandContext);
            Assert.NotNull(await measurementUnits.FindActiveByNameAsync("КВТ·Ч", CancellationToken.None));
            Assert.True(await measurementUnits.ActiveDuplicateExistsAsync(null, "КВТ·Ч", CancellationToken.None));
            Assert.True(await measurementUnits.HasActiveServiceAssignmentsAsync("КВТ·Ч", CancellationToken.None));
            await measurementUnits.RenameServiceAssignmentsAsync("КВТ·Ч", "кВтч", CancellationToken.None);
            await commandContext.SaveChangesAsync();
            Assert.Equal("кВтч", (await commandContext.ChargeServiceSettings.SingleAsync(item => item.Id == setting.Id)).UnitName);
            await measurementUnits.RenameServiceAssignmentsAsync("КВТЧ", "кВт·ч", CancellationToken.None);
            await commandContext.SaveChangesAsync();
        }

        await using var verificationContext = database.CreateContext();
        var savedService = await verificationContext.ChargeServiceSettings.SingleAsync(item => item.Id == setting.Id);
        var savedTariffs = await verificationContext.Tariffs
            .Where(item => item.Id == sourceTariff.Id || item.Id == versionTariffId)
            .OrderBy(item => item.CreatedAtUtc)
            .ToListAsync();
        var savedAudits = await verificationContext.AuditEvents
            .Where(item => item.EntityId == setting.Id.ToString() || item.EntityId == versionTariffId.ToString())
            .ToListAsync();
        var savedVersions = await verificationContext.ChargeServiceTariffVersions
            .Where(item => item.ChargeServiceSettingId == setting.Id)
            .OrderBy(item => item.EffectiveFrom)
            .ToListAsync();

        Assert.Equal(versionTariffId, savedService.TariffId);
        Assert.True(savedService.IsMetered);
        Assert.True(savedService.HasTieredTariff);
        Assert.Equal("кВт·ч", savedService.UnitName);
        Assert.Equal(2, savedTariffs.Count);
        Assert.Equal("fixed", savedTariffs.Single(item => item.Id == sourceTariff.Id).CalculationBase);
        var version = savedTariffs.Single(item => item.Id == versionTariffId);
        Assert.Equal("meter_electricity", version.CalculationBase);
        Assert.Contains("1000", version.ElectricityTiersJson, StringComparison.Ordinal);
        Assert.Contains("1500", version.ElectricityTiersJson, StringComparison.Ordinal);
        Assert.Collection(
            savedVersions,
            item =>
            {
                Assert.Equal(new DateOnly(2026, 1, 1), item.EffectiveFrom);
                Assert.Equal(sourceTariff.Id, item.TariffId);
            },
            item =>
            {
                Assert.Equal(new DateOnly(2026, 8, 1), item.EffectiveFrom);
                Assert.Equal(versionTariffId, item.TariffId);
            });
        Assert.Equal(2, savedAudits.Count);
        Assert.Contains(savedAudits, item => item.Action == "dictionary.tariff_created");
        Assert.Contains(savedAudits, item => item.Action == "dictionary.charge_service_tariff_mode_changed");
    }

    [PostgreSqlFact]
    public async Task ConcurrentTariffModeChanges_AreSerializedWithoutPartialServiceStateOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid incomeTypeId;
        var sourceTariff = new Tariff
        {
            Name = "Электроэнергия — конкурентный тест",
            CalculationBase = "fixed",
            Rate = 8m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        ChargeServiceSetting setting;
        await using (var setupContext = database.CreateContext())
        {
            incomeTypeId = await setupContext.IncomeTypes
                .Where(item => item.Code == "electricity" && !item.IsArchived)
                .Select(item => item.Id)
                .SingleAsync();
            setting = new ChargeServiceSetting
            {
                Name = "Электроэнергия — конкурентный тест",
                IsRegular = true,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                PaymentDueDay = 30,
                OverdueGraceDays = 30,
                IncomeTypeId = incomeTypeId,
                TariffId = sourceTariff.Id,
                UnitName = "руб."
            };
            setupContext.AddRange(sourceTariff, setting);
            await setupContext.SaveChangesAsync();
        }

        var tieredRequest = CreateModeRequest(setting, incomeTypeId, sourceTariff.Id, true, true, "metered_tiered", "meter_electricity");
        var regularRequest = CreateModeRequest(setting, incomeTypeId, sourceTariff.Id, false, false, "regular", "fixed");
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var results = await Task.WhenAll(
            DictionaryServiceTestFactory.Create(firstContext).UpdateChargeServiceWithTariffAsync(
                setting.Id, tieredRequest, Guid.NewGuid(), CancellationToken.None),
            DictionaryServiceTestFactory.Create(secondContext).UpdateChargeServiceWithTariffAsync(
                setting.Id, regularRequest, Guid.NewGuid(), CancellationToken.None));

        Assert.All(results, result => Assert.True(result.Succeeded));
        var createdTariffIds = results.Select(result => result.Value!.Tariff.Id).ToHashSet();
        Assert.Single(createdTariffIds);

        await using var verificationContext = database.CreateContext();
        var savedService = await verificationContext.ChargeServiceSettings.SingleAsync(item => item.Id == setting.Id);
        var selectedTariff = await verificationContext.Tariffs.SingleAsync(item => item.Id == savedService.TariffId);
        var effectiveVersion = await verificationContext.ChargeServiceTariffVersions
            .SingleAsync(item => item.ChargeServiceSettingId == setting.Id && item.EffectiveFrom == new DateOnly(2026, 8, 1));
        Assert.Contains(savedService.TariffId!.Value, createdTariffIds);
        Assert.Equal(savedService.TariffId, effectiveVersion.TariffId);
        if (selectedTariff.CalculationBase == "meter_electricity")
        {
            Assert.True(savedService.IsMetered);
            Assert.True(savedService.HasTieredTariff);
            Assert.Equal("кВт·ч", savedService.UnitName);
        }
        else
        {
            Assert.Equal("fixed", selectedTariff.CalculationBase);
            Assert.False(savedService.IsMetered);
            Assert.False(savedService.HasTieredTariff);
            Assert.Equal("руб.", savedService.UnitName);
        }
    }

    private static UpdateChargeServiceWithTariffRequest CreateModeRequest(
        ChargeServiceSetting setting,
        Guid incomeTypeId,
        Guid tariffId,
        bool isMetered,
        bool isTiered,
        string mode,
        string calculationBase) =>
        new(
            new UpsertChargeServiceSettingRequest(
                setting.Name,
                true,
                1,
                1,
                30,
                null,
                30,
                isMetered,
                isTiered,
                isMetered ? "кВт·ч" : "руб.",
                incomeTypeId,
                tariffId),
            8m,
            mode,
            new DateOnly(2026, 8, 1),
            ChangeReason: "Конкурентная смена режима",
            CalculationBase: calculationBase);
}
