using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFinancialOperationListProjectionIntegrationTests
{
    [PostgreSqlFact]
    public async Task GetListAsync_UsesBoundedCompactProjectionForAllDisplayedRelations()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var owner = new Owner
        {
            LastName = "Иванов",
            FirstName = "Иван",
            MiddleName = "Иванович",
            Phone = "+7 900 000-00-00",
            Address = "Не должен загружаться",
            MeterNotes = "Не должны загружаться"
        };
        var garage = new Garage
        {
            Number = "COMPACT-31415",
            PeopleCount = 7,
            FloorCount = 3,
            StartingBalance = 1250m,
            Comment = "Не должен загружаться из гаража",
            Owner = owner
        };
        var incomeType = new IncomeType
        {
            Name = "Поступление компактного списка 31415",
            Code = "compact_list_income_31415"
        };
        var supplierGroup = new SupplierGroup { Name = "Группа компактного списка 31415" };
        var supplier = new Supplier
        {
            Name = "Поставщик компактного списка 31415",
            StartingBalance = 340m,
            Inn = "0000000000",
            LegalAddress = "Не должен загружаться",
            ContactPerson = "Не должен загружаться",
            Phone = "+7 900 111-22-33",
            Email = "private@example.test",
            Comment = "Не должен загружаться из поставщика",
            Group = supplierGroup,
            GroupId = supplierGroup.Id
        };
        var department = new StaffDepartment { Name = "Отдел компактного списка 31415" };
        var staffMember = new StaffMember
        {
            FullName = "Сотрудник компактного списка 31415",
            Rate = 9999m,
            Department = department,
            DepartmentId = department.Id
        };
        var expenseType = new ExpenseType
        {
            Name = "Расход компактного списка 31415",
            Code = "compact_list_expense_31415"
        };
        var expenseFund = new Fund
        {
            Name = "Фонд компактного списка 31415",
            NormalizedName = "фонд компактного списка 31415",
            Balance = 777m,
            SortOrder = 99
        };
        var expectedCreatedAt = new DateTimeOffset(2049, 5, 15, 10, 30, 0, TimeSpan.Zero);
        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = new DateOnly(2049, 5, 15),
            AccountingMonth = new DateOnly(2049, 5, 1),
            Amount = 875.25m,
            ReceiptBatchId = Guid.NewGuid(),
            ExpensePaymentType = ExpensePaymentTypes.WithReceipt,
            ExpensePaymentSource = ExpensePaymentSources.Bank,
            CounterpartyName = "Разовый получатель",
            NegativeFundBalanceConfirmed = true,
            DocumentNumber = "COMPACT-1",
            Comment = "Комментарий операции нужен",
            Garage = garage,
            IncomeType = incomeType,
            Supplier = supplier,
            StaffMember = staffMember,
            ExpenseType = expenseType,
            ExpenseFund = expenseFund,
            CreatedAtUtc = expectedCreatedAt,
            UpdatedAtUtc = expectedCreatedAt.AddHours(1)
        };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.FinancialOperations.AddRange(
                operation,
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Expense,
                    OperationDate = new DateOnly(2049, 5, 14),
                    AccountingMonth = new DateOnly(2049, 5, 1),
                    Amount = 10m
                },
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Expense,
                    OperationDate = new DateOnly(2049, 5, 13),
                    AccountingMonth = new DateOnly(2049, 5, 1),
                    Amount = 20m
                });
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfFinancialOperationRepository(queryContext);

        var result = await repository.GetListAsync(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            2,
            CancellationToken.None);

        Assert.Equal(2, result.Count);
        var actual = result[0];
        Assert.Equal(operation.Id, actual.Id);
        Assert.Equal("COMPACT-31415", actual.Garage!.Number);
        Assert.Equal(1250m, actual.Garage.StartingBalance);
        Assert.Equal("Иванов Иван Иванович", actual.Garage.Owner!.FullName);
        Assert.Equal("Поступление компактного списка 31415", actual.IncomeType!.Name);
        Assert.Equal("Поставщик компактного списка 31415", actual.Supplier!.Name);
        Assert.Equal(340m, actual.Supplier.StartingBalance);
        Assert.Equal("Сотрудник компактного списка 31415", actual.StaffMember!.FullName);
        Assert.Equal("Отдел компактного списка 31415", actual.StaffMember.Department.Name);
        Assert.Equal("Расход компактного списка 31415", actual.ExpenseType!.Name);
        Assert.Equal("Фонд компактного списка 31415", actual.ExpenseFund!.Name);
        Assert.Equal(expectedCreatedAt, actual.CreatedAtUtc);
        Assert.Empty(queryContext.ChangeTracker.Entries());

        var command = Assert.Single(capture.Commands);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StartingBalance", command, StringComparison.Ordinal);
        Assert.Contains("CreatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("PeopleCount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("FloorCount", command, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialWaterMeterValue", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Phone", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Address", command, StringComparison.Ordinal);
        Assert.DoesNotContain("MeterNotes", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Inn", command, StringComparison.Ordinal);
        Assert.DoesNotContain("LegalAddress", command, StringComparison.Ordinal);
        Assert.DoesNotContain("ContactPerson", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Email", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Rate", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("SortOrder", command, StringComparison.Ordinal);
        Assert.Contains("\"Version\"", command, StringComparison.Ordinal);
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
        var repository = new EfFinancialOperationRepository(queryContext);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetListAsync(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            10,
            cancellation.Token));

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
