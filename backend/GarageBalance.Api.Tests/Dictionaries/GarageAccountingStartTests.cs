using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class GarageAccountingStartTests
{
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly September = new(2026, 9, 1);

    [Theory]
    [InlineData("creation", "2026-08-01")]
    [InlineData("registration", "2026-08-01")]
    [InlineData("explicit", "2026-07-01")]
    [InlineData("unknown", null)]
    public async Task GarageList_ResolvesStartWithoutExposingInternalTimestamp(string source, string? expected)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = new Garage
        {
            Number = "SYNTHETIC-LIST",
            CreatedAtUtc = source == "unknown" ? default : new DateTimeOffset(2026, 8, 31, 22, 30, 0, TimeSpan.Zero),
            RegisteredOn = source is "registration" or "explicit" ? new(2026, 8, 19) : null,
            AccountingStartMonth = source == "explicit" ? new(2026, 7, 1) : null
        };
        database.Context.Garages.Add(garage);
        await database.Context.SaveChangesAsync();
        var rows = await DictionaryServiceTestFactory.Create(database.Context, October).GetGaragesAsync(null, default);
        var result = Assert.Single(rows);
        Assert.Equal(expected is null ? null : DateOnly.ParseExact(expected, "yyyy-MM-dd"), result.AccountingStartMonth);
    }

    [Theory]
    [InlineData(2026, 9, 1)]
    [InlineData(2026, 12, 1)]
    [InlineData(2027, 1, 1)]
    public async Task Create_UsesSelectedMonthAndFullAnnualCharge(int year, int month, int day)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var start = new DateOnly(year, month, day);
        await AssertCreationAsync(database.Context, start);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_CreateBackdatedAndDecemberAnnualCharges()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertCreationAsync(context, new(2026, 12, 1));
    }

    private static async Task AssertCreationAsync(GarageBalanceDbContext context, DateOnly start)
    {
        AddSettings(context);
        await context.SaveChangesAsync();
        var today = start < October ? October.AddDays(12) : start.AddDays(12);
        var service = DictionaryServiceTestFactory.Create(context, today);
        var result = await service.CreateGarageAsync(Request(start), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(start, result.Value!.AccountingStartMonth);
        context.ChangeTracker.Clear();
        var garage = await context.Garages.SingleAsync();
        Assert.Equal(today, garage.RegisteredOn);
        Assert.Equal(start.AddMonths(-1), garage.InitialMeterReadingMonth);
        Assert.Equal(start, (await context.GaragePeopleCountPeriods.SingleAsync()).EffectiveFrom);
        var rows = await context.Accruals.Where(row => !row.IsCanceled).ToArrayAsync();
        Assert.DoesNotContain(rows, row => row.AccountingMonth < start);
        var annual = Assert.Single(rows, row => row.AccountingYear.HasValue);
        Assert.Equal(1200m, annual.Amount);
        Assert.Equal(start, annual.AccountingMonth);
        Assert.Equal(start.Year, annual.AccountingYear);
        Assert.Equal(start < October ? 2 : 1, rows.Count(row => !row.AccountingYear.HasValue));
        var repeated = await service.UpdateGarageAsync(garage.Id,
            Request(start) with { Version = garage.Version }, null, CancellationToken.None);
        Assert.True(repeated.Succeeded, repeated.ErrorMessage);
        Assert.Equal(rows.Length, await context.Accruals.CountAsync(row => !row.IsCanceled));
    }

    [Fact]
    public async Task Create_FutureMonthDoesNotGenerateEarlyAccruals()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        AddSettings(database.Context);
        await database.Context.SaveChangesAsync();
        var result = await DictionaryServiceTestFactory.Create(database.Context, October).CreateGarageAsync(
            Request(new(2026, 12, 1)), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Empty(await database.Context.Accruals.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task Create_ExplicitMonthPreservesUnknownAndZeroBaselines(int? initial)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var result = await DictionaryServiceTestFactory.Create(database.Context, October).CreateGarageAsync(
            Request(September) with { InitialWaterMeterValue = initial, InitialElectricityMeterValue = initial }, null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var garage = await database.Context.Garages.SingleAsync();
        Assert.Equal(initial.HasValue ? (decimal?)initial.Value : null, garage.InitialWaterMeterValue);
        Assert.Equal(initial.HasValue ? (decimal?)initial.Value : null, garage.InitialElectricityMeterValue);
        Assert.Equal(September.AddMonths(-1), garage.InitialMeterReadingMonth);
        Assert.Equal(September, result.Value!.AccountingStartMonth);
    }

    [Fact]
    public async Task Create_OmittedMonthKeepsRegistrationFallbackWithoutRetimingData()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var result = await DictionaryServiceTestFactory.Create(database.Context, October.AddDays(12)).CreateGarageAsync(
            Request(null), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var garage = await database.Context.Garages.SingleAsync();
        Assert.Null(garage.AccountingStartMonth);
        Assert.Equal(October.AddDays(12), garage.RegisteredOn);
        Assert.Equal(September, garage.InitialMeterReadingMonth);
        Assert.Equal(October, result.Value!.AccountingStartMonth);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_MonthConstraintRejectsDayAndPre1900Dates()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        foreach (var invalid in new[] { new DateOnly(2026, 9, 2), new DateOnly(1899, 12, 1) })
        {
            context.Garages.Add(new Garage { Number = "invalid-month", AccountingStartMonth = invalid });
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(exception.InnerException).SqlState);
            context.ChangeTracker.Clear();
        }
        Assert.Empty(await context.Garages.ToListAsync());
    }

    [Fact]
    public async Task ForwardChange_RecalculatesUnpaidMonthsAndReusesAnnualObligation()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertForwardUnpaidAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ForwardChangeRecalculatesUnpaidMonthsAtomically()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertForwardUnpaidAsync(context);
    }

    private static async Task AssertForwardUnpaidAsync(GarageBalanceDbContext context)
    {
        AddSettings(context);
        await context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(context, October);
        var created = await service.CreateGarageAsync(Request(October), null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var garage = await context.Garages.SingleAsync();
        var annualId = (await context.Accruals.SingleAsync(row => row.AccountingYear.HasValue)).Id;
        var changed = await DictionaryServiceTestFactory.Create(context, October.AddMonths(1)).UpdateGarageAsync(garage.Id,
            Request(October.AddMonths(1)) with { Version = garage.Version, AccountingStartReason = "Исправление неоплаченной истории" }, null, CancellationToken.None);
        Assert.True(changed.Succeeded, changed.ErrorMessage);
        context.ChangeTracker.Clear();
        var active = await context.Accruals.Where(row => !row.IsCanceled).ToArrayAsync();
        Assert.Equal(2, active.Length);
        Assert.All(active, row => Assert.Equal(October.AddMonths(1), row.AccountingMonth));
        Assert.Equal(annualId, Assert.Single(active, row => row.AccountingYear.HasValue).Id);
        Assert.Equal(1200m, Assert.Single(active, row => row.AccountingYear.HasValue).Amount);
        Assert.Equal(October.AddMonths(1), (await context.Garages.SingleAsync()).AccountingStartMonth);
    }

    [Fact]
    public async Task Begin_RejectsOversizedCorrectionAndInvalidMonthBeforeMutation()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = await SeedGarageAsync(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context, businessDate: October);
        var invalid = await service.BeginAsync(garage, September.AddDays(1), garage.Version, false, null, CancellationToken.None);
        Assert.Equal("garage_accounting_start_invalid", invalid.ErrorCode);
        var oversized = await service.BeginAsync(garage, new(1900, 1, 1), garage.Version, false, null, CancellationToken.None);
        Assert.Equal("garage_accounting_start_period_too_large", oversized.ErrorCode);
        Assert.Null((await database.Context.Garages.SingleAsync()).AccountingStartMonth);
    }

    [Fact]
    public async Task Update_BackdatingCreatesMissingMonthsAndAuditsReason()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = await SeedGarageAsync(database.Context);
        AddSettings(database.Context);
        await database.Context.SaveChangesAsync();
        var result = await DictionaryServiceTestFactory.Create(database.Context, October.AddDays(12))
            .UpdateGarageAsync(garage.Id, Request(September) with { Version = garage.Version, AccountingStartReason = "Исправлена дата начала учёта" }, null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(September, result.Value!.AccountingStartMonth);
        Assert.Equal(October.AddDays(12), garage.RegisteredOn);
        Assert.Contains(await database.Context.Accruals.ToListAsync(), row => row.AccountingMonth == September && row.Amount == 100m);
        var audit = Assert.Single(await database.Context.AuditEvents.Where(row => row.Action == "dictionary.garage_updated").ToListAsync());
        using var metadata = System.Text.Json.JsonDocument.Parse(audit.MetadataJson!);
        Assert.Contains("Учитывать с: 09.2026", metadata.RootElement.GetProperty("newValue").GetString());
        Assert.Contains("Учитывать с: 10.2026", metadata.RootElement.GetProperty("oldValue").GetString());
        Assert.Equal("Исправлена дата начала учёта", metadata.RootElement.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData(2026, 9, 2, "Причина", "garage_accounting_start_invalid")]
    [InlineData(1899, 12, 1, "Причина", "garage_accounting_start_invalid")]
    [InlineData(2026, 9, 1, " ", "garage_accounting_start_reason_required")]
    public async Task Update_RejectsInvalidMonthOrMissingReasonWithoutMutation(int year, int month, int day, string reason, string code)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = await SeedGarageAsync(database.Context);
        var result = await DictionaryServiceTestFactory.Create(database.Context, October)
            .UpdateGarageAsync(garage.Id, Request(new(year, month, day)) with { Version = garage.Version, AccountingStartReason = reason }, null, CancellationToken.None);
        Assert.Equal(code, result.ErrorCode);
        Assert.Null((await database.Context.Garages.AsNoTracking().SingleAsync()).AccountingStartMonth);
    }

    [Fact]
    public async Task Update_RejectsLongReasonAndPreservesExplicitMonthForOldClient()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = await SeedGarageAsync(database.Context);
        garage.AccountingStartMonth = September;
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context, October);
        var invalid = await service.UpdateGarageAsync(garage.Id, Request(September) with { AccountingStartReason = new('a', 1001) }, null, CancellationToken.None);
        Assert.Equal("garage_accounting_start_reason_invalid", invalid.ErrorCode);
        var oldClient = await service.UpdateGarageAsync(garage.Id, Request(null) with { Number = "test-renamed", Version = garage.Version }, null, CancellationToken.None);
        Assert.True(oldClient.Succeeded, oldClient.ErrorMessage);
        Assert.Equal(September, oldClient.Value!.AccountingStartMonth);
    }

    [Fact]
    public async Task ChangeScope_RollsBackUnsavedAndSavedChangesOnAbandonment()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = await SeedGarageAsync(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context, businessDate: October);
        var change = await service.BeginAsync(garage, September, garage.Version, false, null, CancellationToken.None);
        Assert.True(change.Succeeded, change.ErrorMessage);
        await using (change.Value!)
        {
            garage.Number = "must-rollback";
            await database.Context.SaveChangesAsync();
        }
        var saved = await database.Context.Garages.SingleAsync();
        Assert.Equal("test-garage", saved.Number);
        Assert.Null(saved.AccountingStartMonth);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    public async Task ForwardChange_RejectsPartlyOrFullyPaidHistory(int paidAmount)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = await SeedGarageAsync(context);
        var incomeType = new IncomeType { Name = "Synthetic", Code = "synthetic" };
        var accrual = new Accrual { Garage = garage, IncomeType = incomeType, Amount = 100, AccountingMonth = October, Source = AccrualSources.Regular };
        var payment = new FinancialOperation { Garage = garage, IncomeType = incomeType, OperationKind = "income", Amount = paidAmount, AccountingMonth = October, OperationDate = October.AddDays(10) };
        context.AddRange(accrual, payment, new AccrualPaymentAllocation { Accrual = accrual, FinancialOperation = payment, Amount = paidAmount });
        await context.SaveChangesAsync();
        var result = await DictionaryServiceTestFactory.Create(context, October.AddMonths(1)).UpdateGarageAsync(garage.Id,
            Request(October.AddMonths(1)) with { Version = garage.Version, AccountingStartReason = "Проверка переноса" }, null, CancellationToken.None);
        Assert.Equal("garage_accounting_start_paid_history", result.ErrorCode);
        Assert.Null((await context.Garages.SingleAsync()).AccountingStartMonth);
        Assert.Equal(100m, (await context.Accruals.SingleAsync()).Amount);
    }

    [Theory]
    [InlineData(MeterKinds.Water)]
    [InlineData("service_11111111111141118111111111111111")]
    public async Task ForwardChange_RejectsExistingReadings(string kind)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = await SeedGarageAsync(database.Context);
        database.Context.MeterReadings.Add(new MeterReading { Garage = garage, MeterKind = kind, AccountingMonth = October, ReadingDate = October.AddDays(20), PreviousValue = 0, CurrentValue = 10, Consumption = 10 });
        await database.Context.SaveChangesAsync();
        var result = await DictionaryServiceTestFactory.Create(database.Context, October).UpdateGarageAsync(garage.Id,
            Request(October.AddMonths(1)) with { Version = garage.Version, AccountingStartReason = "Проверка" }, null, CancellationToken.None);
        Assert.Equal("garage_accounting_start_reading_history", result.ErrorCode);
        Assert.Null((await database.Context.Garages.SingleAsync()).AccountingStartMonth);
    }

    [Fact]
    public async Task Begin_RejectsStaleVersionAfterReloadAndCanceledRequest()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = await SeedGarageAsync(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context, businessDate: October);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => service.BeginAsync(garage, September, Guid.NewGuid(), false, null, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        garage = await database.Context.Garages.SingleAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BeginAsync(garage, September, garage.Version, false, null, cancellation.Token));
        Assert.Null((await database.Context.Garages.SingleAsync()).AccountingStartMonth);
    }

    private static UpsertGarageRequest Request(DateOnly? month) => new("test-garage", 1, 1, null, 0, null, null, null, AccountingStartMonth: month);

    [PostgreSqlFact]
    public async Task PostgreSql_ConcurrentWaitCancellationReleasesTransactionAndStaleTrackedGarageIsReloaded()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var first = database.CreateContext();
        var garage = await SeedGarageAsync(first);
        await using var second = database.CreateContext();
        var stale = await second.Garages.SingleAsync(row => row.Id == garage.Id);
        var oldVersion = stale.Version;
        var firstService = FinanceServiceTestFactory.Create(first, businessDate: October);
        var secondService = FinanceServiceTestFactory.Create(second, businessDate: October);
        var change = await firstService.BeginAsync(garage, September, garage.Version, false, null, CancellationToken.None);
        Assert.True(change.Succeeded, change.ErrorMessage);
        await using (change.Value!)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondService.BeginAsync(stale, September, oldVersion, false, null, cancellation.Token));
            await first.SaveChangesAsync();
            var completed = await change.Value!.CompleteAsync(CancellationToken.None);
            Assert.True(completed.Succeeded, completed.ErrorMessage);
        }
        stale = await second.Garages.SingleAsync(row => row.Id == garage.Id);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => secondService.BeginAsync(stale, September.AddMonths(-1), oldVersion, false, null, CancellationToken.None));
        Assert.Equal(September, (await second.Garages.AsNoTracking().SingleAsync(row => row.Id == garage.Id)).AccountingStartMonth);
    }

    private static async Task<Garage> SeedGarageAsync(GarageBalanceDbContext context)
    {
        var garage = new Garage { Number = "test-garage", PeopleCount = 1, FloorCount = 1, RegisteredOn = October.AddDays(12), InitialMeterReadingMonth = September };
        context.Garages.Add(garage);
        await context.SaveChangesAsync();
        return garage;
    }

    private static void AddSettings(GarageBalanceDbContext context)
    {
        foreach (var setting in context.ChargeServiceSettings) setting.IsArchived = true;
        foreach (var annual in new[] { false, true })
        {
            context.ChargeServiceSettings.Add(new ChargeServiceSetting
            {
                Name = annual ? "Synthetic annual" : "Synthetic monthly",
                IsRegular = true,
                PeriodicityMonths = annual ? 12 : 1,
                AccrualStartMonth = 1,
                IncomeType = new IncomeType { Name = annual ? "Synthetic annual" : "Synthetic monthly", Code = annual ? "synthetic_annual" : "synthetic_monthly" },
                Tariff = new Tariff { Name = annual ? "Synthetic annual tariff" : "Synthetic monthly tariff", CalculationBase = TariffCalculationBases.Fixed, Rate = annual ? 1200 : 100, EffectiveFrom = new(2026, 1, 1) }
            });
        }
    }
}
