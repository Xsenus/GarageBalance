using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlIrregularPaymentStateTests
{
    [PostgreSqlFact]
    public async Task GetIrregularPaymentStateAsync_SumsRepeatedAccrualsAndOnlyTheirActivePaymentsOnServer()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var garage = new Garage { Number = "IRREGULAR-STATE-1" };
        var otherGarage = new Garage { Number = "IRREGULAR-STATE-2" };
        var incomeType = new IncomeType { Name = "Проверка состояния разовых начислений" };
        var template = new IrregularPayment { Name = "Карта для проверки суммы", Amount = 100m };
        var otherTemplate = new IrregularPayment { Name = "Другое основание для проверки суммы", Amount = 500m };
        var month = new DateOnly(2026, 9, 1);
        var first = AccrualFor(garage, incomeType, template, month, 100m);
        var second = AccrualFor(garage, incomeType, template, month, 200m);
        var canceledAccrual = AccrualFor(garage, incomeType, template, month, 900m);
        canceledAccrual.IsCanceled = true;
        var payment = IncomeFor(garage, incomeType, month, 230m);
        var inactiveAllocationPayment = IncomeFor(garage, incomeType, month, 80m);
        var canceledPayment = IncomeFor(garage, incomeType, month, 60m);
        canceledPayment.IsCanceled = true;

        await using (var setup = database.CreateContext())
        {
            setup.AddRange(first, second, canceledAccrual,
                AccrualFor(garage, incomeType, otherTemplate, month, 500m),
                AccrualFor(garage, incomeType, template, month.AddMonths(1), 700m),
                AccrualFor(otherGarage, incomeType, template, month, 800m),
                new AccrualPaymentAllocation { Accrual = first, FinancialOperation = payment, Amount = 40m },
                new AccrualPaymentAllocation { Accrual = second, FinancialOperation = payment, Amount = 90m },
                new AccrualPaymentAllocation { Accrual = canceledAccrual, FinancialOperation = payment, Amount = 100m },
                new AccrualPaymentAllocation { Accrual = first, FinancialOperation = canceledPayment, Amount = 60m },
                new AccrualPaymentAllocation { Accrual = second, FinancialOperation = inactiveAllocationPayment, Amount = 80m, IsActive = false });
            await setup.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var result = await new EfAccrualRepository(queryContext).GetIrregularPaymentStateAsync(
            garage.Id, template.Id, month, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsAvailable);
        Assert.Equal(300m, result.Amount);
        Assert.Equal(130m, result.PaidAmount);
        Assert.Equal(170m, result.OutstandingAmount);
        Assert.Empty(queryContext.ChangeTracker.Entries());
        var sql = Assert.Single(capture.Commands);
        Assert.Contains("sum(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Comment\"", sql, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task GetIrregularPaymentStateAsync_PreservesEmptyUnavailableAndFullyPaidStates()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "IRREGULAR-STATE-EMPTY" };
        var incomeType = new IncomeType { Name = "Проверка доступности разового начисления" };
        var template = new IrregularPayment { Name = "Карта для проверки доступности", Amount = 100m };
        var month = new DateOnly(2026, 9, 1);
        var first = AccrualFor(garage, incomeType, template, month, 100m);
        var second = AccrualFor(garage, incomeType, template, month, 200m);
        var payment = IncomeFor(garage, incomeType, month, 300m);
        context.AddRange(first, second,
            new AccrualPaymentAllocation { Accrual = first, FinancialOperation = payment, Amount = 100m },
            new AccrualPaymentAllocation { Accrual = second, FinancialOperation = payment, Amount = 200m });
        await context.SaveChangesAsync();
        var repository = new EfAccrualRepository(context);

        var paid = await repository.GetIrregularPaymentStateAsync(garage.Id, template.Id, month, CancellationToken.None);
        Assert.NotNull(paid);
        Assert.True(paid.IsAvailable);
        Assert.Equal(300m, paid.Amount);
        Assert.Equal(300m, paid.PaidAmount);
        Assert.Equal(0m, paid.OutstandingAmount);

        template.IsActive = false;
        await context.SaveChangesAsync();
        var inactive = await repository.GetIrregularPaymentStateAsync(garage.Id, template.Id, month, CancellationToken.None);
        Assert.NotNull(inactive);
        Assert.False(inactive.IsAvailable);
        Assert.Equal(300m, inactive.Amount);

        template.IsActive = true;
        template.IsArchived = true;
        await context.SaveChangesAsync();
        var archived = await repository.GetIrregularPaymentStateAsync(garage.Id, template.Id, month, CancellationToken.None);
        Assert.NotNull(archived);
        Assert.False(archived.IsAvailable);
        Assert.Equal(300m, archived.PaidAmount);

        Assert.Null(await repository.GetIrregularPaymentStateAsync(Guid.NewGuid(), template.Id, month, CancellationToken.None));
        Assert.Null(await repository.GetIrregularPaymentStateAsync(garage.Id, Guid.NewGuid(), month, CancellationToken.None));
        Assert.Null(await repository.GetIrregularPaymentStateAsync(garage.Id, template.Id, month.AddMonths(1), CancellationToken.None));
        first.IsCanceled = true;
        second.IsCanceled = true;
        await context.SaveChangesAsync();
        Assert.Null(await repository.GetIrregularPaymentStateAsync(garage.Id, template.Id, month, CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task ActiveIrregularDuplicateExistsAsync_ProtectsOnlyActiveRegularAccruals()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "IRREGULAR-STATE-REGULAR" };
        var incomeType = new IncomeType { Name = "Проверка уникальности регулярного основания" };
        var template = new IrregularPayment { Name = "Основание для проверки уникальности", Amount = 100m };
        var month = new DateOnly(2026, 9, 1);
        var manual = AccrualFor(garage, incomeType, template, month, 100m);
        var regular = AccrualFor(garage, incomeType, template, month, 100m);
        regular.Source = AccrualSources.Regular;
        regular.IsCanceled = true;
        context.AddRange(manual, regular);
        await context.SaveChangesAsync();
        var repository = new EfAccrualRepository(context);

        Assert.False(await repository.ActiveIrregularDuplicateExistsAsync(null, garage.Id, template.Id, month, CancellationToken.None));
        regular.IsCanceled = false;
        await context.SaveChangesAsync();
        Assert.True(await repository.ActiveIrregularDuplicateExistsAsync(null, garage.Id, template.Id, month, CancellationToken.None));
        Assert.False(await repository.ActiveIrregularDuplicateExistsAsync(regular.Id, garage.Id, template.Id, month, CancellationToken.None));
        Assert.False(await repository.ActiveIrregularDuplicateExistsAsync(null, Guid.NewGuid(), template.Id, month, CancellationToken.None));
        Assert.False(await repository.ActiveIrregularDuplicateExistsAsync(null, garage.Id, Guid.NewGuid(), month, CancellationToken.None));
        Assert.False(await repository.ActiveIrregularDuplicateExistsAsync(null, garage.Id, template.Id, month.AddMonths(1), CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task GetIrregularPaymentStateAsync_PropagatesCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EfAccrualRepository(context).GetIrregularPaymentStateAsync(
                Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 1), cancellation.Token));
    }

    private static Accrual AccrualFor(
        Garage garage,
        IncomeType incomeType,
        IrregularPayment template,
        DateOnly month,
        decimal amount) =>
        new()
        {
            Garage = garage,
            IncomeType = incomeType,
            IrregularPayment = template,
            Basis = template.Name,
            AccountingMonth = month,
            DueDate = month.AddMonths(1).AddDays(-1),
            OverdueFromDate = month.AddMonths(2),
            Amount = amount,
            Source = AccrualSources.Manual
        };

    private static FinancialOperation IncomeFor(Garage garage, IncomeType incomeType, DateOnly month, decimal amount) => new()
    {
        Garage = garage,
        IncomeType = incomeType,
        OperationKind = FinancialOperationKinds.Income,
        OperationDate = month,
        AccountingMonth = month,
        Amount = amount
    };

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Add(command.CommandText);
            }

            return ValueTask.FromResult(result);
        }
    }
}
