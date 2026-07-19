using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlConsolidatedGarageReportQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task GarageRowsReturnCountAndBoundedPageInOneAggregatedCommand()
    {
        var month = new DateOnly(2043, 3, 1);
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var incomeType = new IncomeType { Name = $"Garage summary {Guid.NewGuid():N}" };
            var firstOwner = new Owner { LastName = "Абрамов", FirstName = "Алексей" };
            var secondOwner = new Owner { LastName = "Сидоров", FirstName = "Сергей" };
            var firstGarage = new Garage { Number = "SUMMARY-01", PeopleCount = 1, FloorCount = 1, StartingBalance = 100m, Owner = firstOwner };
            var emptyGarage = new Garage { Number = "SUMMARY-02", PeopleCount = 1, FloorCount = 1 };
            var secondGarage = new Garage { Number = "SUMMARY-03", PeopleCount = 1, FloorCount = 1, Owner = secondOwner };
            var archivedGarage = new Garage { Number = "SUMMARY-04", PeopleCount = 1, FloorCount = 1, StartingBalance = 999m, IsArchived = true };
            seedContext.AddRange(incomeType, firstOwner, secondOwner, firstGarage, emptyGarage, secondGarage, archivedGarage);
            seedContext.FinancialOperations.AddRange(
                CreateIncome(firstGarage, incomeType, month, 50m, "SUMMARY-IN-1"),
                CreateIncome(secondGarage, incomeType, month, 5m, "SUMMARY-IN-2"),
                CreateIncome(firstGarage, incomeType, month, 500m, "SUMMARY-CANCELED", true));
            seedContext.Accruals.AddRange(
                CreateAccrual(firstGarage, incomeType, month, 20m),
                CreateAccrual(secondGarage, incomeType, month, 30m),
                CreateAccrual(firstGarage, incomeType, month, 700m, true));
            seedContext.MeterReadings.Add(CreateReading(firstGarage, month));
            await seedContext.SaveChangesAsync();

            var capture = new ReaderCommandCapture();
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseNpgsql(database.ConnectionString)
                .AddInterceptors(capture)
                .Options;
            await using var context = new GarageBalanceDbContext(options);
            var query = new EfConsolidatedGarageReportQuery(context);

            var page = await query.GetGarageRowsAsync(null, month, month, 1, CancellationToken.None);

            Assert.Equal(2, page.RowCount);
            var firstRow = Assert.Single(page.Rows);
            Assert.Equal(firstGarage.Id, firstRow.GarageId);
            Assert.Equal(50m, firstRow.IncomeTotal);
            Assert.Equal(120m, firstRow.AccrualTotal);
            Assert.Equal(1, firstRow.MeterReadingCount);
            AssertQueryShape(Assert.Single(capture.Commands));

            capture.Commands.Clear();
            var searched = await query.GetGarageRowsAsync("СИДОРОВ", month, month, null, CancellationToken.None);

            Assert.Equal(1, searched.RowCount);
            var searchedRow = Assert.Single(searched.Rows);
            Assert.Equal(secondGarage.Id, searchedRow.GarageId);
            Assert.Equal(5m, searchedRow.IncomeTotal);
            Assert.Equal(30m, searchedRow.AccrualTotal);
            Assert.Equal(0, searchedRow.MeterReadingCount);
            var searchCommand = Assert.Single(capture.Commands);
            AssertQueryShape(searchCommand);
            Assert.Contains("LOWER(owner.\"LastName\")", searchCommand, StringComparison.Ordinal);
        }
    }

    private static void AssertQueryShape(string command)
    {
        Assert.Equal(1, CountOccurrences(command, "FROM financial_operations"));
        Assert.Equal(1, CountOccurrences(command, "FROM accruals"));
        Assert.Equal(1, CountOccurrences(command, "FROM meter_readings"));
        Assert.Equal(1, CountOccurrences(command, "FROM garages"));
        Assert.Contains("FROM page_rows", command, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)::int", command, StringComparison.Ordinal);
    }

    private static FinancialOperation CreateIncome(
        Garage garage,
        IncomeType incomeType,
        DateOnly month,
        decimal amount,
        string documentNumber,
        bool isCanceled = false) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = month.AddDays(10),
            AccountingMonth = month,
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType,
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
            Source = "consolidated_garage_integration_test",
            IsCanceled = isCanceled
        };

    private static MeterReading CreateReading(Garage garage, DateOnly month) =>
        new()
        {
            Garage = garage,
            MeterKind = MeterKinds.Water,
            AccountingMonth = month,
            ReadingDate = month.AddDays(20),
            CurrentValue = 10m,
            PreviousValue = 0m,
            Consumption = 10m
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
