using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlGarageReportQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task GarageReportReturnsTotalsCountAndBoundedGroupedOrExpandedPageInOneCommand()
    {
        var month = new DateOnly(2043, 4, 1);
        var suffix = Guid.NewGuid().ToString("N");
        var firstGarageNumber = $"GARAGE-REPORT-A-{suffix}";
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid firstGarageId;
        Guid firstOwnerId;
        Guid primaryIncomeTypeId;
        await using (var seedContext = database.CreateContext())
        {
            var firstOwner = new Owner { LastName = "Иванов", FirstName = $"Отчет {suffix}" };
            var secondOwner = new Owner { LastName = "Петров", FirstName = $"Отчет {suffix}" };
            var firstGarage = new Garage { Number = firstGarageNumber, StartingBalance = 100m, Owner = firstOwner };
            var secondGarage = new Garage { Number = $"GARAGE-REPORT-B-{suffix}", StartingBalance = 50m, Owner = secondOwner };
            var archivedGarage = new Garage { Number = $"GARAGE-REPORT-ARCHIVE-{suffix}", StartingBalance = 900m, IsArchived = true };
            var primaryIncomeType = new IncomeType { Name = $"Основной взнос {suffix}" };
            var secondaryIncomeType = new IncomeType { Name = $"Дополнительный взнос {suffix}" };
            seedContext.AddRange(firstOwner, secondOwner, firstGarage, secondGarage, archivedGarage, primaryIncomeType, secondaryIncomeType);
            seedContext.Accruals.AddRange(
                CreateAccrual(firstGarage, primaryIncomeType, month, 500m),
                CreateAccrual(firstGarage, secondaryIncomeType, month, 200m),
                CreateAccrual(secondGarage, primaryIncomeType, month, 300m),
                CreateAccrual(firstGarage, primaryIncomeType, month, 999m, true),
                CreateAccrual(firstGarage, primaryIncomeType, month.AddMonths(-1), 700m));
            seedContext.FinancialOperations.AddRange(
                CreateIncome(firstGarage, primaryIncomeType, month.AddDays(4), 100m, "GARAGE-PRIMARY"),
                CreateIncome(firstGarage, secondaryIncomeType, month.AddDays(5), 50m, "GARAGE-SECONDARY"),
                CreateIncome(secondGarage, primaryIncomeType, month.AddDays(6), 100m, "GARAGE-SECOND"),
                CreateIncome(firstGarage, primaryIncomeType, month.AddDays(7), 888m, "GARAGE-CANCELED", true),
                CreateIncome(firstGarage, primaryIncomeType, month.AddMonths(1), 600m, "GARAGE-OUTSIDE"));
            await seedContext.SaveChangesAsync();
            firstGarageId = firstGarage.Id;
            firstOwnerId = firstOwner.Id;
            primaryIncomeTypeId = primaryIncomeType.Id;
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var query = new EfGarageReportQuery(context);

        var expandedPage = await query.GetRowsAsync(
            month,
            month,
            null,
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            false,
            0,
            1,
            new ReportSort("difference", true),
            CancellationToken.None);

        Assert.Equal(5, expandedPage.RowCount);
        Assert.Equal(1150m, expandedPage.AccrualTotal);
        Assert.Equal(250m, expandedPage.IncomeTotal);
        var highestDifference = Assert.Single(expandedPage.Rows);
        Assert.Equal(firstGarageNumber, highestDifference.GarageNumber);
        Assert.Equal(500m, highestDifference.AccrualAmount);
        Assert.Equal(100m, highestDifference.IncomeAmount);
        var expandedCommand = Assert.Single(capture.Commands);
        Assert.Contains("WITH filtered_garages AS", expandedCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(SUM(accrual_amount), 0)", expandedCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(SUM(income_amount), 0)", expandedCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(expandedCommand, "FROM accruals"));
        Assert.Equal(1, CountOccurrences(expandedCommand, "FROM financial_operations"));
        Assert.Equal(1, CountOccurrences(expandedCommand, "FROM garages"));

        capture.Commands.Clear();
        var grouped = await query.GetRowsAsync(
            month,
            month,
            null,
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            true,
            0,
            25,
            new ReportSort("garageNumber", false),
            CancellationToken.None);

        Assert.Equal(2, grouped.RowCount);
        Assert.Equal(1150m, grouped.AccrualTotal);
        Assert.Equal(250m, grouped.IncomeTotal);
        Assert.Equal(2, grouped.Rows.Count);
        Assert.All(grouped.Rows, row =>
        {
            Assert.Null(row.IncomeTypeId);
            Assert.Equal("ИТОГО", row.IncomeTypeName);
        });
        Assert.Single(capture.Commands);

        foreach (var descending in new[] { false, true })
        {
            foreach (var field in new[] { "accountingMonth", "garageNumber", "ownerName", "incomeTypeName", "accrualAmount", "incomeAmount", "difference" })
            {
                capture.Commands.Clear();
                var sortedPage = await query.GetRowsAsync(
                    month,
                    month,
                    null,
                    new HashSet<Guid>(),
                    new HashSet<Guid>(),
                    new HashSet<Guid>(),
                    false,
                    1,
                    1,
                    new ReportSort(field, descending),
                    CancellationToken.None);

                Assert.Equal(5, sortedPage.RowCount);
                Assert.Equal(1150m, sortedPage.AccrualTotal);
                Assert.Equal(250m, sortedPage.IncomeTotal);
                Assert.Single(sortedPage.Rows);
                Assert.Single(capture.Commands);
            }
        }

        capture.Commands.Clear();
        var garageOwnerSearch = await query.GetRowsAsync(
            month,
            month,
            firstGarageNumber.ToUpperInvariant(),
            new HashSet<Guid> { firstGarageId },
            new HashSet<Guid> { firstOwnerId },
            new HashSet<Guid>(),
            false,
            0,
            25,
            new ReportSort("accountingMonth", false),
            CancellationToken.None);

        Assert.Equal(3, garageOwnerSearch.RowCount);
        Assert.Equal(800m, garageOwnerSearch.AccrualTotal);
        Assert.Equal(150m, garageOwnerSearch.IncomeTotal);
        Assert.All(garageOwnerSearch.Rows, row => Assert.Equal(firstGarageNumber, row.GarageNumber));
        Assert.Single(capture.Commands);

        capture.Commands.Clear();
        var typeFiltered = await query.GetRowsAsync(
            month,
            month,
            null,
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid> { primaryIncomeTypeId },
            false,
            0,
            25,
            new ReportSort("garageNumber", false),
            CancellationToken.None);

        Assert.Equal(2, typeFiltered.RowCount);
        Assert.Equal(800m, typeFiltered.AccrualTotal);
        Assert.Equal(200m, typeFiltered.IncomeTotal);
        Assert.All(typeFiltered.Rows, row => Assert.Equal(primaryIncomeTypeId, row.IncomeTypeId));
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
            Source = "garage_report_query_integration",
            IsCanceled = isCanceled
        };

    private static FinancialOperation CreateIncome(
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
