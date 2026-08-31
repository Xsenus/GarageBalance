using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlCustomerAccrualAcceptanceIntegrationTests
{
    [PostgreSqlFact]
    public async Task MidMonthTariffChange_PersistsCalendarDayProrationAndTwoCalculationSegments()
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
                FloorCount = 1
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
                Name = "Приемочная услуга с посуточным расчетом",
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
            Assert.Collection(
                row.CalculationDetails!.Lines,
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
}
