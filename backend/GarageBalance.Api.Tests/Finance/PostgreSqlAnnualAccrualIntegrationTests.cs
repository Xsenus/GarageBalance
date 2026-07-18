using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlAnnualAccrualIntegrationTests
{
    [PostgreSqlFact]
    public async Task AnnualDeadlines_FromMigratedCatalogDriveOverdueDebtBoundariesAndOutstandingAmounts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "PG-ANNUAL-DEADLINES", PeopleCount = 1, FloorCount = 1 };
        context.Garages.Add(garage);
        await context.SaveChangesAsync();
        var annualServices = await context.ChargeServiceSettings.AsNoTracking()
            .Where(setting =>
                !setting.IsArchived &&
                setting.IsRegular &&
                setting.IncomeTypeId.HasValue &&
                setting.TariffId.HasValue &&
                (setting.IncomeType!.Code == "membership" ||
                 setting.IncomeType.Code == "target" ||
                 setting.IncomeType.Code == "outdoor_lighting"))
            .Select(setting => new
            {
                IncomeTypeId = setting.IncomeTypeId!.Value,
                IncomeTypeCode = setting.IncomeType!.Code!,
                TariffId = setting.TariffId!.Value
            })
            .ToDictionaryAsync(setting => setting.IncomeTypeCode, StringComparer.Ordinal);
        Assert.Equal(3, annualServices.Count);
        var financeService = FinanceServiceTestFactory.Create(context);

        foreach (var service in annualServices.Values)
        {
            var generated = await financeService.GenerateRegularAccrualsAsync(
                new GenerateRegularAccrualsRequest(
                    service.IncomeTypeId,
                    service.TariffId,
                    new DateOnly(2026, 9, 1),
                    "Проверка годовых сроков"),
                null,
                CancellationToken.None);
            Assert.True(generated.Succeeded, generated.ErrorMessage);
        }

        var garageAccruals = await context.Accruals
            .Where(accrual => accrual.GarageId == garage.Id && accrual.AccountingYear == 2026)
            .Include(accrual => accrual.IncomeType)
            .ToDictionaryAsync(accrual => accrual.IncomeType.Code!);
        Assert.Equal(new DateOnly(2026, 6, 30), garageAccruals["membership"].DueDate);
        Assert.Equal(new DateOnly(2026, 7, 31), garageAccruals["membership"].OverdueFromDate);
        Assert.Equal(new DateOnly(2026, 6, 30), garageAccruals["target"].DueDate);
        Assert.Equal(new DateOnly(2026, 7, 31), garageAccruals["target"].OverdueFromDate);
        Assert.Equal(new DateOnly(2026, 12, 31), garageAccruals["outdoor_lighting"].DueDate);
        Assert.Equal(new DateOnly(2027, 1, 1), garageAccruals["outdoor_lighting"].OverdueFromDate);

        var membershipOutstanding = 100m;
        var membershipPayment = await financeService.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                garage.Id,
                annualServices["membership"].IncomeTypeId,
                new DateOnly(2026, 7, 30),
                new DateOnly(2026, 7, 1),
                garageAccruals["membership"].Amount - membershipOutstanding,
                "PG-ANNUAL-MEMBERSHIP-PARTIAL",
                "Частичная оплата"),
            null,
            CancellationToken.None);
        Assert.True(membershipPayment.Succeeded, membershipPayment.ErrorMessage);
        var targetPayment = await financeService.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                garage.Id,
                annualServices["target"].IncomeTypeId,
                new DateOnly(2026, 7, 30),
                new DateOnly(2026, 7, 1),
                garageAccruals["target"].Amount,
                "PG-ANNUAL-TARGET-FULL",
                "Полная оплата"),
            null,
            CancellationToken.None);
        Assert.True(targetPayment.Succeeded, targetPayment.ErrorMessage);

        var beforeOverdue = await FinanceServiceTestFactory.Create(
            context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero)))
            .GetGarageOverdueDebtAsync(garage.Id, CancellationToken.None);
        Assert.True(beforeOverdue.Succeeded, beforeOverdue.ErrorMessage);
        Assert.Empty(beforeOverdue.Value!.Rows);

        var membershipOverdue = await FinanceServiceTestFactory.Create(
            context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)))
            .GetGarageOverdueDebtAsync(garage.Id, CancellationToken.None);
        Assert.True(membershipOverdue.Succeeded, membershipOverdue.ErrorMessage);
        var membershipRow = Assert.Single(membershipOverdue.Value!.Rows);
        Assert.Equal(annualServices["membership"].IncomeTypeId, membershipRow.IncomeTypeId);
        Assert.Equal(membershipOutstanding, membershipRow.OutstandingAmount);
        Assert.Equal(membershipOutstanding, membershipOverdue.Value.Total);

        var nextYearOverdue = await FinanceServiceTestFactory.Create(
            context,
            new FixedTimeProvider(new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero)))
            .GetGarageOverdueDebtAsync(garage.Id, CancellationToken.None);
        Assert.True(nextYearOverdue.Succeeded, nextYearOverdue.ErrorMessage);
        Assert.Equal(2, nextYearOverdue.Value!.Rows.Count);
        Assert.Contains(nextYearOverdue.Value.Rows, row => row.IncomeTypeId == annualServices["membership"].IncomeTypeId && row.OutstandingAmount == membershipOutstanding);
        Assert.Contains(nextYearOverdue.Value.Rows, row => row.IncomeTypeId == annualServices["outdoor_lighting"].IncomeTypeId && row.OutstandingAmount == garageAccruals["outdoor_lighting"].Amount);
        Assert.DoesNotContain(nextYearOverdue.Value.Rows, row => row.IncomeTypeId == annualServices["target"].IncomeTypeId);
    }

    [PostgreSqlFact]
    public async Task AnnualRegularGeneration_SerializesConcurrentMonthsAndKeepsGarageHistoryAfterOwnerChange()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        Guid membershipId;
        Guid tariffId;
        await using (var setupContext = database.CreateContext())
        {
            var membership = await setupContext.IncomeTypes.SingleAsync(item => item.Code == "membership" && !item.IsArchived);
            var originalOwner = new Owner { LastName = "Первый", FirstName = "Владелец" };
            var garage = new Garage { Number = "PG-ANNUAL-IDEMPOTENT", PeopleCount = 1, FloorCount = 1, Owner = originalOwner };
            var tariff = new Tariff
            {
                Name = "PG годовой членский тариф",
                CalculationBase = TariffCalculationBases.Fixed,
                Rate = 700m,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            };
            setupContext.AddRange(garage, tariff);
            await setupContext.SaveChangesAsync();
            garageId = garage.Id;
            membershipId = membership.Id;
            tariffId = tariff.Id;
        }

        await using var januaryContext = database.CreateContext();
        await using var julyContext = database.CreateContext();
        var januaryTask = FinanceServiceTestFactory.Create(januaryContext).GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(membershipId, tariffId, new DateOnly(2026, 1, 1), "Январский запуск"),
            null,
            CancellationToken.None);
        var julyTask = FinanceServiceTestFactory.Create(julyContext).GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(membershipId, tariffId, new DateOnly(2026, 7, 1), "Повторный запуск"),
            null,
            CancellationToken.None);
        var generationResults = await Task.WhenAll(januaryTask, julyTask);
        Assert.Single(generationResults, result => result.Succeeded);
        var duplicate = Assert.Single(generationResults, result => !result.Succeeded);
        Assert.Equal("regular_accruals_empty", duplicate.ErrorCode);

        await using var verificationContext = database.CreateContext();
        var annualAccrual = Assert.Single(await verificationContext.Accruals
            .Where(item => item.GarageId == garageId && item.AccountingYear == 2026 && item.Source == AccrualSources.Regular)
            .ToListAsync());
        var originalAccrualId = annualAccrual.Id;
        var replacementOwner = new Owner { LastName = "Новый", FirstName = "Владелец" };
        verificationContext.Owners.Add(replacementOwner);
        await verificationContext.SaveChangesAsync();
        var currentGarage = await verificationContext.Garages.AsNoTracking().SingleAsync(item => item.Id == garageId);
        var ownerChange = await DictionaryServiceTestFactory.Create(verificationContext).UpdateGarageAsync(
            garageId,
            new UpsertGarageRequest(
                currentGarage.Number,
                currentGarage.PeopleCount,
                currentGarage.FloorCount,
                replacementOwner.Id,
                currentGarage.StartingBalance,
                currentGarage.InitialWaterMeterValue,
                currentGarage.InitialElectricityMeterValue,
                currentGarage.Comment),
            null,
            CancellationToken.None);
        Assert.True(ownerChange.Succeeded, ownerChange.ErrorMessage);

        var financeService = FinanceServiceTestFactory.Create(verificationContext);
        var worksheet = await financeService.GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal("Новый Владелец", worksheet.Value!.OwnerName);
        Assert.Contains(worksheet.Value.Rows, row => row.AnnualAccrualId == originalAccrualId);
        Assert.Equal(1, await verificationContext.Accruals.CountAsync(item =>
            item.GarageId == garageId && item.AccountingYear == 2026 && item.Source == AccrualSources.Regular));
        Assert.Contains(verificationContext.AuditEvents, item => item.Action == "dictionary.garage_updated");
    }

    [PostgreSqlFact]
    public async Task AnnualAccruals_PersistAccountingYearAndDatabaseRejectsInvalidYear()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "PG-ANNUAL-YEAR", PeopleCount = 1, FloorCount = 1 };
        var membership = await context.IncomeTypes.SingleAsync(item => item.Code == "membership" && !item.IsArchived);
        var water = await context.IncomeTypes.SingleAsync(item => item.Code == "water" && !item.IsArchived);
        context.Garages.Add(garage);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context);

        var annual = await service.CreateAccrualAsync(
            new CreateAccrualRequest(garage.Id, membership.Id, new DateOnly(2027, 6, 1), 700m, "manual", "Годовой взнос"),
            null,
            CancellationToken.None);
        var monthly = await service.CreateAccrualAsync(
            new CreateAccrualRequest(garage.Id, water.Id, new DateOnly(2027, 7, 1), 100m, "manual", "Вода"),
            null,
            CancellationToken.None);

        Assert.True(annual.Succeeded, annual.ErrorMessage);
        Assert.Equal(2027, annual.Value!.AccountingYear);
        Assert.True(monthly.Succeeded, monthly.ErrorMessage);
        Assert.Null(monthly.Value!.AccountingYear);
        Assert.Equal(2027, await context.Accruals
            .Where(item => item.Id == annual.Value.Id)
            .Select(item => item.AccountingYear)
            .SingleAsync());

        context.Accruals.Add(new Accrual
        {
            GarageId = garage.Id,
            IncomeTypeId = membership.Id,
            AccountingMonth = new DateOnly(2028, 6, 1),
            AccountingYear = 1800,
            DueDate = new DateOnly(2028, 6, 30),
            OverdueFromDate = new DateOnly(2028, 7, 31),
            Amount = 700m,
            Source = "invalid-year-test"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
