using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Reports;

public sealed class IncomeReportDebtSortingTests
{
    [PostgreSqlFact]
    public async Task PostgreSqlSortsDisplayedDebtBeforePagination()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyDebtOrderAsync(context);
    }

    [Fact]
    public async Task SqliteFallbackSortsDisplayedDebtBeforePagination()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyDebtOrderAsync(database.Context);
    }

    private static async Task VerifyDebtOrderAsync(GarageBalanceDbContext context)
    {
        var month = new DateOnly(2044, 2, 1);
        var garage = new Garage { Number = "DEBT-1", StartingBalance = 1000m };
        var creditGarage = new Garage { Number = "DEBT-2", StartingBalance = 20m };
        var type = new IncomeType { Name = "Сортировка остатка" };
        var otherType = new IncomeType { Name = "Другой вид платежа" };
        var batch = Guid.NewGuid();
        FinancialOperation Payment(Garage target, IncomeType incomeType, int day, decimal amount, string document, Guid? receiptBatchId = null) => new()
        {
            Garage = target,
            IncomeType = incomeType,
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = month.AddDays(day),
            AccountingMonth = month,
            Amount = amount,
            CreatedAtUtc = new DateTimeOffset(2044, 2, 1, 0, 0, 0, TimeSpan.Zero).AddDays(day),
            DocumentNumber = document,
            ReceiptBatchId = receiptBatchId
        };
        var canceled = Payment(garage, type, 6, 999m, "canceled");
        canceled.IsCanceled = true;
        context.AddRange(
            Payment(garage, type, 1, 100m, "first"),
            Payment(garage, otherType, 2, 300m, "second"),
            Payment(garage, type, 3, 50m, "batch-a", batch),
            Payment(garage, type, 4, 50m, "batch-b", batch),
            Payment(creditGarage, type, 5, 50m, "credit"), canceled,
            new Accrual { Garage = garage, IncomeType = type, AccountingMonth = month.AddMonths(1), Amount = 900m, Source = AccrualSources.Manual },
            new Accrual { Garage = garage, IncomeType = type, AccountingMonth = month, Amount = 999m, IsCanceled = true, Source = AccrualSources.Manual });
        await context.SaveChangesAsync();
        var query = new EfIncomeReportQuery(context);
        foreach (var grouped in new[] { false, true })
            foreach (var filtered in new[] { false, true })
                foreach (var descending in new[] { false, true })
                {
                    var expected = new List<decimal> { 900m, 500m, -30m };
                    if (!grouped) expected.Add(550m);
                    if (!filtered) expected.Add(600m);
                    expected.Sort();
                    if (descending) expected.Reverse();
                    var typeIds = filtered ? new HashSet<Guid> { type.Id } : [];
                    var full = await query.GetRowsAsync(month, month.AddMonths(1).AddDays(-1), "payments", new HashSet<Guid>(), new HashSet<Guid>(), typeIds, null, 25, 0, new ReportSort("debt", descending), grouped, CancellationToken.None);
                    Assert.Equal(expected, full.Rows.Select(row => row.DebtAfterPayment!.Value));
                    Assert.Equal(filtered ? 250m : 550m, full.IncomeTotal);
                    for (var offset = 0; offset <= expected.Count; offset++)
                    {
                        var page = await query.GetRowsAsync(month, month.AddMonths(1).AddDays(-1), "payments", new HashSet<Guid>(), new HashSet<Guid>(), typeIds, null, 1, offset, new ReportSort("debt", descending), grouped, CancellationToken.None);
                        Assert.Equal(expected.Count, page.RowCount);
                        Assert.Equal(full.IncomeTotal, page.IncomeTotal);
                        Assert.Equal(expected.Skip(offset).Take(1), page.Rows.Select(row => row.DebtAfterPayment!.Value));
                    }
                }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetRowsAsync(month, month, "payments", new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), null, 1, 0, new ReportSort("debt", false), false, cancellation.Token));
    }
}
