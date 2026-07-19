using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlIncomeReportAccrualQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task AccrualPageLoadsStartingBalanceAccrualTotalsAndPageInOneCommand()
    {
        var month = new DateOnly(2042, 12, 1);
        var suffix = Guid.NewGuid().ToString("N");
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid incomeTypeId;
        await using (var seedContext = database.CreateContext())
        {
            var owner = new Owner { LastName = "Иванов", FirstName = $"Тест {suffix}" };
            var garage = new Garage { Number = $"ACCRUAL-{suffix}", StartingBalance = 100m, Owner = owner };
            var incomeType = new IncomeType { Name = $"Целевой взнос {suffix}" };
            seedContext.AddRange(owner, garage, incomeType);
            seedContext.Accruals.AddRange(
                CreateAccrual(garage, incomeType, month, 500m),
                CreateAccrual(garage, incomeType, month, 999m, true),
                CreateAccrual(garage, incomeType, month.AddMonths(-1), 300m));
            await seedContext.SaveChangesAsync();
            incomeTypeId = incomeType.Id;
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var query = new EfIncomeReportQuery(context);

        var page = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "accruals",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            1,
            0,
            new ReportSort("accrualAmount", true),
            CancellationToken.None);

        Assert.Equal(2, page.RowCount);
        Assert.Equal(600m, page.AccrualTotal);
        Assert.Equal(0m, page.IncomeTotal);
        var accrualRow = Assert.Single(page.Rows);
        Assert.Equal("accruals", accrualRow.RowType);
        Assert.Equal(500m, accrualRow.AccrualAmount);
        Assert.Equal($"Целевой взнос {suffix}", accrualRow.IncomeTypeName);
        var pageCommand = Assert.Single(capture.Commands);
        Assert.Contains("WITH filtered_rows AS", pageCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(SUM(accrual_amount), 0)", pageCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(pageCommand, "FROM accruals"));

        capture.Commands.Clear();
        var typeFiltered = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "accruals",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid> { incomeTypeId },
            null,
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(1, typeFiltered.RowCount);
        Assert.Equal(500m, typeFiltered.AccrualTotal);
        Assert.Equal("accruals", Assert.Single(typeFiltered.Rows).RowType);
        Assert.Single(capture.Commands);

        capture.Commands.Clear();
        var startingBalanceSearch = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "accruals",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            "СТАРТОВЫЙ",
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(1, startingBalanceSearch.RowCount);
        Assert.Equal(100m, startingBalanceSearch.AccrualTotal);
        var startingBalanceRow = Assert.Single(startingBalanceSearch.Rows);
        Assert.Equal("starting_balance", startingBalanceRow.RowType);
        Assert.Equal("Стартовый баланс", startingBalanceRow.IncomeTypeName);
        Assert.Single(capture.Commands);
    }

    private static Accrual CreateAccrual(
        Garage garage,
        IncomeType incomeType,
        DateOnly accountingMonth,
        decimal amount,
        bool isCanceled = false) =>
        new()
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = accountingMonth,
            DueDate = accountingMonth.AddMonths(1).AddDays(-1),
            OverdueFromDate = accountingMonth.AddMonths(1),
            Amount = amount,
            Source = "income_accrual_query_integration",
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
