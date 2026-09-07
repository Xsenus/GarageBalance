using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class GarageInitialMeterReadingTests
{
    [Theory]
    [InlineData("2027-01-15", "2026-12-01", 0, 100, 1)]
    [InlineData("2026-09-05", "2026-08-01", 12, 0, 1)]
    [InlineData("2027-01-15", "2026-12-01", 0, 100, 2)]
    public async Task CreationPersistsPreviousBusinessMonthWithoutConsumptionAndFirstReadingUsesBaseline(
        string businessDate, string baselineDate, int water, int electricity, int monthOffset)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var dictionaries = DictionaryServiceTestFactory.Create(context, DateOnly.Parse(businessDate));
        var request = new UpsertGarageRequest("INITIAL-1", 1, 1, null, 0, water, electricity, null);
        var created = await dictionaries.CreateGarageAsync(request, null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var garage = await context.Garages.SingleAsync();
        var baselineMonth = DateOnly.Parse(baselineDate);
        Assert.Equal(baselineMonth, garage.InitialMeterReadingMonth);
        Assert.Equal(DateOnly.Parse(businessDate), garage.RegisteredOn);
        Assert.Empty(await context.MeterReadings.ToListAsync());
        Assert.Empty(await context.Accruals.ToListAsync());
        Assert.False((await dictionaries.CreateGarageAsync(request, null, CancellationToken.None)).Succeeded);
        Assert.Single(await context.Garages.ToListAsync());

        var finance = FinanceServiceTestFactory.Create(context);
        foreach (var (kind, value) in new[] { (MeterKinds.Water, water), (MeterKinds.Electricity, electricity) })
        {
            var page = await finance.GetMeterReadingYearPageAsync(new MeterReadingYearRequest(baselineMonth.Year, kind), CancellationToken.None);
            Assert.True(page.Succeeded, page.ErrorMessage);
            Assert.Equal(baselineMonth, Assert.Single(page.Value!.Garages).InitialReadingMonth);
            Assert.Equal(value, Assert.Single(page.Value.Garages).InitialReadingValue);
            Assert.Empty(page.Value.Readings);
            foreach (var rejectedMonth in new[] { baselineMonth, baselineMonth.AddMonths(-1) })
            {
                var rejected = await finance.CreateMeterReadingAsync(new CreateMeterReadingRequest(garage.Id, kind, rejectedMonth, rejectedMonth.AddDays(20), value, null), null, CancellationToken.None);
                Assert.Equal("meter_reading_before_baseline", rejected.ErrorCode);
                var replacement = await finance.ReplaceMeterDeviceAsync(new ReplaceMeterDeviceRequest(garage.Id, kind, rejectedMonth, rejectedMonth.AddDays(20), "TEST-NEW", 0, 1, value, "Проверка исходного месяца"), null, CancellationToken.None);
                Assert.Equal("meter_reading_before_baseline", replacement.ErrorCode);
            }
            var month = baselineMonth.AddMonths(monthOffset);
            var readingRequest = new CreateMeterReadingRequest(garage.Id, kind, month, month.AddDays(20), value + 5, null);
            var reading = await finance.CreateMeterReadingAsync(readingRequest, null, CancellationToken.None);
            Assert.True(reading.Succeeded, reading.ErrorMessage);
            Assert.Equal(value, reading.Value!.PreviousValue);
            Assert.Equal(5, reading.Value.Consumption);
            Assert.Equal(kind == MeterKinds.Electricity && monthOffset > 1, reading.Value.HasGapWarning);
            Assert.Equal("meter_reading_duplicate", (await finance.CreateMeterReadingAsync(readingRequest, null, CancellationToken.None)).ErrorCode);
        }
        Assert.DoesNotContain(await context.Accruals.ToListAsync(), item => item.AccountingMonth <= baselineMonth);
        Assert.Equal(2, await context.MeterReadings.CountAsync());
        Assert.Single(await context.AuditEvents.Where(item => item.Action == "dictionary.garage_created").ToListAsync());
    }

    [Fact]
    public async Task CanceledCreationDoesNotPersistGarageOrBaseline()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var dictionaries = DictionaryServiceTestFactory.Create(database.Context, new DateOnly(2026, 9, 5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dictionaries.CreateGarageAsync(new UpsertGarageRequest("CANCELED-INITIAL", 1, 1, null, 0, 0, 100, null), null, new CancellationToken(true)));
        Assert.Empty(await database.Context.Garages.ToListAsync());
        Assert.Empty(await database.Context.MeterReadings.ToListAsync());
        Assert.Empty(await database.Context.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task EditingOpeningValuesKeepsOriginalMonthAndEmptyBaselinesRemainAbsent()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var created = await DictionaryServiceTestFactory.Create(context, new DateOnly(2026, 1, 20))
            .CreateGarageAsync(new UpsertGarageRequest("INITIAL-EMPTY", 1, 1, null, 0, null, null, null), null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var finance = FinanceServiceTestFactory.Create(context);
        var empty = await finance.GetMeterReadingYearPageAsync(new MeterReadingYearRequest(2025, MeterKinds.Water), CancellationToken.None);
        Assert.Null(Assert.Single(empty.Value!.Garages).InitialReadingValue);
        var updated = await DictionaryServiceTestFactory.Create(context, new DateOnly(2026, 3, 1))
            .UpdateGarageAsync(created.Value!.Id, new UpsertGarageRequest("INITIAL-EMPTY", 1, 1, null, 0, 0, 10, null), null, CancellationToken.None);
        Assert.True(updated.Succeeded, updated.ErrorMessage);
        context.ChangeTracker.Clear();
        Assert.Equal(new DateOnly(2025, 12, 1), (await context.Garages.SingleAsync()).InitialMeterReadingMonth);
        Assert.Equal(new DateOnly(2026, 1, 20), (await context.Garages.SingleAsync()).RegisteredOn);
        var page = await finance.GetMeterReadingYearPageAsync(new MeterReadingYearRequest(2025, MeterKinds.Electricity), CancellationToken.None);
        Assert.Equal(10, Assert.Single(page.Value!.Garages).InitialReadingValue);
        Assert.Empty(page.Value.Readings);
    }
}
