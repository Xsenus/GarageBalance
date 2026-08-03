using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlRegularAccrualAutomationLockIntegrationTests
{
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
