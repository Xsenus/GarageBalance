using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlSupplierListProjectionIntegrationTests
{
    [PostgreSqlFact]
    public async Task GetListAsync_UsesOneBoundedCompactProjectionWithDebtForDisplayedSupplierData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var group = new SupplierGroup
        {
            Name = "Коммунальные услуги",
            IsSystem = true,
            CreatedAtUtc = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
            UpdatedAtUtc = DateTimeOffset.Parse("2025-02-01T00:00:00Z")
        };
        var service = new ChargeServiceSetting
        {
            Name = "Водоснабжение",
            IsRegular = true,
            PeriodicityMonths = 12,
            PaymentDueDay = 25,
            OverdueGraceDays = 30
        };
        var expenseType = new ExpenseType
        {
            Name = "Коммунальные расходы",
            Code = "supplier_service",
            IsSystem = true
        };
        var fund = new Fund
        {
            Name = "Водоснабжение",
            NormalizedName = "водоснабжение",
            Balance = 9876.54m,
            SortOrder = 5,
            AllowOperations = false,
            IsSystem = false
        };
        var firstSupplier = new Supplier
        {
            Name = "Альфа %_ 88423",
            Group = group,
            Inn = "7700000001",
            LegalAddress = "Отображаемый адрес",
            ContactPerson = "Анна Петрова",
            Phone = "+7 900 111-22-33",
            Email = "supplier@example.test",
            StartingBalance = 100m,
            Comment = "Отображаемый комментарий",
            ChargeServiceSetting = service,
            ExpenseType = expenseType,
            ExpenseFund = fund
        };
        var secondSupplier = new Supplier
        {
            Name = "Бета 88423",
            Group = group,
            StartingBalance = 500m
        };
        var excludedByLimitSupplier = new Supplier
        {
            Name = "Вега 88423",
            Group = group,
            StartingBalance = 700m
        };
        var archivedSupplier = new Supplier
        {
            Name = "Архивный 88423",
            Group = group,
            StartingBalance = 900m,
            IsArchived = true
        };
        var accrual = new SupplierAccrual
        {
            Supplier = firstSupplier,
            ExpenseType = expenseType,
            AccountingMonth = new DateOnly(2026, 8, 1),
            Amount = 900m,
            Source = "manual"
        };
        var payment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            Supplier = firstSupplier,
            ExpenseType = expenseType,
            OperationDate = new DateOnly(2026, 8, 20),
            AccountingMonth = new DateOnly(2026, 8, 1),
            Amount = 250m
        };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(
                group,
                service,
                expenseType,
                fund,
                firstSupplier,
                secondSupplier,
                excludedByLimitSupplier,
                archivedSupplier,
                accrual,
                payment);
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfSupplierRepository(queryContext);

        var result = await repository.GetListAsync(null, null, false, 2, CancellationToken.None);

        Assert.Equal([firstSupplier.Id, secondSupplier.Id], result.Items.Select(supplier => supplier.Id));
        var actual = result.Items[0];
        Assert.Equal("Альфа %_ 88423", actual.Name);
        Assert.Equal(group.Id, actual.GroupId);
        Assert.Equal("Коммунальные услуги", actual.Group.Name);
        Assert.Equal("7700000001", actual.Inn);
        Assert.Equal("Отображаемый адрес", actual.LegalAddress);
        Assert.Equal("Анна Петрова", actual.ContactPerson);
        Assert.Equal("+7 900 111-22-33", actual.Phone);
        Assert.Equal("supplier@example.test", actual.Email);
        Assert.Equal(100m, actual.StartingBalance);
        Assert.Equal("Отображаемый комментарий", actual.Comment);
        Assert.False(actual.IsArchived);
        Assert.Equal(service.Id, actual.ChargeServiceSettingId);
        Assert.Equal("Водоснабжение", actual.ChargeServiceSetting!.Name);
        Assert.Equal(expenseType.Id, actual.ExpenseTypeId);
        Assert.Equal("Коммунальные расходы", actual.ExpenseType!.Name);
        Assert.Equal(fund.Id, actual.ExpenseFundId);
        Assert.Equal("Водоснабжение", actual.ExpenseFund!.Name);
        Assert.Equal(9876.54m, actual.ExpenseFund.Balance);
        Assert.Equal(750m, result.DebtTotals[firstSupplier.Id]);
        Assert.Equal(500m, result.DebtTotals[secondSupplier.Id]);
        Assert.Empty(queryContext.ChangeTracker.Entries());

        var command = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supplier_accruals", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("financial_operations", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("PeriodicityMonths", command, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentDueDay", command, StringComparison.Ordinal);
        Assert.DoesNotContain("OverdueGraceDays", command, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizedName", command, StringComparison.Ordinal);
        Assert.DoesNotContain("SortOrder", command, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowOperations", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Code", command, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSystem", command, StringComparison.Ordinal);

        var literalSearch = await repository.GetListAsync(group.Id, "%_", true, 10, CancellationToken.None);

        Assert.Equal(firstSupplier.Id, Assert.Single(literalSearch.Items).Id);
        Assert.Equal(750m, literalSearch.DebtTotals[firstSupplier.Id]);
        var searchCommand = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("ILIKE", searchCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE '\\'", searchCommand, StringComparison.Ordinal);
        Assert.Contains("LIMIT", searchCommand, StringComparison.OrdinalIgnoreCase);
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
        var repository = new EfSupplierRepository(queryContext);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.GetListAsync(null, null, false, 10, cancellation.Token));

        Assert.Empty(capture.TakeCommandsAndClear());
        Assert.Empty(queryContext.ChangeTracker.Entries());
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        private readonly List<string> commands = [];

        public IReadOnlyList<string> TakeCommandsAndClear()
        {
            var result = commands.ToArray();
            commands.Clear();
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
