using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlRegularAccrualAutomationLockIntegrationTests
{
    [PostgreSqlFact]
    public async Task PreviewRegularAccrualAutomationAsync_ReadsPostgreSqlScopeWithoutWriting()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var owner = new Owner { LastName = "Тестов", FirstName = "Предпросмотр" };
        var garage = new Garage { Number = "preview-1", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var incomeType = new IncomeType { Name = "Предпросмотр начислений", Code = $"preview_{Guid.NewGuid():N}" };
        var tariff = new Tariff
        {
            Name = "Предпросмотр начислений",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 100m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        var service = new ChargeServiceSetting
        {
            Name = "Ежемесячная услуга предпросмотра",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            IncomeType = incomeType,
            Tariff = tariff
        };
        context.AddRange(owner, garage, incomeType, tariff, service);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var accrualCountBefore = await context.Accruals.CountAsync();
        var auditCountBefore = await context.AuditEvents.CountAsync();

        var preview = await FinanceServiceTestFactory.Create(context)
            .PreviewRegularAccrualAutomationAsync(new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 8, 1), preview.AccountingMonth);
        Assert.Equal(1, preview.ActiveGarageCount);
        Assert.True(preview.DueRegularServiceCount >= 1);
        Assert.Equal(preview.DueRegularServiceCount + preview.ActiveFeeCampaignCount, preview.MaximumGarageChecks);
        Assert.Equal(accrualCountBefore, await context.Accruals.CountAsync());
        Assert.Equal(auditCountBefore, await context.AuditEvents.CountAsync());
        Assert.False(context.ChangeTracker.HasChanges());
    }

    [Fact]
    public void LockKey_IsStableForMonthAndSeparatedBetweenMonths()
    {
        var august = EfRegularAccrualAutomationLock.CreateLockKey(new DateOnly(2026, 8, 1));

        Assert.Equal(august, EfRegularAccrualAutomationLock.CreateLockKey(new DateOnly(2026, 8, 31)));
        Assert.NotEqual(august, EfRegularAccrualAutomationLock.CreateLockKey(new DateOnly(2026, 9, 1)));
    }

    [PostgreSqlFact]
    public async Task TryAcquireAsync_SerializesSameMonthAcrossApplicationInstances()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var ownerContext = database.CreateContext();
        await using var contenderContext = database.CreateContext();
        var owner = new EfRegularAccrualAutomationLock(ownerContext);
        var contender = new EfRegularAccrualAutomationLock(contenderContext);
        var month = new DateOnly(2026, 8, 1);

        var ownerLease = await owner.TryAcquireAsync(month, CancellationToken.None);
        Assert.NotNull(ownerLease);

        var startedAt = DateTime.UtcNow;
        var rejectedLease = await contender.TryAcquireAsync(month, CancellationToken.None);

        Assert.Null(rejectedLease);
        Assert.True(DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(2));

        await ownerLease!.DisposeAsync();
        await using var acquiredAfterRelease = await contender.TryAcquireAsync(month, CancellationToken.None);
        Assert.NotNull(acquiredAfterRelease);
    }

    [PostgreSqlFact]
    public async Task TryAcquireAsync_AllowsDifferentMonthsAtTheSameTime()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var first = new EfRegularAccrualAutomationLock(firstContext);
        var second = new EfRegularAccrualAutomationLock(secondContext);

        await using var august = await first.TryAcquireAsync(new DateOnly(2026, 8, 1), CancellationToken.None);
        await using var september = await second.TryAcquireAsync(new DateOnly(2026, 9, 1), CancellationToken.None);

        Assert.NotNull(august);
        Assert.NotNull(september);
    }
}
