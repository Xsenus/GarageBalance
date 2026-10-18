using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Finance;

// Accruals rewritten by the former multi-month worksheet calculation are restored once.
public sealed class AccrualPeriodRepairTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly Today = new(2026, 10, 18);

    [Fact]
    public async Task Sqlite_RestoresCanceledSeptemberChargeOnceAndSkipsUnaffectedGarages()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertRepairAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RestoresCanceledSeptemberChargeOnceAndSkipsUnaffectedGarages()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertRepairAsync(context);
    }

    private static async Task AssertRepairAsync(GarageBalanceDbContext context)
    {
        foreach (var existing in context.ChargeServiceSettings) existing.IsArchived = true;
        var income = new IncomeType { Name = "Synthetic repair", Code = "synthetic_repair" };
        var septemberTariff = new Tariff { Name = "Synthetic repair 2026", CalculationBase = TariffCalculationBases.Fixed, Rate = 100, EffectiveFrom = new(2026, 1, 1) };
        var octoberTariff = new Tariff { Name = "Synthetic repair October", CalculationBase = TariffCalculationBases.Fixed, Rate = 125, EffectiveFrom = October };
        var setting = new ChargeServiceSetting
        {
            Name = "Synthetic repair",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            UnitName = "гараж",
            IncomeType = income,
            Tariff = octoberTariff
        };
        setting.TariffVersions.Add(new ChargeServiceTariffVersion { Tariff = septemberTariff, EffectiveFrom = septemberTariff.EffectiveFrom, EffectiveTo = October.AddDays(-1) });
        setting.TariffVersions.Add(new ChargeServiceTariffVersion { Tariff = octoberTariff, EffectiveFrom = October });
        var affected = new Garage { Number = "SYNTHETIC-REPAIR", RegisteredOn = October, AccountingStartMonth = September, PeopleCount = 1 };
        var untouched = new Garage { Number = "SYNTHETIC-UNTOUCHED", RegisteredOn = October, AccountingStartMonth = September, PeopleCount = 1 };
        context.AddRange(setting, affected, untouched);
        // State left by the former calculation: September canceled with October terms.
        var canceled = new Accrual
        {
            Garage = affected,
            IncomeType = income,
            Tariff = octoberTariff,
            AccountingMonth = September,
            DueDate = September.AddDays(29),
            OverdueFromDate = October.AddDays(29),
            Amount = 100m,
            Source = AccrualSources.Regular,
            IsCanceled = true
        };
        context.Add(canceled);
        context.AuditEvents.Add(new AuditEvent
        {
            Action = AccrualPeriodRepairService.AffectedActions[1],
            EntityType = "accrual",
            EntityId = canceled.Id.ToString(),
            RelatedGarageId = affected.Id.ToString(),
            RelatedAccountingMonth = "2026-09",
            Summary = "Synthetic former recalculation"
        });
        await context.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddScoped(_ => context);
        services.AddScoped<IFinanceService>(_ => FinanceServiceTestFactory.Create(context, businessDate: Today));
        services.AddScoped<IApplicationSettingRepository>(_ => new EfApplicationSettingRepository(context));
        services.AddScoped<IAccrualPeriodRepairQuery>(_ => new EfAccrualPeriodRepairQuery(context));
        services.AddScoped<IBusinessDateProvider>(_ => new TestBusinessDateProvider(Today));
        services.AddScoped<IAuditEventWriter>(_ => new AuditEventWriter(context));
        await using var provider = services.BuildServiceProvider();
        var repair = new AccrualPeriodRepairService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AccrualPeriodRepairService>.Instance);

        var first = await repair.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new AccrualPeriodRepairResult(false, 1, 1, 0), first);
        context.ChangeTracker.Clear();
        var active = await context.Accruals.AsNoTracking()
            .Where(accrual => accrual.GarageId == affected.Id && !accrual.IsCanceled)
            .OrderBy(accrual => accrual.AccountingMonth)
            .Select(accrual => new { accrual.AccountingMonth, accrual.Amount })
            .ToListAsync();
        Assert.Equal([(September, 100m), (October, 125m)], active.Select(row => (row.AccountingMonth, row.Amount)).ToArray());
        Assert.False(await context.Accruals.AnyAsync(accrual => accrual.GarageId == untouched.Id));
        Assert.Single(await context.AuditEvents.Where(item => item.Action == "finance.accrual_period_repair_completed").ToListAsync());
        Assert.NotNull(await context.ApplicationSettings.SingleOrDefaultAsync(item => item.Key == AccrualPeriodRepairService.CompletedSettingKey));

        var second = await repair.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new AccrualPeriodRepairResult(true, 0, 0, 0), second);
        Assert.Single(await context.AuditEvents.Where(item => item.Action == "finance.accrual_period_repair_completed").ToListAsync());
    }

    [Fact]
    public async Task QuerySkipsMalformedAuditRowsAndKeepsTheEarliestMonthPerGarage()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garageId = Guid.NewGuid();
        foreach (var (garage, month) in new[] { (garageId.ToString(), "2026-08"), (garageId.ToString(), "2026-03"), ("not-a-guid", "2026-01"), (Guid.NewGuid().ToString(), "bad") })
        {
            database.Context.AuditEvents.Add(new AuditEvent
            {
                Action = AccrualPeriodRepairService.AffectedActions[0],
                EntityType = "accrual",
                RelatedGarageId = garage,
                RelatedAccountingMonth = month,
                Summary = "Synthetic"
            });
        }
        database.Context.AuditEvents.Add(new AuditEvent
        {
            Action = "finance.other",
            EntityType = "accrual",
            RelatedGarageId = Guid.NewGuid().ToString(),
            RelatedAccountingMonth = "2020-01",
            Summary = "Synthetic"
        });
        await database.Context.SaveChangesAsync();

        var candidates = await new EfAccrualPeriodRepairQuery(database.Context)
            .GetCandidatesAsync(AccrualPeriodRepairService.AffectedActions, CancellationToken.None);

        Assert.Equal(new AccrualPeriodRepairCandidate(garageId, new DateOnly(2026, 3, 1)), Assert.Single(candidates));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerSkipsWhenAutomationIsDisabledAndContainsFailures(bool enabled)
    {
        // An empty container makes any attempted run fail while resolving its dependencies.
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var repair = new AccrualPeriodRepairService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AccrualPeriodRepairService>.Instance);
        using var worker = new AccrualPeriodRepairWorker(repair,
            Options.Create(new RegularAccrualAutomationOptions { Enabled = enabled }), NullLogger<AccrualPeriodRepairWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repair.RunOnceAsync(CancellationToken.None));
    }
}
