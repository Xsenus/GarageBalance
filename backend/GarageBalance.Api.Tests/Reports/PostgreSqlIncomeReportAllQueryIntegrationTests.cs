using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlIncomeReportAllQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task AllRowsLoadTotalsBoundedPageAndSequentialDebtInAtMostTwoCommands()
    {
        var month = new DateOnly(2043, 3, 1);
        var suffix = Guid.NewGuid().ToString("N");
        var garageNumber = $"INCOME-ALL-{suffix}";
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        Guid ownerId;
        Guid incomeTypeId;
        await using (var seedContext = database.CreateContext())
        {
            var owner = new Owner { LastName = "Петров", FirstName = $"Комплексный {suffix}" };
            var garage = new Garage { Number = garageNumber, StartingBalance = 100m, Owner = owner };
            var incomeType = new IncomeType { Name = $"Членский взнос {suffix}" };
            seedContext.AddRange(owner, garage, incomeType);
            seedContext.Accruals.AddRange(
                CreateAccrual(garage, incomeType, month, 500m),
                CreateAccrual(garage, incomeType, month, 999m, true),
                CreateAccrual(garage, incomeType, month.AddMonths(-1), 300m));
            seedContext.FinancialOperations.AddRange(
                CreatePayment(garage, incomeType, month.AddMonths(-1).AddDays(4), 50m, "ALL-PREVIOUS"),
                CreatePayment(garage, incomeType, month.AddDays(4), 200m, "ALL-FIRST"),
                CreatePayment(garage, incomeType, month.AddDays(8), 100m, "ALL-SECOND"),
                CreatePayment(garage, incomeType, month.AddDays(10), 777m, "ALL-CANCELED", true));
            await seedContext.SaveChangesAsync();
            garageId = garage.Id;
            ownerId = owner.Id;
            incomeTypeId = incomeType.Id;
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var query = new EfIncomeReportQuery(context);

        var accrualPage = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "all",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            1,
            0,
            new ReportSort("accrualAmount", true),
            CancellationToken.None);

        Assert.Equal(4, accrualPage.RowCount);
        Assert.Equal(600m, accrualPage.AccrualTotal);
        Assert.Equal(300m, accrualPage.IncomeTotal);
        var accrualRow = Assert.Single(accrualPage.Rows);
        Assert.Equal("accruals", accrualRow.RowType);
        Assert.Equal(500m, accrualRow.AccrualAmount);
        var combinedCommand = Assert.Single(capture.Commands);
        Assert.Contains("WITH filtered_rows AS", combinedCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(SUM(accrual_amount), 0)", combinedCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(SUM(income_amount), 0)", combinedCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(combinedCommand, "FROM accruals"));
        Assert.Equal(1, CountOccurrences(combinedCommand, "FROM financial_operations"));

        capture.Commands.Clear();
        var fullPage = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "all",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(4, fullPage.RowCount);
        Assert.Equal(600m, fullPage.AccrualTotal);
        Assert.Equal(300m, fullPage.IncomeTotal);
        Assert.Equal(4, fullPage.Rows.Count);
        var payments = fullPage.Rows.Where(row => row.RowType == "payments").ToList();
        Assert.Collection(
            payments,
            row =>
            {
                Assert.Equal("ALL-FIRST", row.DocumentNumber);
                Assert.Equal(650m, row.DebtAfterPayment);
            },
            row =>
            {
                Assert.Equal("ALL-SECOND", row.DocumentNumber);
                Assert.Equal(550m, row.DebtAfterPayment);
            });
        Assert.Equal(2, capture.Commands.Count);

        foreach (var descending in new[] { false, true })
        {
            foreach (var field in new[] { "date", "accountingMonth", "garageNumber", "ownerName", "incomeTypeName", "accrualAmount", "incomeAmount", "debt", "documentNumber" })
            {
                capture.Commands.Clear();
                var sortedPage = await query.GetRowsAsync(
                    month,
                    month.AddMonths(1).AddDays(-1),
                    "all",
                    new HashSet<Guid>(),
                    new HashSet<Guid>(),
                    new HashSet<Guid>(),
                    null,
                    1,
                    1,
                    new ReportSort(field, descending),
                    CancellationToken.None);

                Assert.Equal(4, sortedPage.RowCount);
                Assert.Equal(600m, sortedPage.AccrualTotal);
                Assert.Equal(300m, sortedPage.IncomeTotal);
                Assert.Single(sortedPage.Rows);
                Assert.InRange(capture.Commands.Count, 1, 2);
            }
        }

        capture.Commands.Clear();
        var filtered = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "all",
            new HashSet<Guid> { garageId },
            new HashSet<Guid> { ownerId },
            new HashSet<Guid>(),
            garageNumber.ToUpperInvariant(),
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(4, filtered.RowCount);
        Assert.Equal(600m, filtered.AccrualTotal);
        Assert.Equal(300m, filtered.IncomeTotal);
        Assert.Equal(2, capture.Commands.Count);

        capture.Commands.Clear();
        var typeFiltered = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "all",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid> { incomeTypeId },
            null,
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(3, typeFiltered.RowCount);
        Assert.Equal(500m, typeFiltered.AccrualTotal);
        Assert.Equal(300m, typeFiltered.IncomeTotal);
        Assert.DoesNotContain(typeFiltered.Rows, row => row.RowType == "starting_balance");
        Assert.Equal(2, capture.Commands.Count);

        capture.Commands.Clear();
        var startingBalanceSearch = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "all",
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
        Assert.Equal(0m, startingBalanceSearch.IncomeTotal);
        Assert.Equal("starting_balance", Assert.Single(startingBalanceSearch.Rows).RowType);
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
            Source = "income_all_query_integration",
            IsCanceled = isCanceled
        };

    private static FinancialOperation CreatePayment(
        Garage garage,
        IncomeType incomeType,
        DateOnly operationDate,
        decimal amount,
        string documentNumber,
        bool isCanceled = false) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = operationDate,
            AccountingMonth = new DateOnly(operationDate.Year, operationDate.Month, 1),
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType,
            DocumentNumber = documentNumber,
            IsCanceled = isCanceled,
            CreatedAtUtc = new DateTimeOffset(operationDate.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)
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
