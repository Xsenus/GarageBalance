using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlConsolidatedMonthlyReportQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task MonthlyReportBuildsAllSectionsFromOneCommandAndOneBaseScan()
    {
        var january = new DateOnly(2043, 1, 1);
        var february = january.AddMonths(1);
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var incomeType = new IncomeType { Name = $"Monthly income {Guid.NewGuid():N}" };
            var expenseType = new ExpenseType { Name = $"Monthly expense {Guid.NewGuid():N}" };
            var garage = new Garage { Number = "MONTHLY-01", PeopleCount = 1, FloorCount = 1, StartingBalance = 100m };
            var archivedGarage = new Garage { Number = "MONTHLY-ARCHIVED", PeopleCount = 1, FloorCount = 1, StartingBalance = 999m, IsArchived = true };
            seedContext.AddRange(incomeType, expenseType, garage, archivedGarage);
            seedContext.FinancialOperations.AddRange(
                CreateOperation(FinancialOperationKinds.Income, january, 50m, garage, incomeType, null, "MONTH-IN-1"),
                CreateOperation(FinancialOperationKinds.Expense, january, 20m, null, null, expenseType, "MONTH-OUT-1"),
                CreateOperation(FinancialOperationKinds.Income, february, 30m, garage, incomeType, null, "MONTH-IN-2"),
                CreateOperation(FinancialOperationKinds.Expense, february, 10m, null, null, expenseType, "MONTH-OUT-2"),
                CreateOperation(FinancialOperationKinds.Income, january, 500m, garage, incomeType, null, "MONTH-CANCELED", true));
            seedContext.Accruals.AddRange(
                CreateAccrual(garage, incomeType, january, 80m),
                CreateAccrual(garage, incomeType, february, 40m),
                CreateAccrual(garage, incomeType, january, 700m, true));
            seedContext.MeterReadings.AddRange(
                CreateReading(garage, january, MeterKinds.Water, 10m),
                CreateReading(garage, february, MeterKinds.Water, 20m),
                CreateReading(garage, february, MeterKinds.Electricity, 30m),
                CreateReading(garage, january, MeterKinds.Electricity, 40m, true));
            await seedContext.SaveChangesAsync();

            var capture = new ReaderCommandCapture();
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseNpgsql(database.ConnectionString)
                .AddInterceptors(capture)
                .Options;
            await using var context = new GarageBalanceDbContext(options);

            var result = await new EfConsolidatedMonthlyReportQuery(context).GetMonthlyDataAsync(
                january,
                february,
                new ReportSort("incomeTotal", true),
                0,
                1,
                CancellationToken.None);

            Assert.Equal(2, result.MonthlyRowCount);
            Assert.Equal(100m, result.GarageStartingBalanceTotal);
            Assert.Equal([new AmountCountByMonth(january, 50m, 1), new AmountCountByMonth(february, 30m, 1)], result.IncomeByMonth);
            Assert.Equal([new AmountCountByMonth(january, 20m, 1), new AmountCountByMonth(february, 10m, 1)], result.ExpenseByMonth);
            Assert.Equal([new AmountCountByMonth(january, 80m, 1), new AmountCountByMonth(february, 40m, 1)], result.AccrualByMonth);
            Assert.Equal([new CountByMonth(january, 1), new CountByMonth(february, 2)], result.MeterReadingsByMonth);
            Assert.Equal(80m, Assert.Single(result.IncomeBreakdown).Amount);
            Assert.Equal(30m, Assert.Single(result.ExpenseBreakdown).Amount);
            var page = Assert.Single(result.MonthlyRows);
            Assert.Equal(january, page.AccountingMonth);
            Assert.Equal(50m, page.IncomeTotal);
            Assert.Equal(20m, page.ExpenseTotal);
            Assert.Equal(180m, page.AccrualTotal);
            Assert.Equal(30m, page.Balance);
            Assert.Equal(130m, page.Debt);
            Assert.Equal(2, page.OperationCount);
            Assert.Equal(2, page.AccrualCount);
            Assert.Equal(1, page.MeterReadingCount);

            var command = Assert.Single(capture.Commands);
            Assert.Equal(1, CountOccurrences(command, "FROM financial_operations"));
            Assert.Equal(1, CountOccurrences(command, "FROM accruals"));
            Assert.Equal(1, CountOccurrences(command, "FROM meter_readings"));
            Assert.Equal(1, CountOccurrences(command, "FROM garages"));
            Assert.Contains("AS MATERIALIZED", command, StringComparison.Ordinal);
            Assert.Contains("FROM monthly_page", command, StringComparison.Ordinal);
        }
    }

    private static FinancialOperation CreateOperation(
        string kind,
        DateOnly month,
        decimal amount,
        Garage? garage,
        IncomeType? incomeType,
        ExpenseType? expenseType,
        string documentNumber,
        bool isCanceled = false) =>
        new()
        {
            OperationKind = kind,
            OperationDate = month.AddDays(10),
            AccountingMonth = month,
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType,
            ExpenseType = expenseType,
            DocumentNumber = documentNumber,
            IsCanceled = isCanceled,
            CreatedAtUtc = new DateTimeOffset(month.AddDays(10).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)
        };

    private static Accrual CreateAccrual(Garage garage, IncomeType incomeType, DateOnly month, decimal amount, bool isCanceled = false) =>
        new()
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = month,
            DueDate = month.AddMonths(1).AddDays(-1),
            OverdueFromDate = month.AddMonths(1),
            Amount = amount,
            Source = "monthly_report_integration_test",
            IsCanceled = isCanceled
        };

    private static MeterReading CreateReading(
        Garage garage,
        DateOnly month,
        string kind,
        decimal currentValue,
        bool isCanceled = false) =>
        new()
        {
            Garage = garage,
            MeterKind = kind,
            AccountingMonth = month,
            ReadingDate = month.AddDays(20),
            CurrentValue = currentValue,
            PreviousValue = 0m,
            Consumption = currentValue,
            IsCanceled = isCanceled
        };

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private sealed class ReaderCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
