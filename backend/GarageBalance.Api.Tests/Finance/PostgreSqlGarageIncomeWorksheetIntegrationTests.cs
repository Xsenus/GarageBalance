using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlGarageIncomeWorksheetIntegrationTests
{
    [PostgreSqlFact]
    public async Task GarageLedger_ForFourteenMonths_KeepsReadingsAnnualFeesFifoCreditAndOverdueConsistent()
    {
        var firstMonth = new DateOnly(2026, 1, 1);
        var lastMonth = new DateOnly(2027, 2, 1);
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var waterType = await context.IncomeTypes.SingleAsync(type => type.Code == MeterKinds.Water && !type.IsArchived);
        var membershipType = await context.IncomeTypes.SingleAsync(type => type.Code == "membership" && !type.IsArchived);
        var garage = new Garage
        {
            Number = "PG-FOURTEEN-MONTH-LEDGER",
            PeopleCount = 1,
            FloorCount = 1,
            InitialWaterMeterValue = 100m
        };
        context.Garages.Add(garage);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            context,
            new FixedTimeProvider(new DateTimeOffset(2027, 3, 1, 12, 0, 0, TimeSpan.Zero)));
        var waterAccrualIds = new List<Guid>();

        for (var monthOffset = 0; monthOffset < 14; monthOffset++)
        {
            var month = firstMonth.AddMonths(monthOffset);
            var reading = await service.CreateMeterReadingAsync(
                new CreateMeterReadingRequest(
                    garage.Id,
                    MeterKinds.Water,
                    month,
                    month.AddDays(19),
                    101m + monthOffset,
                    $"Показание за {month:MM.yyyy}"),
                null,
                CancellationToken.None);
            Assert.True(reading.Succeeded, reading.ErrorMessage);
            Assert.Equal(100m + monthOffset, reading.Value!.PreviousValue);
            Assert.Equal(1m, reading.Value.Consumption);

            var accrual = await service.CreateAccrualAsync(
                new CreateAccrualRequest(
                    garage.Id,
                    waterType.Id,
                    month,
                    100m,
                    AccrualSources.Regular,
                    $"Вода за {month:MM.yyyy}"),
                null,
                CancellationToken.None);
            Assert.True(accrual.Succeeded, accrual.ErrorMessage);
            waterAccrualIds.Add(accrual.Value!.Id);
        }

        var membership2026 = await service.CreateAccrualAsync(
            new CreateAccrualRequest(
                garage.Id,
                membershipType.Id,
                firstMonth,
                700m,
                AccrualSources.Regular,
                "Членский взнос за 2026 год"),
            null,
            CancellationToken.None);
        var membership2027 = await service.CreateAccrualAsync(
            new CreateAccrualRequest(
                garage.Id,
                membershipType.Id,
                new DateOnly(2027, 1, 1),
                800m,
                AccrualSources.Regular,
                "Членский взнос за 2027 год"),
            null,
            CancellationToken.None);
        Assert.True(membership2026.Succeeded, membership2026.ErrorMessage);
        Assert.True(membership2027.Succeeded, membership2027.ErrorMessage);
        Assert.Equal(2026, membership2026.Value!.AccountingYear);
        Assert.Equal(2027, membership2027.Value!.AccountingYear);

        var partialWater = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                garage.Id,
                waterType.Id,
                new DateOnly(2026, 3, 10),
                new DateOnly(2026, 3, 1),
                250m,
                "PG-14-WATER-PARTIAL",
                "Частичная оплата воды"),
            null,
            CancellationToken.None);
        Assert.True(partialWater.Succeeded, partialWater.ErrorMessage);
        var partialWaterAllocations = await context.AccrualPaymentAllocations
            .Where(allocation => allocation.IsActive && waterAccrualIds.Contains(allocation.AccrualId))
            .OrderBy(allocation => allocation.Accrual.AccountingMonth)
            .Select(allocation => allocation.Amount)
            .ToArrayAsync();
        Assert.Equal([100m, 100m, 50m], partialWaterAllocations);

        var partialMembership = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                garage.Id,
                membershipType.Id,
                new DateOnly(2026, 7, 30),
                new DateOnly(2026, 7, 1),
                300m,
                "PG-14-MEMBERSHIP-PARTIAL",
                "Частичная оплата членского взноса"),
            null,
            CancellationToken.None);
        var waterWithCredit = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                garage.Id,
                waterType.Id,
                new DateOnly(2027, 2, 10),
                lastMonth,
                1200m,
                "PG-14-WATER-CREDIT",
                "Погашение воды с переплатой"),
            null,
            CancellationToken.None);
        Assert.True(partialMembership.Succeeded, partialMembership.ErrorMessage);
        Assert.True(waterWithCredit.Succeeded, waterWithCredit.ErrorMessage);

        var waterAllocations = await context.AccrualPaymentAllocations
            .Where(allocation => allocation.IsActive && waterAccrualIds.Contains(allocation.AccrualId))
            .GroupBy(allocation => allocation.AccrualId)
            .Select(group => group.Sum(allocation => allocation.Amount))
            .ToArrayAsync();
        Assert.Equal(14, waterAllocations.Length);
        Assert.All(waterAllocations, amount => Assert.Equal(100m, amount));
        var membershipAllocation = await context.AccrualPaymentAllocations
            .Where(allocation => allocation.IsActive && allocation.AccrualId == membership2026.Value.Id)
            .SumAsync(allocation => allocation.Amount);
        Assert.Equal(300m, membershipAllocation);
        var waterIncomeTotal = await context.FinancialOperations
            .Where(operation => !operation.IsCanceled && operation.IncomeTypeId == waterType.Id)
            .SumAsync(operation => operation.Amount);
        Assert.Equal(50m, waterIncomeTotal - waterAllocations.Sum());

        var worksheet = await service.GetGarageIncomeWorksheetAsync(
            garage.Id,
            new GarageIncomeWorksheetRequest(firstMonth, lastMonth),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal(14, worksheet.Value!.Rows.Count(row => row.IncomeTypeId == waterType.Id));
        Assert.Equal(2900m, worksheet.Value.AccrualTotal);
        Assert.Equal(1750m, worksheet.Value.IncomeTotal);
        Assert.Equal(1150m, worksheet.Value.ClosingDebt);
        var lastWaterRow = Assert.Single(worksheet.Value.Rows, row =>
            row.IncomeTypeId == waterType.Id && row.AccountingMonth == lastMonth);
        Assert.Equal(114m, lastWaterRow.MeterValue);
        Assert.Equal(1m, lastWaterRow.MeterConsumption);
        Assert.Equal(0m, lastWaterRow.Debt);
        Assert.Equal(400m, Assert.Single(worksheet.Value.Rows, row =>
            row.AnnualAccrualId == membership2026.Value.Id &&
            row.AccountingMonth == new DateOnly(2026, 12, 1)).Debt);
        Assert.Equal(800m, Assert.Single(worksheet.Value.Rows, row =>
            row.AnnualAccrualId == membership2027.Value.Id &&
            row.AccountingMonth == lastMonth).Debt);

        var history = await service.GetGarageBalanceHistoryAsync(
            garage.Id,
            new GarageBalanceHistoryRequest(firstMonth, lastMonth),
            CancellationToken.None);
        Assert.True(history.Succeeded, history.ErrorMessage);
        Assert.Equal(14, history.Value!.Rows.Count);
        Assert.Equal(2900m, history.Value.AccrualTotal);
        Assert.Equal(1750m, history.Value.IncomeTotal);
        Assert.Equal(1150m, history.Value.Debt);

        var overdue = await FinanceServiceTestFactory.Create(
                context,
                new FixedTimeProvider(new DateTimeOffset(2027, 3, 1, 12, 0, 0, TimeSpan.Zero)))
            .GetGarageOverdueDebtAsync(garage.Id, CancellationToken.None);
        Assert.True(overdue.Succeeded, overdue.ErrorMessage);
        var overdueMembership = Assert.Single(overdue.Value!.Rows);
        Assert.Equal(membershipType.Id, overdueMembership.IncomeTypeId);
        Assert.Equal(firstMonth, overdueMembership.AccountingMonth);
        Assert.Equal(700m, overdueMembership.OriginalAmount);
        Assert.Equal(350m, overdueMembership.PaidAmount);
        Assert.Equal(350m, overdueMembership.OutstandingAmount);
        Assert.Equal(350m, overdue.Value.Total);

        Assert.Equal(14, await context.AuditEvents.CountAsync(audit => audit.Action == "finance.meter_reading_created"));
        Assert.Equal(16, await context.AuditEvents.CountAsync(audit => audit.Action == "finance.accrual_created"));
        Assert.Equal(3, await context.AuditEvents.CountAsync(audit => audit.Action == "finance.income_created"));
    }

    [PostgreSqlFact]
    public async Task AnnualObligations_ForEverySystemType_HandlePartialFullOverpaymentAndNewYear()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var annualTypes = await context.IncomeTypes
            .Where(type => type.Code == "membership" || type.Code == "target" || type.Code == "outdoor_lighting")
            .OrderBy(type => type.Code)
            .ToListAsync();
        Assert.Equal(3, annualTypes.Count);

        foreach (var incomeType in annualTypes)
        {
            var code = incomeType.Code!;
            var garage = new Garage
            {
                Number = $"PG-ANNUAL-{code.ToUpperInvariant()}",
                PeopleCount = 1,
                FloorCount = 1
            };
            context.Garages.Add(garage);
            await context.SaveChangesAsync();
            var service = FinanceServiceTestFactory.Create(context);

            var accrual2026 = await service.CreateAccrualAsync(
                new CreateAccrualRequest(
                    garage.Id,
                    incomeType.Id,
                    new DateOnly(2026, 1, 1),
                    700m,
                    AccrualSources.Regular,
                    "Годовое обязательство 2026"),
                null,
                CancellationToken.None);
            Assert.True(accrual2026.Succeeded, accrual2026.ErrorMessage);
            Assert.Equal(2026, accrual2026.Value!.AccountingYear);

            var partialPayment = await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    garage.Id,
                    incomeType.Id,
                    new DateOnly(2026, 3, 10),
                    new DateOnly(2026, 3, 1),
                    300m,
                    $"ANNUAL-{code}-PARTIAL",
                    null),
                null,
                CancellationToken.None);
            Assert.True(partialPayment.Succeeded, partialPayment.ErrorMessage);

            var partialWorksheet = await service.GetGarageIncomeWorksheetAsync(
                garage.Id,
                new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)),
                CancellationToken.None);
            Assert.True(partialWorksheet.Succeeded, partialWorksheet.ErrorMessage);
            var marchPartial = Assert.Single(partialWorksheet.Value!.Rows, row =>
                row.AnnualAccrualId == accrual2026.Value.Id &&
                row.AccountingMonth == new DateOnly(2026, 3, 1));
            Assert.Equal(300m, marchPartial.IncomeAmount);
            Assert.Equal(400m, marchPartial.Debt);
            Assert.Equal(400m, Assert.Single(partialWorksheet.Value.Rows, row =>
                row.AnnualAccrualId == accrual2026.Value.Id &&
                row.AccountingMonth == new DateOnly(2026, 6, 1)).PayableAmount);

            var overpayment = await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    garage.Id,
                    incomeType.Id,
                    new DateOnly(2026, 4, 10),
                    new DateOnly(2026, 4, 1),
                    500m,
                    $"ANNUAL-{code}-FULL",
                    null),
                null,
                CancellationToken.None);
            Assert.True(overpayment.Succeeded, overpayment.ErrorMessage);

            var paidWorksheet = await service.GetGarageIncomeWorksheetAsync(
                garage.Id,
                new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)),
                CancellationToken.None);
            Assert.True(paidWorksheet.Succeeded, paidWorksheet.ErrorMessage);
            var aprilPaid = Assert.Single(paidWorksheet.Value!.Rows, row =>
                row.AnnualAccrualId == accrual2026.Value.Id &&
                row.AccountingMonth == new DateOnly(2026, 4, 1));
            Assert.Equal(400m, aprilPaid.IncomeAmount);
            Assert.Equal(0m, aprilPaid.Debt);
            Assert.DoesNotContain(paidWorksheet.Value.Rows, row =>
                row.AnnualAccrualId == accrual2026.Value.Id &&
                row.AccountingMonth > new DateOnly(2026, 4, 1));
            Assert.Equal(800m, paidWorksheet.Value.IncomeTotal);
            Assert.Equal(0m, paidWorksheet.Value.ClosingDebt);

            var accrual2027 = await service.CreateAccrualAsync(
                new CreateAccrualRequest(
                    garage.Id,
                    incomeType.Id,
                    new DateOnly(2027, 1, 1),
                    900m,
                    AccrualSources.Regular,
                    "Годовое обязательство 2027"),
                null,
                CancellationToken.None);
            Assert.True(accrual2027.Succeeded, accrual2027.ErrorMessage);
            Assert.Equal(2027, accrual2027.Value!.AccountingYear);

            var newYearWorksheet = await service.GetGarageIncomeWorksheetAsync(
                garage.Id,
                new GarageIncomeWorksheetRequest(new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 1)),
                CancellationToken.None);
            Assert.True(newYearWorksheet.Succeeded, newYearWorksheet.ErrorMessage);
            var january2027 = Assert.Single(newYearWorksheet.Value!.Rows, row =>
                row.AnnualAccrualId == accrual2027.Value.Id &&
                row.AccountingMonth == new DateOnly(2027, 1, 1));
            Assert.Equal(900m, january2027.AccrualAmount);
            Assert.Equal(800m, january2027.PayableAmount);
            Assert.Equal(800m, january2027.Debt);
            Assert.DoesNotContain(newYearWorksheet.Value.Rows, row => row.AnnualAccrualId == accrual2026.Value.Id);

            var allocations = await context.AccrualPaymentAllocations
                .Where(allocation =>
                    allocation.IsActive &&
                    (allocation.AccrualId == accrual2026.Value.Id || allocation.AccrualId == accrual2027.Value.Id))
                .GroupBy(allocation => allocation.AccrualId)
                .Select(group => new { AccrualId = group.Key, Amount = group.Sum(allocation => allocation.Amount) })
                .ToDictionaryAsync(item => item.AccrualId, item => item.Amount);
            Assert.Equal(700m, allocations[accrual2026.Value.Id]);
            Assert.Equal(100m, allocations[accrual2027.Value.Id]);

            var finalPayment = await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    garage.Id,
                    incomeType.Id,
                    new DateOnly(2027, 2, 10),
                    new DateOnly(2027, 2, 1),
                    800m,
                    $"ANNUAL-{code}-2027-FULL",
                    null),
                null,
                CancellationToken.None);
            Assert.True(finalPayment.Succeeded, finalPayment.ErrorMessage);

            var newYearPaidWorksheet = await service.GetGarageIncomeWorksheetAsync(
                garage.Id,
                new GarageIncomeWorksheetRequest(new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 1)),
                CancellationToken.None);
            Assert.True(newYearPaidWorksheet.Succeeded, newYearPaidWorksheet.ErrorMessage);
            var february2027 = Assert.Single(newYearPaidWorksheet.Value!.Rows, row =>
                row.AnnualAccrualId == accrual2027.Value.Id &&
                row.AccountingMonth == new DateOnly(2027, 2, 1));
            Assert.Equal(800m, february2027.IncomeAmount);
            Assert.Equal(0m, february2027.Debt);
            Assert.DoesNotContain(newYearPaidWorksheet.Value.Rows, row =>
                row.AnnualAccrualId == accrual2027.Value.Id &&
                row.AccountingMonth > new DateOnly(2027, 2, 1));
        }
    }

    [PostgreSqlFact]
    public async Task Worksheet_ProjectsAnnualObligationUntilFullPaymentAndReopensItAfterCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var membershipType = await context.IncomeTypes.SingleAsync(type => type.Code == "membership");
        var garage = new Garage
        {
            Number = "PG-ANNUAL-OBLIGATION",
            PeopleCount = 1,
            FloorCount = 1
        };
        context.Garages.Add(garage);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context);

        var accrual = await service.CreateAccrualAsync(
            new CreateAccrualRequest(garage.Id, membershipType.Id, new DateOnly(2026, 1, 1), 700m, "regular", null),
            null,
            CancellationToken.None);
        Assert.True(accrual.Succeeded, accrual.ErrorMessage);
        var annualAccrualId = accrual.Value!.Id;
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(garage.Id, membershipType.Id, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 1), 300m, null, null),
            null,
            CancellationToken.None)).Succeeded);

        var partial = await service.GetGarageIncomeWorksheetAsync(
            garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(partial.Succeeded, partial.ErrorMessage);
        Assert.Equal(6, partial.Value!.Rows.Count(row => row.AnnualAccrualId == annualAccrualId));
        Assert.Equal(400m, Assert.Single(partial.Value.Rows, row =>
            row.AnnualAccrualId == annualAccrualId && row.AccountingMonth == new DateOnly(2026, 6, 1)).Debt);

        var fullPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(garage.Id, membershipType.Id, new DateOnly(2026, 4, 10), new DateOnly(2026, 4, 1), 400m, null, null),
            null,
            CancellationToken.None);
        Assert.True(fullPayment.Succeeded, fullPayment.ErrorMessage);
        var paid = await service.GetGarageIncomeWorksheetAsync(
            garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(paid.Succeeded, paid.ErrorMessage);
        Assert.DoesNotContain(paid.Value!.Rows, row =>
            row.AnnualAccrualId == annualAccrualId && row.AccountingMonth > new DateOnly(2026, 4, 1));

        var canceled = await service.CancelOperationAsync(
            fullPayment.Value!.Id,
            new CancelFinanceEntryRequest("Проверка возврата годового остатка"),
            null,
            CancellationToken.None);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        var reopened = await service.GetGarageIncomeWorksheetAsync(
            garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(reopened.Succeeded, reopened.ErrorMessage);
        Assert.Equal(400m, Assert.Single(reopened.Value!.Rows, row =>
            row.AnnualAccrualId == annualAccrualId && row.AccountingMonth == new DateOnly(2026, 6, 1)).Debt);
    }

    [PostgreSqlFact]
    public async Task Worksheet_ReturnsMeterIdentityAndVersionForInlineEditing()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var electricityType = await context.IncomeTypes.SingleAsync(type => type.Code == MeterKinds.Electricity);
        var garage = new Garage
        {
            Number = "PG-INLINE-METER",
            PeopleCount = 1,
            FloorCount = 1,
            InitialElectricityMeterValue = 100m
        };
        var reading = new MeterReading
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            AccountingMonth = new DateOnly(2026, 7, 1),
            ReadingDate = new DateOnly(2026, 7, 17),
            PreviousValue = 100m,
            CurrentValue = 118m,
            Consumption = 18m
        };
        context.AddRange(
            garage,
            reading,
            new Accrual
            {
                Garage = garage,
                IncomeType = electricityType,
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 134.46m,
                Source = "regular"
            });
        await context.SaveChangesAsync();

        var service = FinanceServiceTestFactory.Create(context);
        var result = await service.GetGarageIncomeWorksheetAsync(
            garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var row = Assert.Single(result.Value!.Rows, item => item.IncomeTypeId == electricityType.Id);
        Assert.Equal(MeterKinds.Electricity, row.MeterKind);
        Assert.Equal(reading.Id, row.MeterReadingId);
        Assert.Equal(reading.Version, row.MeterReadingVersion);
        Assert.Equal(reading.ReadingDate, row.MeterReadingDate);
        Assert.Equal(118m, row.MeterValue);
        Assert.Equal(18m, row.MeterConsumption);
        var missingWater = Assert.Single(result.Value.Rows, item => item.MeterKind == MeterKinds.Water);
        Assert.Null(missingWater.MeterValue);
        Assert.Equal(0m, missingWater.AccrualAmount);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
