using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlCustomerAccrualAcceptanceIntegrationTests
{
    [PostgreSqlFact]
    public async Task NewGarageWorksheet_DoesNotBackdateAutomaticAccrualsBeforeRegistrationMonth()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage
        {
            Number = "PG-NEW-AUGUST-GARAGE",
            PeopleCount = 1,
            FloorCount = 1,
            CreatedAtUtc = new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero)
        };
        var incomeType = new IncomeType
        {
            Name = "Ежемесячная услуга нового гаража",
            Code = "new_garage_registration_month"
        };
        var tariff = new Tariff
        {
            Name = "Тариф нового гаража",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 300m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Ежемесячная услуга нового гаража",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            OverdueGraceDays = 30,
            IncomeType = incomeType,
            Tariff = tariff,
            UnitName = "руб."
        };
        var erroneousJanuaryAccrual = new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            Tariff = tariff,
            AccountingMonth = new DateOnly(2026, 1, 1),
            Amount = 300m,
            Source = AccrualSources.Regular
        };
        context.AddRange(garage, incomeType, tariff, setting, erroneousJanuaryAccrual);
        await context.SaveChangesAsync();

        var result = await FinanceServiceTestFactory.Create(
                context,
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero)))
            .CalculateGarageIncomeWorksheetAsync(
                garage.Id,
                new GarageIncomeWorksheetRequest(
                    new DateOnly(2026, 1, 1),
                    new DateOnly(2026, 8, 1)),
                Guid.NewGuid(),
                CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var row = Assert.Single(result.Value!.Rows, item => item.IncomeTypeId == incomeType.Id);
        Assert.Equal(new DateOnly(2026, 8, 1), row.AccountingMonth);
        Assert.Equal(300m, row.AccrualAmount);
        Assert.True(erroneousJanuaryAccrual.IsCanceled);
        Assert.Equal(
            [new DateOnly(2026, 8, 1)],
            await context.Accruals
                .Where(accrual =>
                    accrual.GarageId == garage.Id &&
                    accrual.IncomeTypeId == incomeType.Id &&
                    !accrual.IsCanceled)
                .Select(accrual => accrual.AccountingMonth)
                .ToArrayAsync());
        Assert.Contains(await context.AuditEvents.ToListAsync(), audit =>
            audit.Action == "finance.regular_accrual_before_garage_registration_canceled" &&
            audit.EntityId == erroneousJanuaryAccrual.Id.ToString());
    }

    [PostgreSqlFact]
    public async Task MidMonthTariffChange_PersistsDayWeightedRateAndTwoCalculationSegments()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        Guid incomeTypeId;
        await using (var setupContext = database.CreateContext())
        {
            var garage = new Garage
            {
                Number = "PG-CUSTOMER-MID-MONTH",
                PeopleCount = 1,
                FloorCount = 1,
                CreatedAtUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)
            };
            var incomeType = new IncomeType
            {
                Name = "Приемочная услуга со сменой тарифа",
                Code = "customer_mid_month_tariff"
            };
            var firstTariff = new Tariff
            {
                Name = "Приемочный тариф 01–15 августа",
                CalculationBase = TariffCalculationBases.Fixed,
                Rate = 310m,
                EffectiveFrom = new DateOnly(2026, 8, 1)
            };
            var secondTariff = new Tariff
            {
                Name = "Приемочный тариф 16–31 августа",
                CalculationBase = TariffCalculationBases.Fixed,
                Rate = 620m,
                EffectiveFrom = new DateOnly(2026, 8, 16)
            };
            var setting = new ChargeServiceSetting
            {
                Name = "Приемочная услуга со средней месячной ставкой",
                IsRegular = true,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                PaymentDueDay = 20,
                OverdueGraceDays = 30,
                IncomeType = incomeType,
                Tariff = secondTariff,
                UnitName = "руб."
            };
            setupContext.AddRange(garage, incomeType, firstTariff, secondTariff, setting);
            setupContext.ChargeServiceTariffVersions.AddRange(
                new ChargeServiceTariffVersion
                {
                    ChargeServiceSetting = setting,
                    Tariff = firstTariff,
                    EffectiveFrom = new DateOnly(2026, 8, 1),
                    EffectiveTo = new DateOnly(2026, 8, 15)
                },
                new ChargeServiceTariffVersion
                {
                    ChargeServiceSetting = setting,
                    Tariff = secondTariff,
                    EffectiveFrom = new DateOnly(2026, 8, 16)
                });
            await setupContext.SaveChangesAsync();
            garageId = garage.Id;
            incomeTypeId = incomeType.Id;
        }

        await using (var commandContext = database.CreateContext())
        {
            var result = await FinanceServiceTestFactory.Create(commandContext)
                .CalculateGarageIncomeWorksheetAsync(
                    garageId,
                    new GarageIncomeWorksheetRequest(
                        new DateOnly(2026, 8, 1),
                        new DateOnly(2026, 8, 1)),
                    Guid.NewGuid(),
                    CancellationToken.None);

            Assert.True(result.Succeeded, result.ErrorMessage);
            var row = Assert.Single(
                result.Value!.Rows,
                item => item.IncomeTypeId == incomeTypeId);
            Assert.Equal(470m, row.AccrualAmount);
            Assert.NotNull(row.CalculationDetails);
            Assert.Equal(470m, row.CalculationDetails!.AverageRate);
            Assert.Contains("(310 × 15 + 620 × 16) / 31 = 470", row.CalculationDetails.RateAveragingRule, StringComparison.Ordinal);
            Assert.Collection(
                row.CalculationDetails.Lines,
                firstHalf =>
                {
                    Assert.Equal(new DateOnly(2026, 8, 1), firstHalf.EffectiveFrom);
                    Assert.Equal(new DateOnly(2026, 8, 15), firstHalf.EffectiveTo);
                    Assert.Equal(15, firstHalf.Days);
                    Assert.Equal(31, firstHalf.MonthDays);
                    Assert.Equal(150m, firstHalf.Amount);
                },
                secondHalf =>
                {
                    Assert.Equal(new DateOnly(2026, 8, 16), secondHalf.EffectiveFrom);
                    Assert.Equal(new DateOnly(2026, 8, 31), secondHalf.EffectiveTo);
                    Assert.Equal(16, secondHalf.Days);
                    Assert.Equal(31, secondHalf.MonthDays);
                    Assert.Equal(320m, secondHalf.Amount);
                });
        }

        await using var verificationContext = database.CreateContext();
        var persisted = await verificationContext.Accruals
            .SingleAsync(accrual =>
                accrual.GarageId == garageId &&
                accrual.IncomeTypeId == incomeTypeId &&
                !accrual.IsCanceled);
        Assert.Equal(470m, persisted.Amount);
        var persistedDetails = RegularAccrualCalculator.Deserialize(persisted.CalculationDetailsJson);
        Assert.NotNull(persistedDetails);
        Assert.Equal(470m, persistedDetails!.TotalAmount);
        Assert.Equal(470m, persistedDetails.AverageRate);
        Assert.Equal("Расчёт за месяц: 1 месяц × 470 = 470,00.", persistedDetails.MonthlyCalculationFormula);
        Assert.Equal([15, 16], persistedDetails.Lines.Select(line => line.Days));
        Assert.Contains(
            await verificationContext.AuditEvents.ToListAsync(),
            audit => audit.Action == "finance.regular_accrual_calculated_for_garage_worksheet");
    }

    [PostgreSqlFact]
    public async Task PenaltyAccrual_PersistsReasonAndReasonedAuditInPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        Guid penaltyIncomeTypeId;
        await using (var setupContext = database.CreateContext())
        {
            var garage = new Garage
            {
                Number = "PG-CUSTOMER-PENALTY",
                PeopleCount = 1,
                FloorCount = 1
            };
            penaltyIncomeTypeId = await setupContext.IncomeTypes
                .Where(item => item.Code == "penalty" && !item.IsArchived)
                .Select(item => item.Id)
                .SingleAsync();
            setupContext.Garages.Add(garage);
            await setupContext.SaveChangesAsync();
            garageId = garage.Id;
        }

        var actorUserId = Guid.NewGuid();
        const string reason = "Нарушение срока оплаты по акту сверки";
        Guid accrualId;
        await using (var commandContext = database.CreateContext())
        {
            var result = await FinanceServiceTestFactory.Create(commandContext).CreateAccrualAsync(
                new CreateAccrualRequest(
                    garageId,
                    penaltyIncomeTypeId,
                    new DateOnly(2026, 8, 23),
                    1234.56m,
                    AccrualSources.Manual,
                    reason),
                actorUserId,
                CancellationToken.None);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(reason, result.Value!.Comment);
            accrualId = result.Value.Id;
        }

        await using var verificationContext = database.CreateContext();
        var persisted = await verificationContext.Accruals
            .Include(accrual => accrual.IncomeType)
            .SingleAsync(accrual => accrual.Id == accrualId);
        Assert.Equal("penalty", persisted.IncomeType.Code);
        Assert.Equal(new DateOnly(2026, 8, 1), persisted.AccountingMonth);
        Assert.Equal(1234.56m, persisted.Amount);
        Assert.Equal(reason, persisted.Comment);
        var audit = await verificationContext.AuditEvents
            .SingleAsync(item => item.Action == "finance.accrual_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal(garageId.ToString(), audit.RelatedGarageId);
        Assert.Contains("вид Штраф", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"Комментарий: {reason}", audit.Summary, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task CustomerThresholdExample_PersistsProgressiveDayWeightedAmount17755()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        Guid incomeTypeId;
        await using (var setupContext = database.CreateContext())
        {
            var garage = new Garage
            {
                Number = "PG-CUSTOMER-THRESHOLD-EXAMPLE",
                PeopleCount = 1,
                FloorCount = 1,
                CreatedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)
            };
            var incomeType = new IncomeType
            {
                Name = "Электроэнергия по порогам",
                Code = "customer_threshold_example"
            };
            var oldTariff = CreateThresholdTariff("До смены", new DateOnly(2026, 9, 1), 7.5m, 7.5m, 7.5m);
            var newTariff = CreateThresholdTariff("После смены", new DateOnly(2026, 9, 2), 7.5m, 10m, 12m);
            var setting = new ChargeServiceSetting
            {
                Name = "Электроэнергия по порогам",
                IsRegular = true,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                PaymentDueDay = 20,
                OverdueGraceDays = 30,
                IncomeType = incomeType,
                Tariff = newTariff,
                IsMetered = true,
                MeterKind = MeterKinds.Electricity,
                HasTieredTariff = true,
                UnitName = "кВт·ч"
            };
            setupContext.AddRange(
                garage,
                incomeType,
                oldTariff,
                newTariff,
                setting,
                new MeterReading
                {
                    Garage = garage,
                    MeterKind = MeterKinds.Electricity,
                    AccountingMonth = new DateOnly(2026, 9, 1),
                    ReadingDate = new DateOnly(2026, 9, 30),
                    PreviousValue = 0m,
                    CurrentValue = 2000m,
                    Consumption = 2000m
                });
            setupContext.ChargeServiceTariffVersions.AddRange(
                new ChargeServiceTariffVersion
                {
                    ChargeServiceSetting = setting,
                    Tariff = oldTariff,
                    EffectiveFrom = new DateOnly(2026, 9, 1),
                    EffectiveTo = new DateOnly(2026, 9, 1)
                },
                new ChargeServiceTariffVersion
                {
                    ChargeServiceSetting = setting,
                    Tariff = newTariff,
                    EffectiveFrom = new DateOnly(2026, 9, 2)
                });
            await setupContext.SaveChangesAsync();
            garageId = garage.Id;
            incomeTypeId = incomeType.Id;
        }

        await using (var commandContext = database.CreateContext())
        {
            var result = await FinanceServiceTestFactory.Create(commandContext)
                .CalculateGarageIncomeWorksheetAsync(
                    garageId,
                    new GarageIncomeWorksheetRequest(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1)),
                    Guid.NewGuid(),
                    CancellationToken.None);

            Assert.True(result.Succeeded, result.ErrorMessage);
            var row = Assert.Single(result.Value!.Rows, item => item.IncomeTypeId == incomeTypeId);
            Assert.Equal(17755m, row.AccrualAmount);
            Assert.Equal(4, row.CalculationDetails!.Version);
            Assert.Equal(17755m, row.CalculationDetails.TotalAmount);
            Assert.Contains("1100 × 7,5 = 8250,00", row.CalculationDetails.MonthlyCalculationFormula, StringComparison.Ordinal);
            Assert.Contains("600 × 9,9167 = 5950,00", row.CalculationDetails.MonthlyCalculationFormula, StringComparison.Ordinal);
            Assert.Contains("300 × 11,85 = 3555,00", row.CalculationDetails.MonthlyCalculationFormula, StringComparison.Ordinal);
        }

        await using var verificationContext = database.CreateContext();
        var persisted = await verificationContext.Accruals.SingleAsync(accrual =>
            accrual.GarageId == garageId &&
            accrual.IncomeTypeId == incomeTypeId &&
            !accrual.IsCanceled);
        Assert.Equal(17755m, persisted.Amount);
        Assert.Equal(17755m, RegularAccrualCalculator.Deserialize(persisted.CalculationDetailsJson)!.TotalAmount);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static Tariff CreateThresholdTariff(
        string name,
        DateOnly effectiveFrom,
        decimal firstRate,
        decimal secondRate,
        decimal thirdRate) => new()
        {
            Name = name,
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = firstRate,
            ElectricityFirstThreshold = 1100m,
            ElectricitySecondThreshold = 1700m,
            ElectricityFirstRate = firstRate,
            ElectricitySecondRate = secondRate,
            ElectricityThirdRate = thirdRate,
            EffectiveFrom = effectiveFrom
        };
}
