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
    public async Task TariffSchedule_ReplacesRepeatedLegacyTariffAndKeepsGapOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid serviceId;
        Guid serviceVersion;
        Guid firstTariffId;
        Guid firstTariffVersion;
        Guid repeatedTariffId;
        Guid repeatedTariffVersion;
        await using (var setupContext = database.CreateContext())
        {
            var incomeTypeId = await setupContext.IncomeTypes
                .Where(item => item.Code == "water" && !item.IsArchived)
                .Select(item => item.Id)
                .SingleAsync();
            var firstTariff = new Tariff { Name = "Вода 101 — PostgreSQL", CalculationBase = "meter_water", Rate = 101m, EffectiveFrom = new DateOnly(2026, 1, 1) };
            var repeatedTariff = new Tariff { Name = "Вода 105 — PostgreSQL", CalculationBase = "meter_water", Rate = 105m, EffectiveFrom = new DateOnly(2026, 8, 17) };
            var setting = new ChargeServiceSetting
            {
                Name = "Вода — сетка PostgreSQL",
                IsRegular = true,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                PaymentDueDay = 30,
                OverdueGraceDays = 30,
                IncomeTypeId = incomeTypeId,
                TariffId = repeatedTariff.Id,
                IsMetered = true,
                UnitName = "м³"
            };
            setupContext.AddRange(firstTariff, repeatedTariff, setting);
            setupContext.ChargeServiceTariffVersions.AddRange(
                new ChargeServiceTariffVersion { ChargeServiceSettingId = setting.Id, TariffId = firstTariff.Id, EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 8, 16) },
                new ChargeServiceTariffVersion { ChargeServiceSettingId = setting.Id, TariffId = repeatedTariff.Id, EffectiveFrom = new DateOnly(2026, 8, 17), EffectiveTo = new DateOnly(2026, 8, 25) },
                new ChargeServiceTariffVersion { ChargeServiceSettingId = setting.Id, TariffId = repeatedTariff.Id, EffectiveFrom = new DateOnly(2026, 9, 2) });
            await setupContext.SaveChangesAsync();
            serviceId = setting.Id;
            serviceVersion = setting.Version;
            firstTariffId = firstTariff.Id;
            firstTariffVersion = firstTariff.Version;
            repeatedTariffId = repeatedTariff.Id;
            repeatedTariffVersion = repeatedTariff.Version;
        }

        await using (var commandContext = database.CreateContext())
        {
            var result = await DictionaryServiceTestFactory.Create(commandContext, new DateOnly(2026, 9, 2))
                .UpdateChargeServiceTariffScheduleAsync(
                    serviceId,
                    new UpsertChargeServiceTariffScheduleRequest(
                        [
                            new(firstTariffId, new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 16), 101m, firstTariffVersion),
                            new(repeatedTariffId, new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 25), 103m, repeatedTariffVersion),
                            new(repeatedTariffId, new DateOnly(2026, 9, 2), null, 105m, repeatedTariffVersion)
                        ],
                        true,
                        "Исправление сетки PostgreSQL",
                        serviceVersion),
                    Guid.NewGuid(),
                    CancellationToken.None);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal([101m, 103m, 105m], result.Value!.Periods.Select(item => item.Rate));
        }

        await using var verificationContext = database.CreateContext();
        var periods = await verificationContext.ChargeServiceTariffVersions
            .Where(item => item.ChargeServiceSettingId == serviceId && !item.IsArchived)
            .OrderBy(item => item.EffectiveFrom)
            .ToListAsync();
        Assert.Equal(3, periods.Count);
        Assert.Equal(3, periods.Select(item => item.TariffId).Distinct().Count());
        Assert.Equal(new DateOnly(2026, 8, 25), periods[1].EffectiveTo);
        Assert.Equal(new DateOnly(2026, 9, 2), periods[2].EffectiveFrom);
    }

    [PostgreSqlFact]
    public async Task TieredWaterModeChange_UsesCurrentScheduleTariffWhenStoredPointerIsClosedOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid incomeTypeId;
        Guid serviceId;
        Guid serviceVersion;
        Guid currentTariffId;
        Guid currentTariffVersion;
        await using (var setupContext = database.CreateContext())
        {
            incomeTypeId = await setupContext.IncomeTypes
                .Where(item => item.Code == "water" && !item.IsArchived)
                .Select(item => item.Id)
                .SingleAsync();
            var closedTariff = new Tariff
            {
                Name = "Вода — закрытый указатель PostgreSQL",
                CalculationBase = "meter_electricity",
                Rate = 105m,
                EffectiveFrom = new DateOnly(2026, 8, 26)
            };
            var currentTariff = new Tariff
            {
                Name = "Вода — действующий период PostgreSQL",
                CalculationBase = "meter_electricity",
                Rate = 103m,
                EffectiveFrom = new DateOnly(2026, 9, 2)
            };
            var setting = new ChargeServiceSetting
            {
                Name = "Вода — закрытый указатель PostgreSQL",
                IsRegular = true,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                PaymentDueDay = 20,
                OverdueGraceDays = 30,
                IncomeTypeId = incomeTypeId,
                TariffId = closedTariff.Id,
                IsMetered = true,
                UnitName = "м³",
                MeterKind = "water"
            };
            setupContext.AddRange(closedTariff, currentTariff, setting);
            setupContext.ChargeServiceTariffVersions.AddRange(
                new ChargeServiceTariffVersion
                {
                    ChargeServiceSettingId = setting.Id,
                    TariffId = closedTariff.Id,
                    EffectiveFrom = new DateOnly(2026, 8, 26),
                    EffectiveTo = new DateOnly(2026, 9, 1)
                },
                new ChargeServiceTariffVersion
                {
                    ChargeServiceSettingId = setting.Id,
                    TariffId = currentTariff.Id,
                    EffectiveFrom = new DateOnly(2026, 9, 2)
                });
            await setupContext.SaveChangesAsync();
            serviceId = setting.Id;
            serviceVersion = setting.Version;
            currentTariffId = currentTariff.Id;
            currentTariffVersion = currentTariff.Version;
        }

        await using var commandContext = database.CreateContext();
        var result = await DictionaryServiceTestFactory.Create(commandContext, new DateOnly(2026, 9, 4))
            .UpdateChargeServiceWithTariffAsync(
                serviceId,
                new UpdateChargeServiceWithTariffRequest(
                    new UpsertChargeServiceSettingRequest(
                        "Вода — закрытый указатель PostgreSQL",
                        true,
                        1,
                        1,
                        20,
                        null,
                        30,
                        true,
                        true,
                        "м³",
                        incomeTypeId,
                        currentTariffId,
                        serviceVersion),
                    103m,
                    "metered_tiered",
                    new DateOnly(2026, 9, 4),
                    [
                        new(null, "До 100", 100m, 103m),
                        new(null, "Свыше 100", null, 120m)
                    ],
                    "Включение порогов воды",
                    "meter_water",
                    currentTariffVersion),
                Guid.NewGuid(),
                CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(result.Value!.Service.HasTieredTariff);
        Assert.Equal("meter_water", result.Value.Tariff.CalculationBase);
        Assert.Equal(3, await commandContext.ChargeServiceTariffVersions.CountAsync(item => item.ChargeServiceSettingId == serviceId));
    }

    [PostgreSqlFact]
    public async Task ConsecutiveWaterModeChanges_OnSameDateUseReturnedVersionsOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid incomeTypeId;
        Guid serviceId;
        Guid sourceTariffId;
        Guid serviceVersion;
        Guid tariffVersion;
        await using (var setupContext = database.CreateContext())
        {
            incomeTypeId = await setupContext.IncomeTypes
                .Where(item => item.Code == "water" && !item.IsArchived)
                .Select(item => item.Id)
                .SingleAsync();
            var sourceTariff = new Tariff
            {
                Name = "Вода — последовательная смена режима",
                CalculationBase = "meter_water",
                Rate = 103m,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            };
            var setting = new ChargeServiceSetting
            {
                Name = "Вода — последовательная смена режима",
                IsRegular = true,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                PaymentDueDay = 20,
                OverdueGraceDays = 30,
                IncomeTypeId = incomeTypeId,
                TariffId = sourceTariff.Id,
                IsMetered = true,
                UnitName = "м³",
                MeterKind = "water"
            };
            setupContext.AddRange(sourceTariff, setting);
            await setupContext.SaveChangesAsync();
            serviceId = setting.Id;
            sourceTariffId = sourceTariff.Id;
            serviceVersion = setting.Version;
            tariffVersion = sourceTariff.Version;
        }

        UpdatedChargeServiceWithTariffDto regular;
        await using (var regularContext = database.CreateContext())
        {
            var result = await DictionaryServiceTestFactory.Create(regularContext, new DateOnly(2026, 9, 3))
                .UpdateChargeServiceWithTariffAsync(
                    serviceId,
                    new UpdateChargeServiceWithTariffRequest(
                        new UpsertChargeServiceSettingRequest(
                            "Вода — последовательная смена режима",
                            true,
                            1,
                            1,
                            20,
                            null,
                            30,
                            false,
                            false,
                            "м³",
                            incomeTypeId,
                            sourceTariffId,
                            serviceVersion),
                        103m,
                        "regular",
                        new DateOnly(2026, 9, 3),
                        ChangeReason: "Отключение счетчика",
                        CalculationBase: "fixed",
                        TariffVersion: tariffVersion),
                    Guid.NewGuid(),
                    CancellationToken.None);

            Assert.True(result.Succeeded, result.ErrorMessage);
            regular = result.Value!;
            Assert.False(regular.Service.IsMetered);
        }

        await using (var tieredContext = database.CreateContext())
        {
            var result = await DictionaryServiceTestFactory.Create(tieredContext, new DateOnly(2026, 9, 3))
                .UpdateChargeServiceWithTariffAsync(
                    serviceId,
                    new UpdateChargeServiceWithTariffRequest(
                        new UpsertChargeServiceSettingRequest(
                            regular.Service.Name,
                            true,
                            regular.Service.PeriodicityMonths,
                            regular.Service.AccrualStartMonth,
                            regular.Service.PaymentDueDay,
                            regular.Service.PaymentDueMonth,
                            regular.Service.OverdueGraceDays,
                            true,
                            true,
                            regular.Service.UnitName,
                            regular.Service.IncomeTypeId,
                            regular.Tariff.Id,
                            regular.Service.Version),
                        regular.Tariff.Rate,
                        "metered_tiered",
                        new DateOnly(2026, 9, 3),
                        [
                            new(null, "До 100", 100m, 103m),
                            new(null, "Свыше 100", null, 120m)
                        ],
                        "Включение порогов воды",
                        "meter_water",
                        regular.Tariff.Version),
                    Guid.NewGuid(),
                    CancellationToken.None);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.True(result.Value!.Service.IsMetered);
            Assert.True(result.Value.Service.HasTieredTariff);
            Assert.Equal("water", result.Value.Service.MeterKind);
            Assert.Equal("meter_water", result.Value.Tariff.CalculationBase);
            Assert.Equal(regular.Tariff.Id, result.Value.Tariff.Id);
            Assert.NotEqual(regular.Tariff.Version, result.Value.Tariff.Version);
        }
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
