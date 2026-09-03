using System.Data.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFinancialJournalIntegrationTests
{
    [PostgreSqlFact]
    public async Task JournalFiltersCyrillicOnPostgreSqlAndKeepsEverySourceQueryBounded()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var owner = new Owner { LastName = "Тестов", FirstName = "Журнал" };
            var garage = new Garage { Number = "J-103", Owner = owner, PeopleCount = 1, FloorCount = 1 };
            var incomeType = new IncomeType { Name = "Контроль журнала", Code = $"journal_{Guid.NewGuid():N}" };
            seedContext.Add(new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 9, 15),
                AccountingMonth = new DateOnly(2026, 9, 1),
                Amount = 500m,
                Garage = garage,
                IncomeType = incomeType,
                DocumentNumber = "ПКО-ЖУРНАЛ",
                CreatedAtUtc = new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero)
            });
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var query = new EfFinancialJournalQuery(context);

        var filtered = await query.GetPageAsync(
            new FinancialJournalRequest(
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 30),
                "financial_operation",
                "тестов",
                "active",
                "пко",
                0,
                25),
            CancellationToken.None);

        var item = Assert.Single(filtered.Items);
        Assert.Equal("ПКО-ЖУРНАЛ", item.DocumentNumber);
        Assert.Equal(2, capture.Take().Count);

        var all = await query.GetPageAsync(
            new FinancialJournalRequest(null, null, null, null, null, null, 0, 25),
            CancellationToken.None);
        Assert.Equal(1, all.TotalCount);
        var commands = capture.Take();
        Assert.Equal(14, commands.Count);
        Assert.Equal(7, commands.Count(command => command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class ReaderCommandCapture : DbCommandInterceptor
    {
        private readonly List<string> commands = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public IReadOnlyList<string> Take()
        {
            var result = commands.ToArray();
            commands.Clear();
            return result;
        }
    }
}
