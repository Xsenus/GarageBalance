using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlSupplierAccrualListProjectionIntegrationTests
{
    [PostgreSqlFact]
    public async Task GetListAsync_UsesBoundedCompactProjectionForDisplayedSupplierAccrualData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var group = new SupplierGroup { Name = "Группа компактных начислений 16180" };
        var supplier = new Supplier
        {
            Name = "Поставщик компактных начислений 16180",
            Inn = "0000000000",
            LegalAddress = "Не должен загружаться",
            ContactPerson = "Не должен загружаться",
            Phone = "+7 900 000-00-00",
            Email = "private@example.test",
            StartingBalance = 500m,
            Comment = "Не должен загружаться из поставщика",
            Group = group,
            GroupId = group.Id
        };
        var expenseType = new ExpenseType
        {
            Name = "Услуга компактных начислений 16180",
            Code = "compact_supplier_accrual_16180"
        };
        var fund = new Fund
        {
            Name = "Фонд компактных начислений 16180",
            NormalizedName = "фонд компактных начислений 16180",
            Balance = 777m,
            SortOrder = 99
        };
        var accrual = new SupplierAccrual
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            ExpenseFund = fund,
            AccountingMonth = new DateOnly(2051, 3, 1),
            Amount = 1250.50m,
            Source = "manual",
            DocumentNumber = "COMPACT-SUPPLIER-%_-1",
            Comment = "Комментарий начисления отображается"
        };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.SupplierAccruals.AddRange(
                accrual,
                new SupplierAccrual
                {
                    Supplier = new Supplier
                    {
                        Name = "Второй поставщик компактных начислений 16180",
                        Group = group,
                        GroupId = group.Id
                    },
                    ExpenseType = new ExpenseType
                    {
                        Name = "Вторая услуга компактных начислений 16180",
                        Code = "compact_supplier_accrual_second_16180"
                    },
                    AccountingMonth = new DateOnly(2051, 2, 1),
                    Amount = 10m,
                    Source = "manual"
                });
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfSupplierAccrualRepository(queryContext);

        var result = await repository.GetListAsync(null, null, null, null, 1, CancellationToken.None);

        var actual = Assert.Single(result);
        Assert.Equal(accrual.Id, actual.Id);
        Assert.Equal("Поставщик компактных начислений 16180", actual.Supplier.Name);
        Assert.Equal("Услуга компактных начислений 16180", actual.ExpenseType.Name);
        Assert.Equal("Фонд компактных начислений 16180", actual.ExpenseFund!.Name);
        Assert.Equal(new DateOnly(2051, 3, 1), actual.AccountingMonth);
        Assert.Equal(1250.50m, actual.Amount);
        Assert.Equal("manual", actual.Source);
        Assert.Equal("COMPACT-SUPPLIER-%_-1", actual.DocumentNumber);
        Assert.Equal("Комментарий начисления отображается", actual.Comment);
        Assert.False(actual.IsCanceled);
        Assert.Empty(queryContext.ChangeTracker.Entries());

        var command = Assert.Single(capture.Commands);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Inn", command, StringComparison.Ordinal);
        Assert.DoesNotContain("LegalAddress", command, StringComparison.Ordinal);
        Assert.DoesNotContain("ContactPerson", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Phone", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Email", command, StringComparison.Ordinal);
        Assert.DoesNotContain("StartingBalance", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Code", command, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizedName", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Balance", command, StringComparison.Ordinal);
        Assert.DoesNotContain("SortOrder", command, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceFinancialOperationId", command, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Version", command, StringComparison.Ordinal);

        capture.Commands.Clear();
        var literalSearch = await repository.GetListAsync(
            new DateOnly(2051, 3, 1),
            new DateOnly(2051, 3, 1),
            "%_",
            supplier.Id,
            10,
            CancellationToken.None);

        Assert.Equal(accrual.Id, Assert.Single(literalSearch).Id);
        var searchCommand = Assert.Single(capture.Commands);
        Assert.Contains("ILIKE", searchCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE '\\'", searchCommand, StringComparison.Ordinal);
        Assert.Contains("LIMIT", searchCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Inn", searchCommand, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task GetListAsync_PropagatesCancellationBeforeDatabaseRead()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfSupplierAccrualRepository(queryContext);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.GetListAsync(null, null, null, null, 10, cancellation.Token));

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
