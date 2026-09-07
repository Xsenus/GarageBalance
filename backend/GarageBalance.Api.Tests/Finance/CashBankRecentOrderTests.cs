using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Finance;

public sealed class CashBankRecentOrderTests
{
    [Fact]
    public async Task SqliteRecentOperationsFollowDisplayedDateAndTimeBeforeLimiting()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyOrderAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlRecentOperationsFollowDisplayedDateAndTimeBeforeLimiting()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyOrderAsync(context);
    }

    private static async Task VerifyOrderAsync(GarageBalanceDbContext context)
    {
        var repository = new EfCashBankBalanceOperationRepository(context);
        Assert.Empty(await repository.GetRecentAsync(50, CancellationToken.None));
        var date = new DateOnly(2026, 9, 7);
        var start = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 60; index++)
        {
            context.CashBankBalanceOperations.Add(Operation(60 - index, date, start.AddMinutes(index)));
        }
        context.CashBankBalanceOperations.AddRange(
            Operation(1000, date, start.AddMinutes(59)),
            Operation(2000, date.AddDays(1), start.AddDays(-1)),
            Operation(3000, date.AddDays(-1), start.AddDays(1)));
        await context.SaveChangesAsync();

        var expected = new[] { Identifier(2000), Identifier(1000) }
            .Concat(Enumerable.Range(1, 60).Select(Identifier)).Append(Identifier(3000)).ToArray();
        foreach (var take in new[] { 0, 1, 2, 50, 100 })
        {
            var rows = await repository.GetRecentAsync(take, CancellationToken.None);
            Assert.Equal(expected.Take(take), rows.Select(row => row.Id));
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetRecentAsync(50, canceled.Token));
    }

    private static Guid Identifier(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

    private static CashBankBalanceOperation Operation(int id, DateOnly date, DateTimeOffset createdAt) => new()
    {
        Id = Identifier(id),
        OperationDate = date,
        CreatedAtUtc = createdAt,
        Account = id % 2 == 0 ? CashBankAccounts.Cash : CashBankAccounts.Bank,
        OperationKind = CashBankBalanceOperationKinds.Adjustment,
        Direction = CashBankBalanceDirections.Increase,
        Amount = 1m,
        Reason = $"Order check {id}"
    };
}
