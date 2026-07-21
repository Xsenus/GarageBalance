using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlGarageBalanceTotalsIntegrationTests
{
    [PostgreSqlFact]
    public async Task BalanceTotals_AggregateEachFinancialSourceOnceWithoutChangingRules()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var selectedGarage = new Garage { Number = "BALANCE-TOTALS-1" };
        var otherGarage = new Garage { Number = "BALANCE-TOTALS-2" };
        var incomeType = new IncomeType { Name = "Balance totals" };
        var overdueAccrual = AccrualFor(selectedGarage, incomeType, 100m, new DateOnly(2026, 7, 1));
        var futureAccrual = AccrualFor(selectedGarage, incomeType, 50m, new DateOnly(2026, 8, 1));
        futureAccrual.AccountingMonth = new DateOnly(2026, 6, 1);
        var reviewAccrual = AccrualFor(selectedGarage, incomeType, 300m, new DateOnly(2026, 7, 1));
        reviewAccrual.AccountingMonth = new DateOnly(2026, 5, 1);
        reviewAccrual.DueDateNeedsReview = true;
        reviewAccrual.DueDateReviewReason = "Needs review";
        var canceledAccrual = AccrualFor(selectedGarage, incomeType, 999m, new DateOnly(2026, 7, 1));
        canceledAccrual.AccountingMonth = new DateOnly(2026, 4, 1);
        canceledAccrual.IsCanceled = true;
        var payment = PaymentFor(selectedGarage, incomeType, 80m);
        var canceledPayment = PaymentFor(selectedGarage, incomeType, 999m);
        canceledPayment.IsCanceled = true;
        var otherAccrual = AccrualFor(otherGarage, incomeType, 700m, new DateOnly(2026, 7, 1));

        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(
                selectedGarage,
                otherGarage,
                incomeType,
                overdueAccrual,
                futureAccrual,
                reviewAccrual,
                canceledAccrual,
                otherAccrual,
                payment,
                canceledPayment);
            setupContext.AccrualPaymentAllocations.AddRange(
                new AccrualPaymentAllocation { Accrual = overdueAccrual, FinancialOperation = payment, Amount = 30m },
                new AccrualPaymentAllocation { Accrual = futureAccrual, FinancialOperation = payment, Amount = 20m },
                new AccrualPaymentAllocation
                {
                    Accrual = overdueAccrual,
                    FinancialOperation = payment,
                    Amount = 10m,
                    IsActive = false
                },
                new AccrualPaymentAllocation
                {
                    Accrual = overdueAccrual,
                    FinancialOperation = canceledPayment,
                    Amount = 999m
                });
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);

        var totals = await new EfGarageRepository(
                queryContext,
                TestBusinessDateProvider.From(new FixedTimeProvider(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero))))
            .GetBalanceTotalsAsync([selectedGarage.Id], CancellationToken.None);

        Assert.Equal(450m, totals.AccrualTotals[selectedGarage.Id]);
        Assert.Equal(80m, totals.IncomeTotals[selectedGarage.Id]);
        Assert.Equal(70m, totals.OverdueAccrualTotals[selectedGarage.Id]);
        Assert.Equal(50m, totals.AllocatedIncomeTotals[selectedGarage.Id]);
        Assert.DoesNotContain(otherGarage.Id, totals.AccrualTotals.Keys);

        var command = Assert.Single(capture.Commands);
        Assert.True(CountOccurrences(command, "FROM accruals AS") == 1, command);
        Assert.True(CountOccurrences(command, "FROM accrual_payment_allocations AS") == 1, command);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
    }

    private static Accrual AccrualFor(
        Garage garage,
        IncomeType incomeType,
        decimal amount,
        DateOnly overdueFromDate) =>
        new()
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = new DateOnly(2026, 7, 1),
            DueDate = overdueFromDate.AddDays(-1),
            OverdueFromDate = overdueFromDate,
            Amount = amount,
            Source = AccrualSources.Regular
        };

    private static FinancialOperation PaymentFor(Garage garage, IncomeType incomeType, decimal amount) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 7, 15),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType
        };

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
