using System.Data.Common;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Funds;

public sealed class PostgreSqlFundTotalsIntegrationTests
{
    [PostgreSqlFact]
    public async Task GetTotalsAsync_KeepsFinancialTotalsWhenFundCatalogIsEmptyAndUsesOneUnionQuery()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateContext())
        {
            setupContext.FinancialOperations.AddRange(
                CreateOperation(FinancialOperationKinds.Income, 125m),
                CreateOperation(FinancialOperationKinds.Expense, 40m),
                CreateOperation(FinancialOperationKinds.Income, 999m, isCanceled: true));
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);

        var totals = await new EfFundRepository(queryContext).GetTotalsAsync(CancellationToken.None);

        Assert.Equal(125m, totals.IncomeTotal);
        Assert.Equal(40m, totals.ExpenseTotal);
        Assert.Equal(0m, totals.AllocatedFundTotal);
        var command = Assert.Single(capture.Commands);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    public async Task GetTotalsAsync_KeepsAllocatedBalanceWhenFinancialHistoryIsEmpty()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateContext())
        {
            setupContext.Funds.Add(new Fund
            {
                Name = "Резервный фонд",
                NormalizedName = "РЕЗЕРВНЫЙ ФОНД",
                Balance = 300m
            });
            await setupContext.SaveChangesAsync();
        }

        await using var queryContext = database.CreateContext();
        var totals = await new EfFundRepository(queryContext).GetTotalsAsync(CancellationToken.None);

        Assert.Equal(0m, totals.IncomeTotal);
        Assert.Equal(0m, totals.ExpenseTotal);
        Assert.Equal(300m, totals.AllocatedFundTotal);
    }

    private static FinancialOperation CreateOperation(string kind, decimal amount, bool isCanceled = false) =>
        new()
        {
            OperationKind = kind,
            OperationDate = new DateOnly(2026, 7, 20),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = amount,
            IsCanceled = isCanceled
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
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
