using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlSupplierAccrualPageIntegrationTests
{
    [PostgreSqlFact]
    public async Task SupplierAccrualPageLoadsCountRowsAndRelatedNamesInOneCommandForEveryPageShape()
    {
        var builder = new AccountingTestDataBuilder();
        var group = builder.BuildSupplierGroup("Группа страницы начислений поставщикам 2047");
        var selectedSupplier = builder.BuildSupplier(group, "Поставщик страницы 2047");
        var otherSupplier = builder.BuildSupplier(group, "Другой поставщик страницы 2047");
        var targetExpenseType = builder.BuildExpenseType("Target услуга страницы 2047", "target_page_2047");
        var ordinaryExpenseType = builder.BuildExpenseType("Обычная услуга страницы 2047", "ordinary_page_2047");
        var expectedCreatedAt = new DateTimeOffset(2047, 2, 4, 9, 30, 0, TimeSpan.Zero);
        var expectedUpdatedAt = expectedCreatedAt.AddMinutes(45);
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.SupplierAccruals.AddRange(
                CreateAccrual(
                    selectedSupplier,
                    targetExpenseType,
                    new DateOnly(2047, 3, 1),
                    900m,
                    "REG-03",
                    "Обычная строка"),
                CreateAccrual(
                    selectedSupplier,
                    ordinaryExpenseType,
                    new DateOnly(2047, 2, 1),
                    725.50m,
                    "Target invoice 2047",
                    "Счёт поставщика",
                    createdAtUtc: expectedCreatedAt,
                    updatedAtUtc: expectedUpdatedAt),
                CreateAccrual(
                    selectedSupplier,
                    ordinaryExpenseType,
                    new DateOnly(2047, 1, 1),
                    500m,
                    "REG-01",
                    "Target комментарий 2047"),
                CreateAccrual(
                    otherSupplier,
                    ordinaryExpenseType,
                    new DateOnly(2047, 2, 1),
                    300m,
                    "Target other supplier",
                    "Исключается фильтром поставщика"),
                CreateAccrual(
                    selectedSupplier,
                    ordinaryExpenseType,
                    new DateOnly(2046, 12, 1),
                    200m,
                    "Target outside period",
                    "За пределами периода"),
                CreateAccrual(
                    selectedSupplier,
                    ordinaryExpenseType,
                    new DateOnly(2047, 4, 1),
                    100m,
                    "Target canceled",
                    "Отменённая строка",
                    isCanceled: true));
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var repository = new EfSupplierAccrualRepository(context);

        var page = await repository.GetPageAsync(
            new DateOnly(2047, 1, 1),
            new DateOnly(2047, 3, 1),
            "target",
            selectedSupplier.Id,
            1,
            2,
            CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(
            [new DateOnly(2047, 2, 1), new DateOnly(2047, 1, 1)],
            page.Items.Select(accrual => accrual.AccountingMonth));

        var documentAccrual = page.Items[0];
        Assert.Equal(selectedSupplier.Id, documentAccrual.SupplierId);
        Assert.Equal("Поставщик страницы 2047", documentAccrual.Supplier.Name);
        Assert.Equal(ordinaryExpenseType.Id, documentAccrual.ExpenseTypeId);
        Assert.Equal("Обычная услуга страницы 2047", documentAccrual.ExpenseType.Name);
        Assert.Equal(725.50m, documentAccrual.Amount);
        Assert.Equal("manual", documentAccrual.Source);
        Assert.Equal("Target invoice 2047", documentAccrual.DocumentNumber);
        Assert.Equal("Счёт поставщика", documentAccrual.Comment);
        Assert.False(documentAccrual.IsCanceled);
        Assert.Equal(expectedCreatedAt, documentAccrual.CreatedAtUtc);
        Assert.Equal(expectedUpdatedAt, documentAccrual.UpdatedAtUtc);

        var commentAccrual = page.Items[1];
        Assert.Equal("REG-01", commentAccrual.DocumentNumber);
        Assert.Equal("Target комментарий 2047", commentAccrual.Comment);
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var beyondEnd = await repository.GetPageAsync(
            new DateOnly(2047, 1, 1),
            new DateOnly(2047, 3, 1),
            "target",
            selectedSupplier.Id,
            20,
            5,
            CancellationToken.None);

        Assert.Equal(3, beyondEnd.TotalCount);
        Assert.Empty(beyondEnd.Items);
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var empty = await repository.GetPageAsync(null, null, "missing", null, 0, 5, CancellationToken.None);

        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Items);
        AssertSingleCombinedCommand(capture);
    }

    private static SupplierAccrual CreateAccrual(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly accountingMonth,
        decimal amount,
        string documentNumber,
        string comment,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? updatedAtUtc = null,
        bool isCanceled = false) =>
        new()
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = accountingMonth,
            Amount = amount,
            Source = "manual",
            DocumentNumber = documentNumber,
            Comment = comment,
            IsCanceled = isCanceled,
            CreatedAtUtc = createdAtUtc ?? accountingMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            UpdatedAtUtc = updatedAtUtc ?? accountingMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        };

    private static void AssertSingleCombinedCommand(ReaderCommandCapture capture)
    {
        var command = Assert.Single(capture.Commands);
        Assert.Contains("COUNT(*)", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supplier_accruals", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("suppliers", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expense_types", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", command, StringComparison.OrdinalIgnoreCase);
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
