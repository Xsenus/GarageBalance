using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlGarageBalanceHistoryQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task GetAsync_AggregatesOpeningAndMonthlyValuesWithOneScanPerFinancialSource()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var garage = new Garage { Number = "BALANCE-HISTORY-1", StartingBalance = 100m };
        var otherGarage = new Garage { Number = "BALANCE-HISTORY-2" };
        var incomeType = new IncomeType { Name = "Balance history" };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(garage, otherGarage, incomeType);
            setupContext.Accruals.AddRange(
                AccrualFor(garage, incomeType, new DateOnly(2026, 5, 1), 300m),
                AccrualFor(garage, incomeType, new DateOnly(2026, 6, 1), 500m),
                AccrualFor(garage, incomeType, new DateOnly(2026, 7, 1), 700m),
                AccrualFor(garage, incomeType, new DateOnly(2026, 8, 1), 900m),
                AccrualFor(otherGarage, incomeType, new DateOnly(2026, 6, 1), 400m));
            var canceledAccrual = AccrualFor(garage, incomeType, new DateOnly(2026, 4, 1), 999m);
            canceledAccrual.IsCanceled = true;
            setupContext.Accruals.Add(canceledAccrual);
            setupContext.FinancialOperations.AddRange(
                IncomeFor(garage, incomeType, new DateOnly(2026, 5, 1), 100m, "HISTORY-OPENING"),
                IncomeFor(garage, incomeType, new DateOnly(2026, 6, 1), 200m, "HISTORY-JUNE"),
                IncomeFor(garage, incomeType, new DateOnly(2026, 7, 1), 300m, "HISTORY-JULY"),
                IncomeFor(garage, incomeType, new DateOnly(2026, 8, 1), 900m, "HISTORY-AFTER"),
                IncomeFor(otherGarage, incomeType, new DateOnly(2026, 6, 1), 400m, "HISTORY-OTHER"));
            var canceledIncome = IncomeFor(
                garage,
                incomeType,
                new DateOnly(2026, 4, 1),
                999m,
                "HISTORY-CANCELED");
            canceledIncome.IsCanceled = true;
            setupContext.FinancialOperations.Add(canceledIncome);
            setupContext.FinancialOperations.Add(new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = new DateOnly(2026, 6, 20),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 777m,
                DocumentNumber = "HISTORY-EXPENSE",
                Garage = garage
            });
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);

        var history = await new EfGarageBalanceHistoryQuery(queryContext).GetAsync(
            garage.Id,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 7, 1),
            CancellationToken.None);

        Assert.NotNull(history);
        Assert.Equal(garage.Id, history.GarageId);
        Assert.Equal(100m, history.StartingBalance);
        Assert.Equal(300m, history.PreviousAccrualTotal);
        Assert.Equal(100m, history.PreviousIncomeTotal);
        Assert.Equal(
            [(new DateOnly(2026, 6, 1), 500m), (new DateOnly(2026, 7, 1), 700m)],
            history.AccrualBuckets
                .OrderBy(bucket => bucket.AccountingMonth)
                .Select(bucket => (bucket.AccountingMonth, bucket.Amount))
                .ToArray());
        Assert.Equal(
            [(new DateOnly(2026, 6, 1), 200m), (new DateOnly(2026, 7, 1), 300m)],
            history.IncomeBuckets
                .OrderBy(bucket => bucket.AccountingMonth)
                .Select(bucket => (bucket.AccountingMonth, bucket.Amount))
                .ToArray());

        var command = Assert.Single(capture.Commands);
        Assert.True(CountOccurrences(command, "FROM accruals AS") == 1, command);
        Assert.True(CountOccurrences(command, "FROM financial_operations AS") == 1, command);
        Assert.Equal(2, CountOccurrences(command, "UNION ALL"));
    }

    private static Accrual AccrualFor(
        Garage garage,
        IncomeType incomeType,
        DateOnly accountingMonth,
        decimal amount) =>
        new()
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = accountingMonth,
            DueDate = accountingMonth.AddMonths(1).AddDays(-1),
            OverdueFromDate = accountingMonth.AddMonths(2),
            Amount = amount,
            Source = AccrualSources.Regular
        };

    private static FinancialOperation IncomeFor(
        Garage garage,
        IncomeType incomeType,
        DateOnly accountingMonth,
        decimal amount,
        string documentNumber) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = accountingMonth.AddDays(19),
            AccountingMonth = accountingMonth,
            Amount = amount,
            DocumentNumber = documentNumber,
            Garage = garage,
            IncomeType = incomeType
        };

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

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
