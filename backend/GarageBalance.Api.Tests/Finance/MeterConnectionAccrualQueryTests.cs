using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterConnectionAccrualQueryTests
{
    [Fact]
    public async Task CustomServiceQueriesSelectOnlyMatchingActiveRegularAccruals()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertCustomServiceQueriesAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_CustomServiceQueriesSelectOnlyMatchingActiveRegularAccruals()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertCustomServiceQueriesAsync(context);
    }

    private static async Task AssertCustomServiceQueriesAsync(GarageBalanceDbContext context)
    {
        var month = new DateOnly(2026, 9, 1);
        var kind = MeterKinds.ForService(Guid.NewGuid());
        var otherKind = MeterKinds.ForService(Guid.NewGuid());
        var garage = new Garage { Number = "connection-accrual-query" };
        var otherGarage = new Garage { Number = "connection-accrual-query-other" };
        var income = new IncomeType { Code = "connection_accrual_query", Name = "Проверка выборки подключённой услуги" };
        var current = Create(garage, month);
        var later = Create(garage, month.AddMonths(1));
        var before = Create(garage, month.AddMonths(-1));
        var canceled = Create(garage, month);
        canceled.IsCanceled = true;
        var manual = Create(garage, month);
        manual.Source = AccrualSources.Manual;
        var otherService = Create(garage, month);
        otherService.CalculationMeterKind = otherKind;
        otherService.IncomeType = new IncomeType { Code = "connection_accrual_other", Name = "Другая услуга" };
        var unmetered = Create(garage, month);
        unmetered.RequiresMeterReading = false;
        unmetered.CalculationMeterKind = null;
        unmetered.IncomeType = new IncomeType { Code = "connection_accrual_unmetered", Name = "Услуга без счётчика" };
        context.AddRange(garage, otherGarage, income, current, later, before, canceled, manual,
            otherService, unmetered, Create(otherGarage, month));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new EfAccrualRepository(context);

        var exact = await repository.GetActiveMeteredForUpdateAsync(garage.Id, month, kind, CancellationToken.None);
        Assert.Equal(current.Id, Assert.Single(exact).Id);
        Assert.Equal(EntityState.Unchanged, context.Entry(exact[0]).State);
        var chain = await repository.GetActiveMeteredFromForUpdateAsync(garage.Id, month, kind, CancellationToken.None);
        Assert.Equal(new[] { current.Id, later.Id }, chain.Select(item => item.Id));
        Assert.Empty(await repository.GetActiveMeteredForUpdateAsync(garage.Id, month.AddMonths(2), kind, CancellationToken.None));
        Assert.Empty(await repository.GetActiveMeteredFromForUpdateAsync(garage.Id, month.AddMonths(2), kind, CancellationToken.None));
        Assert.Empty(await repository.GetActiveMeteredForUpdateAsync(garage.Id, month, "unknown", CancellationToken.None));
        Assert.Empty(await repository.GetActiveMeteredFromForUpdateAsync(garage.Id, month, "unknown", CancellationToken.None));
        using var canceledToken = new CancellationTokenSource();
        canceledToken.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetActiveMeteredForUpdateAsync(garage.Id, month, kind, canceledToken.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetActiveMeteredFromForUpdateAsync(garage.Id, month, kind, canceledToken.Token));

        Accrual Create(Garage target, DateOnly accountingMonth) => new()
        {
            Garage = target,
            IncomeType = income,
            AccountingMonth = accountingMonth,
            DueDate = accountingMonth.AddDays(20),
            OverdueFromDate = accountingMonth.AddDays(21),
            Amount = 25m,
            Source = AccrualSources.Regular,
            RequiresMeterReading = true,
            CalculationMeterKind = kind
        };
    }
}
