using System.Data.Common;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class PostgreSqlReceiptPrintingQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task FindReceiptOperationsAsync_LoadsBatchWithOneBoundedCompactReadOnlyQuery()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var batchId = Guid.NewGuid();
        var owner = new Owner
        {
            LastName = "Иванов",
            FirstName = "Иван",
            MiddleName = "Иванович",
            Phone = "+7 900 000-00-00",
            Address = "Не должно загружаться",
            MeterNotes = "Не должны загружаться"
        };
        var garage = new Garage
        {
            Number = "КВ-1",
            PeopleCount = 4,
            FloorCount = 2,
            StartingBalance = 123m,
            Comment = "Не должен загружаться",
            Owner = owner
        };
        var firstIncomeType = new IncomeType
        {
            Name = "Квитанция вода тест",
            Code = "private_code"
        };
        var secondIncomeType = new IncomeType
        {
            Name = "Квитанция взнос тест",
            Code = "private_code_2"
        };
        var first = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 7, 10),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 500m,
            ReceiptBatchId = batchId,
            DocumentNumber = "PKO-2",
            Garage = garage,
            IncomeType = firstIncomeType,
            Comment = "Не должен загружаться"
        };
        var anchor = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 7, 10),
            AccountingMonth = new DateOnly(2026, 8, 1),
            Amount = 1500m,
            ReceiptBatchId = batchId,
            DocumentNumber = "PKO-1",
            Garage = garage,
            IncomeType = secondIncomeType
        };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(first, anchor);
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfReceiptPrintingRepository(queryContext);

        var result = await repository.FindReceiptOperationsAsync(anchor.Id, CancellationToken.None);

        Assert.Equal([first.Id, anchor.Id], result.Select(item => item.Id));
        Assert.Equal(["Квитанция вода тест", "Квитанция взнос тест"], result.Select(item => item.IncomeType!.Name));
        Assert.All(result, item =>
        {
            Assert.Equal("КВ-1", item.Garage!.Number);
            Assert.Equal("Иванов Иван Иванович", item.Garage.Owner!.FullName);
            Assert.Equal(batchId, item.ReceiptBatchId);
        });
        Assert.Empty(queryContext.ChangeTracker.Entries());

        var command = Assert.Single(capture.Commands);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReceiptBatchId", command, StringComparison.Ordinal);
        Assert.Contains("LastName", command, StringComparison.Ordinal);
        Assert.Contains("income_types", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Phone", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Address", command, StringComparison.Ordinal);
        Assert.DoesNotContain("MeterNotes", command, StringComparison.Ordinal);
        Assert.DoesNotContain("PeopleCount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("FloorCount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("StartingBalance", command, StringComparison.Ordinal);
        Assert.DoesNotContain("CounterpartyName", command, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpensePaymentType", command, StringComparison.Ordinal);
        Assert.DoesNotContain("NegativeFundBalanceConfirmed", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Comment", command, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task FindReceiptOperationsAsync_ReturnsOnlyUnbatchedAnchorAndEmptyForUnknownId()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var anchor = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = new DateOnly(2026, 8, 10),
            AccountingMonth = new DateOnly(2026, 8, 1),
            Amount = 100m,
            IsCanceled = true
        };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.Add(anchor);
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfReceiptPrintingRepository(queryContext);

        var single = await repository.FindReceiptOperationsAsync(anchor.Id, CancellationToken.None);
        var operation = Assert.Single(single);
        Assert.Equal(anchor.Id, operation.Id);
        Assert.Equal(FinancialOperationKinds.Expense, operation.OperationKind);
        Assert.True(operation.IsCanceled);
        Assert.Null(operation.ReceiptBatchId);
        Assert.Single(capture.Commands);

        capture.Commands.Clear();
        var missing = await repository.FindReceiptOperationsAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Empty(missing);
        Assert.Single(capture.Commands);
        Assert.Empty(queryContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task FindReceiptOperationsAsync_PropagatesCancellationBeforeDatabaseRead()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfReceiptPrintingRepository(queryContext);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.FindReceiptOperationsAsync(Guid.NewGuid(), cancellation.Token));

        Assert.Empty(capture.Commands);
        Assert.Empty(queryContext.ChangeTracker.Entries());
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
