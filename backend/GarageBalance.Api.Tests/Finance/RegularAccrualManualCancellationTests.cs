using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class RegularAccrualManualCancellationTests
{
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly November = new(2026, 11, 1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualCancellationSurvivesReopeningBatchAndAnnualCardUntilExplicitRestore(bool annual)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyCancellationAsync(database.Context, annual);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ManualCancellationSurvivesAutomaticGenerationAndCanBeRestored()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyCancellationAsync(context, annual: false);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_AnnualCancellationBlocksOtherMonthsOfTheYear()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyCancellationAsync(context, annual: true);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ConcurrentCancellationAndWorksheetPreserveTheManualDecision()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        Guid accrualId;
        await using (var setup = database.CreateContext())
        {
            var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(setup, October);
            setting.PeriodicityMonths = 1;
            setting.PaymentDueMonth = null;
            await setup.SaveChangesAsync();
            garageId = garage.Id;
            Assert.True((await FinanceServiceTestFactory.Create(setup, businessDate: November)
                .CalculateGarageIncomeWorksheetAsync(garageId, new(October, October), null, default)).Succeeded);
            accrualId = await setup.Accruals.Where(item => item.GarageId == garageId).Select(item => item.Id).SingleAsync();
        }
        await using var cancellationContext = database.CreateContext();
        await using var worksheetContext = database.CreateContext();
        var cancellation = FinanceServiceTestFactory.Create(cancellationContext, businessDate: November)
            .CancelAccrualAsync(accrualId, new("Synthetic concurrent cancel"), null, default);
        var worksheet = FinanceServiceTestFactory.Create(worksheetContext, businessDate: November)
            .CalculateGarageIncomeWorksheetAsync(garageId, new(October, October), null, default);
        await Task.WhenAll(cancellation, worksheet).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True((await cancellation).Succeeded);
        Assert.True((await worksheet).Succeeded);
        await using var verification = database.CreateContext();
        Assert.True((await FinanceServiceTestFactory.Create(verification, businessDate: November)
            .CalculateGarageIncomeWorksheetAsync(garageId, new(October, October), null, default)).Succeeded);
        var remaining = await verification.Accruals.Where(item => item.GarageId == garageId).SingleAsync();
        Assert.Equal(accrualId, remaining.Id);
        Assert.True(remaining.IsCanceled);
    }

    private static async Task VerifyCancellationAsync(GarageBalanceDbContext context, bool annual)
    {
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(context, October);
        if (!annual)
        {
            setting.PeriodicityMonths = 1;
            setting.PaymentDueMonth = null;
            setting.Tariff!.Rate = 125;
            await context.SaveChangesAsync();
        }
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        Assert.True((await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(October, October), null, default)).Succeeded);
        var accrual = await context.Accruals.SingleAsync(item => item.GarageId == garage.Id);
        Assert.True((await service.CancelAccrualAsync(accrual.Id, new("Synthetic cancellation"), null, default)).Succeeded);

        // Re-enter with a new context state, as a new browser request does.
        context.ChangeTracker.Clear();
        service = FinanceServiceTestFactory.Create(context, businessDate: November);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var worksheet = await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(October, October), null, default);
            Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        }
        if (annual)
        {
            var annualCard = await service.CalculateGarageAnnualPaymentsAsync(garage.Id, 2026, null, default);
            Assert.True(annualCard.Succeeded, annualCard.ErrorMessage);
            Assert.True((await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(November, November), null, default)).Succeeded);
        }
        var generated = await service.GenerateRegularAccrualsAsync(
            new(setting.IncomeTypeId!.Value, setting.TariffId!.Value, annual ? new(2026, 1, 1) : October, null), null, default);
        Assert.False(generated.Succeeded);
        Assert.Equal("regular_accruals_empty", generated.ErrorCode);
        Assert.Contains("отменено вручную", generated.ErrorMessage);
        var canceled = await context.Accruals.SingleAsync(item => item.GarageId == garage.Id);
        Assert.Equal(accrual.Id, canceled.Id);
        Assert.True(canceled.IsCanceled);

        var restored = await service.RestoreAccrualAsync(accrual.Id, null, default);
        Assert.True(restored.Succeeded, restored.ErrorMessage);
        context.ChangeTracker.Clear();
        var worksheetAfterRestore = await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(October, October), null, default);
        Assert.True(worksheetAfterRestore.Succeeded, worksheetAfterRestore.ErrorMessage);
        var active = await context.Accruals.SingleAsync(item => item.GarageId == garage.Id);
        Assert.Equal(accrual.Id, active.Id);
        Assert.False(active.IsCanceled);
        Assert.Equal(annual ? 1200m : 125m, active.Amount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NovemberWorksheetCatchesUpAnnualOctoberOnlyAndHonorsYearHold(bool held)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(context, October);
        var monthly = new ChargeServiceSetting
        {
            Name = "Synthetic monthly trash",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            IncomeType = new IncomeType { Name = "Synthetic monthly trash", Code = "synthetic_monthly_trash" },
            Tariff = new Tariff { Name = "Synthetic monthly trash", CalculationBase = TariffCalculationBases.Fixed, Rate = 125, EffectiveFrom = new(2026, 1, 1) }
        };
        context.Add(monthly);
        if (held) context.Add(new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2026), BooleanValue = true });
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(November, November), null, default);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }
        var accruals = await context.Accruals.Where(item => item.GarageId == garage.Id).ToArrayAsync();
        Assert.Equal(held ? 1 : 2, accruals.Length);
        var monthlyAccrual = Assert.Single(accruals, item => item.IncomeTypeId == monthly.IncomeTypeId);
        Assert.Equal(November, monthlyAccrual.AccountingMonth);
        Assert.Equal(125m, monthlyAccrual.Amount);
        if (!held)
        {
            var annualAccrual = Assert.Single(accruals, item => item.IncomeTypeId == annual.IncomeTypeId);
            Assert.Equal(October, annualAccrual.AccountingMonth);
            Assert.Equal(1200m, annualAccrual.Amount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdatingMeterReadingDoesNotRecreateManuallyCanceledAccrual(bool annual)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(context, October);
        garage.InitialWaterMeterValue = 10;
        setting.PeriodicityMonths = annual ? 12 : 1;
        setting.PaymentDueMonth = annual ? 10 : null;
        setting.AccrualStartMonth = annual ? 10 : 1;
        setting.IsMetered = true;
        setting.MeterKind = MeterKinds.Water;
        setting.Tariff!.CalculationBase = TariffCalculationBases.MeterWater;
        setting.Tariff.Rate = 100;
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: October);
        var reading = await service.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Water, October, October.AddDays(19), 12, null), null, default);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        var accrual = await context.Accruals.SingleAsync();
        Assert.Equal(annual ? 2026 : (int?)null, accrual.AccountingYear);
        Assert.True((await service.CancelAccrualAsync(accrual.Id, new("Synthetic meter cancellation"), null, default)).Succeeded);
        var updated = await service.UpdateMeterReadingAsync(reading.Value!.Id,
            new(garage.Id, MeterKinds.Water, October, October.AddDays(20), 13, null, reading.Value.Version), null, default);
        Assert.True(updated.Succeeded, updated.ErrorMessage);
        var remaining = await context.Accruals.SingleAsync();
        Assert.True(remaining.IsCanceled);
        Assert.Equal(accrual.Id, remaining.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnnualCatchUpReusesEarlierUnclassifiedRecordAndPreservesPaidHistory(bool paid)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(context, October);
        var originalDueDate = October.AddDays(20);
        var old = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            AccountingMonth = October,
            Amount = 900,
            Source = AccrualSources.Regular,
            DueDate = originalDueDate,
            OverdueFromDate = originalDueDate.AddDays(31)
        };
        var unrelatedMonthly = new Accrual
        {
            Garage = garage,
            IncomeType = new IncomeType { Name = "Synthetic unrelated history", Code = "synthetic_unrelated_history" },
            AccountingMonth = October.AddMonths(-1),
            Amount = 125,
            Source = AccrualSources.Regular
        };
        context.AddRange(old, unrelatedMonthly);
        if (paid)
        {
            var payment = new FinancialOperation
            {
                Garage = garage,
                IncomeType = setting.IncomeType,
                OperationKind = FinancialOperationKinds.Income,
                AccountingMonth = October,
                OperationDate = October.AddDays(10),
                Amount = 100
            };
            context.AddRange(payment, new AccrualPaymentAllocation { Accrual = old, FinancialOperation = payment, Amount = 100 });
        }
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        var annualCard = await service.CalculateGarageAnnualPaymentsAsync(garage.Id, 2026, null, default);
        Assert.True(annualCard.Succeeded, annualCard.ErrorMessage);
        var result = await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(November, November), null, default);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(2, await context.Accruals.CountAsync());
        Assert.Equal(old.Id, (await context.Accruals.SingleAsync(item => item.IncomeTypeId == setting.IncomeTypeId)).Id);
        Assert.Equal(900m, old.Amount);
        Assert.Null(old.AccountingYear);
        Assert.Null(old.IsAnnualAccrual);
        Assert.Equal(originalDueDate, old.DueDate);
        Assert.False(unrelatedMonthly.IsCanceled);
        Assert.Equal(125m, unrelatedMonthly.Amount);
    }

    [Fact]
    public async Task CancellationLookupUsesLatestLifecycleAndExcludesOtherGaragesAndManualCharges()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(context, October);
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        Assert.True((await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(October, October), null, default)).Succeeded);
        var accrual = await context.Accruals.SingleAsync();
        accrual.IsCanceled = true;
        var manual = new AuditEvent { EntityType = "accrual", EntityId = accrual.Id.ToString(), Action = "finance.accrual_canceled", CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2), Summary = "Synthetic manual cancel" };
        context.Add(manual);
        await context.SaveChangesAsync();
        var repository = new EfAccrualRepository(context);
        Assert.Single(await repository.GetManualRegularCancellationsAsync(garage.Id, null, November, November, default));
        var generationState = await repository.GetRegularGenerationStateAsync(setting.IncomeTypeId!.Value, November, 2026, default);
        Assert.Empty(generationState.ExistingGarageIds);
        Assert.Contains(garage.Id, generationState.CanceledGarageIds);
        Assert.Empty(await repository.GetManualRegularCancellationsAsync(Guid.NewGuid(), null, November, November, default));
        Assert.Empty(await repository.GetManualRegularCancellationsAsync(null, Guid.NewGuid(), November, November, default));
        context.Add(new AuditEvent { EntityType = "accrual", EntityId = accrual.Id.ToString(), Action = "finance.accrual_restored", CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1), Summary = "Synthetic restore" });
        context.Add(new AuditEvent { EntityType = "accrual", EntityId = accrual.Id.ToString(), Action = "finance.regular_accrual_canceled_without_tariff", CreatedAtUtc = DateTimeOffset.UtcNow, Summary = "Synthetic automatic cancel" });
        await context.SaveChangesAsync();
        Assert.Empty(await repository.GetManualRegularCancellationsAsync(garage.Id, null, October, November, default));
        generationState = await repository.GetRegularGenerationStateAsync(setting.IncomeTypeId!.Value, November, 2026, default);
        Assert.Empty(generationState.CanceledGarageIds);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetManualRegularCancellationsAsync(null, null, October, November, default));
        Assert.True((await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(October, October), null, default)).Succeeded);
        Assert.Single(await context.Accruals.Where(item => !item.IsCanceled).ToArrayAsync());
    }

    [Fact]
    public async Task ExplicitAnnualCancellationWithoutStoredYearBlocksChangedDueMonth()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyLegacyAnnualCancellationAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ExplicitAnnualCancellationWithoutStoredYearBlocksChangedDueMonth()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyLegacyAnnualCancellationAsync(context);
    }

    private static async Task VerifyLegacyAnnualCancellationAsync(GarageBalanceDbContext context)
    {
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(context, October);
        var old = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            AccountingMonth = October,
            IsAnnualAccrual = true,
            AccountingYear = null,
            Amount = 1200,
            Source = AccrualSources.Regular
        };
        context.Add(old);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        Assert.True((await service.CancelAccrualAsync(old.Id, new("Synthetic annual cancellation"), null, default)).Succeeded);
        setting.AccrualStartMonth = 11;
        setting.PaymentDueMonth = 11;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var canceled = Assert.Single(await new EfAccrualRepository(context)
            .GetManualRegularCancellationsAsync(garage.Id, null, November, November, default));
        Assert.Equal(2026, canceled.AccountingYear);
        Assert.True((await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(November, November), null, default)).Succeeded);
        Assert.True((await service.CalculateGarageAnnualPaymentsAsync(garage.Id, 2026, null, default)).Succeeded);
        var remaining = await context.Accruals.SingleAsync();
        Assert.Equal(old.Id, remaining.Id);
        Assert.True(remaining.IsCanceled);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_TariffRemovalRechecksManualCancellationAfterAcquiringGarageLock()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        Guid incomeTypeId;
        Guid accrualId;
        await using (var setup = database.CreateContext())
        {
            var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(setup, October);
            setting.PeriodicityMonths = 1;
            setting.PaymentDueMonth = null;
            await setup.SaveChangesAsync();
            garageId = garage.Id;
            incomeTypeId = setting.IncomeTypeId!.Value;
            Assert.True((await FinanceServiceTestFactory.Create(setup, businessDate: November)
                .CalculateGarageIncomeWorksheetAsync(garageId, new(October, October), null, default)).Succeeded);
            accrualId = await setup.Accruals.Select(item => item.Id).SingleAsync();
        }
        await using var tariffContext = database.CreateContext();
        var boundary = new BeforeGarageLock(new EfAccrualPaymentAllocationRepository(tariffContext), garageId, async token =>
        {
            await using var cancellationContext = database.CreateContext();
            var result = await FinanceServiceTestFactory.Create(cancellationContext, businessDate: November)
                .CancelAccrualAsync(accrualId, new("Synthetic coordinated cancellation"), null, token);
            Assert.True(result.Succeeded, result.ErrorMessage);
        });
        var canceled = await FinanceServiceTestFactory.Create(tariffContext, businessDate: November, allocationRepository: boundary)
            .CancelUnpaidRegularAccrualsWithoutTariffAsync(incomeTypeId, October, null, "Synthetic tariff removal", default);
        Assert.True(boundary.Triggered);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        Assert.Equal(0, canceled.Value);
        await using var verification = database.CreateContext();
        Assert.Single(await new EfAccrualRepository(verification)
            .GetManualRegularCancellationsAsync(garageId, null, October, October, default));
        Assert.True((await FinanceServiceTestFactory.Create(verification, businessDate: November)
            .CalculateGarageIncomeWorksheetAsync(garageId, new(October, October), null, default)).Succeeded);
        Assert.True((await verification.Accruals.SingleAsync()).IsCanceled);
    }

    private sealed class BeforeGarageLock(IAccrualPaymentAllocationRepository inner, Guid garageId,
        Func<CancellationToken, Task> beforeAcquire) : IAccrualPaymentAllocationRepository
    {
        public bool Triggered { get; private set; }
        public async Task<IAsyncDisposable> AcquireRebuildLockAsync(IReadOnlyCollection<AccrualPaymentAllocationKey> keys, CancellationToken cancellationToken)
        {
            if (!Triggered && keys.Any(key => key.GarageId == garageId))
            {
                Triggered = true;
                await beforeAcquire(cancellationToken);
            }
            return await inner.AcquireRebuildLockAsync(keys, cancellationToken);
        }
        public Task<IAsyncDisposable> AcquireGarageIncomeWorksheetLockAsync(Guid id, CancellationToken cancellationToken) =>
            inner.AcquireGarageIncomeWorksheetLockAsync(id, cancellationToken);
        public Task<AccrualPaymentAllocationRebuildResult> RebuildAsync(IReadOnlyCollection<AccrualPaymentAllocationKey> keys, CancellationToken cancellationToken) =>
            inner.RebuildAsync(keys, cancellationToken);
        public Task<bool> HasActiveAllocationAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => inner.HasActiveAllocationAsync(ids, cancellationToken);
        public Task<IReadOnlySet<Guid>> GetActivelyAllocatedAccrualIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => inner.GetActivelyAllocatedAccrualIdsAsync(ids, cancellationToken);
        public Task<decimal> GetActiveAllocatedAmountAsync(Guid id, CancellationToken cancellationToken) => inner.GetActiveAllocatedAmountAsync(id, cancellationToken);
    }
}
