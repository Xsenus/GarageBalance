using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlConsolidatedMonthlyReportQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task MonthlyReportBuildsSectionsAndBankBalancesFromOneBoundedCommand()
    {
        var january = new DateOnly(2043, 1, 1);
        var february = january.AddMonths(1);
        var december = january.AddMonths(-1);
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var incomeType = new IncomeType { Name = $"Monthly income {Guid.NewGuid():N}" };
            var expenseType = new ExpenseType { Name = $"Monthly expense {Guid.NewGuid():N}" };
            var cashExpenseType = new ExpenseType { Name = "Выплата без чека", Code = $"no_receipt_{Guid.NewGuid():N}" };
            var garage = new Garage { Number = "MONTHLY-01", PeopleCount = 1, FloorCount = 1, StartingBalance = 100m };
            var archivedGarage = new Garage { Number = "MONTHLY-ARCHIVED", PeopleCount = 1, FloorCount = 1, StartingBalance = 999m, IsArchived = true };
            seedContext.AddRange(incomeType, expenseType, cashExpenseType, garage, archivedGarage);
            var explicitBankExpense = CreateOperation(FinancialOperationKinds.Expense, december, 5m, null, null, cashExpenseType, "MONTH-BANK-OPENING");
            explicitBankExpense.ExpensePaymentSource = ExpensePaymentSources.Bank;
            explicitBankExpense.ExpensePaymentType = ExpensePaymentTypes.WithoutReceipt;
            var explicitCashExpense = CreateOperation(FinancialOperationKinds.Expense, december, 7m, null, null, expenseType, "MONTH-CASH-EXCLUDED");
            explicitCashExpense.ExpensePaymentSource = ExpensePaymentSources.Cash;
            var legacyCashExpense = CreateOperation(FinancialOperationKinds.Expense, december, 9m, null, null, cashExpenseType, "MONTH-LEGACY-CASH-EXCLUDED");
            var withoutReceiptExpense = CreateOperation(FinancialOperationKinds.Expense, december, 3m, null, null, expenseType, "MONTH-WITHOUT-RECEIPT-EXCLUDED");
            withoutReceiptExpense.ExpensePaymentType = ExpensePaymentTypes.WithoutReceipt;
            var canceledBankExpense = CreateOperation(FinancialOperationKinds.Expense, december, 11m, null, null, expenseType, "MONTH-CANCELED-BANK", true);
            canceledBankExpense.ExpensePaymentSource = ExpensePaymentSources.Bank;
            seedContext.FinancialOperations.AddRange(
                CreateOperation(FinancialOperationKinds.Income, january, 50m, garage, incomeType, null, "MONTH-IN-1"),
                CreateOperation(FinancialOperationKinds.Expense, january, 20m, null, null, expenseType, "MONTH-OUT-1"),
                CreateOperation(FinancialOperationKinds.Income, february, 30m, garage, incomeType, null, "MONTH-IN-2"),
                CreateOperation(FinancialOperationKinds.Expense, february, 10m, null, null, expenseType, "MONTH-OUT-2"),
                CreateOperation(FinancialOperationKinds.Income, january, 500m, garage, incomeType, null, "MONTH-CANCELED", true),
                explicitBankExpense,
                explicitCashExpense,
                legacyCashExpense,
                withoutReceiptExpense,
                canceledBankExpense);
            seedContext.CashBankTransfers.AddRange(
                new CashBankTransfer { TransferDate = december.AddDays(5), Amount = 100m },
                new CashBankTransfer { TransferDate = december.AddDays(6), Amount = 1_000m, IsCanceled = true });
            seedContext.Accruals.AddRange(
                CreateAccrual(garage, incomeType, january, 80m),
                CreateAccrual(garage, incomeType, february, 40m),
                CreateAccrual(garage, incomeType, january, 700m, true));
            seedContext.MeterReadings.AddRange(
                CreateReading(garage, january, MeterKinds.Water, 10m),
                CreateReading(garage, february, MeterKinds.Water, 20m),
                CreateReading(garage, february, MeterKinds.Electricity, 30m),
                CreateReading(garage, january, MeterKinds.Electricity, 40m, true));
            await seedContext.SaveChangesAsync();

            var capture = new ReaderCommandCapture();
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseNpgsql(database.ConnectionString)
                .AddInterceptors(capture)
                .Options;
            await using var context = new GarageBalanceDbContext(options);

            var result = await new EfConsolidatedMonthlyReportQuery(context).GetMonthlyDataAsync(
                january,
                february,
                new ReportSort("incomeTotal", true),
                0,
                1,
                CancellationToken.None);

            Assert.Equal(2, result.MonthlyRowCount);
            Assert.Equal(100m, result.GarageStartingBalanceTotal);
            Assert.Equal([new AmountCountByMonth(january, 50m, 1), new AmountCountByMonth(february, 30m, 1)], result.IncomeByMonth);
            Assert.Equal([new AmountCountByMonth(january, 20m, 1), new AmountCountByMonth(february, 10m, 1)], result.ExpenseByMonth);
            Assert.Equal([new AmountCountByMonth(january, 80m, 1), new AmountCountByMonth(february, 40m, 1)], result.AccrualByMonth);
            Assert.Equal([new CountByMonth(january, 1), new CountByMonth(february, 2)], result.MeterReadingsByMonth);
            Assert.Equal(80m, Assert.Single(result.IncomeBreakdown).Amount);
            Assert.Equal(30m, Assert.Single(result.ExpenseBreakdown).Amount);
            Assert.Equal([50m, 30m], result.IncomeBreakdownByMonth.Select(row => row.Amount));
            Assert.Equal([20m, 10m], result.ExpenseBreakdownByMonth.Select(row => row.Amount));
            var page = Assert.Single(result.MonthlyRows);
            Assert.Equal(january, page.AccountingMonth);
            Assert.Equal(50m, page.IncomeTotal);
            Assert.Equal(20m, page.ExpenseTotal);
            Assert.Equal(180m, page.AccrualTotal);
            Assert.Equal(30m, page.Balance);
            Assert.Equal(130m, page.Debt);
            Assert.Equal(2, page.OperationCount);
            Assert.Equal(2, page.AccrualCount);
            Assert.Equal(1, page.MeterReadingCount);
            Assert.Equal(95m, page.BankBalanceOpening);
            Assert.Equal(75m, page.BankBalanceClosing);

            var reportCommand = Assert.Single(capture.Commands);
            Assert.Contains("WITH operations AS MATERIALIZED", reportCommand, StringComparison.Ordinal);
            Assert.Contains("cash_bank_transfers", reportCommand, StringComparison.Ordinal);
            Assert.Equal(2, CountOccurrences(reportCommand, "FROM financial_operations"));
            Assert.Equal(1, CountOccurrences(reportCommand, "FROM accruals"));
            Assert.Equal(1, CountOccurrences(reportCommand, "FROM meter_readings"));
            Assert.Equal(1, CountOccurrences(reportCommand, "FROM garages"));
            Assert.Contains("FROM monthly_page", reportCommand, StringComparison.Ordinal);
            Assert.Contains("\"OperationDate\"", reportCommand, StringComparison.Ordinal);
            Assert.Contains("\"TransferDate\"", reportCommand, StringComparison.Ordinal);
            Assert.Contains("ExpensePaymentSource", reportCommand, StringComparison.Ordinal);
            Assert.Contains("ExpensePaymentType", reportCommand, StringComparison.Ordinal);
            Assert.Contains("= ANY", reportCommand, StringComparison.Ordinal);
            Assert.Contains("bank_balance_buckets AS", reportCommand, StringComparison.Ordinal);
            Assert.Contains("date_trunc('month', movement_date)", reportCommand, StringComparison.Ordinal);
            Assert.Contains("FROM bank_balance_buckets", reportCommand, StringComparison.Ordinal);

            capture.Commands.Clear();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new EfConsolidatedMonthlyReportQuery(context).GetMonthlyDataAsync(
                    january,
                    february,
                    new ReportSort("accountingMonth", false),
                    0,
                    null,
                    cancellation.Token));
            Assert.Empty(capture.Commands);
        }
    }

    private static FinancialOperation CreateOperation(
        string kind,
        DateOnly month,
        decimal amount,
        Garage? garage,
        IncomeType? incomeType,
        ExpenseType? expenseType,
        string documentNumber,
        bool isCanceled = false) =>
        new()
        {
            OperationKind = kind,
            OperationDate = month.AddDays(10),
            AccountingMonth = month,
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType,
            ExpenseType = expenseType,
            DocumentNumber = documentNumber,
            IsCanceled = isCanceled,
            CreatedAtUtc = new DateTimeOffset(month.AddDays(10).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)
        };

    private static Accrual CreateAccrual(Garage garage, IncomeType incomeType, DateOnly month, decimal amount, bool isCanceled = false) =>
        new()
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = month,
            DueDate = month.AddMonths(1).AddDays(-1),
            OverdueFromDate = month.AddMonths(1),
            Amount = amount,
            Source = "monthly_report_integration_test",
            IsCanceled = isCanceled
        };

    private static MeterReading CreateReading(
        Garage garage,
        DateOnly month,
        string kind,
        decimal currentValue,
        bool isCanceled = false) =>
        new()
        {
            Garage = garage,
            MeterKind = kind,
            AccountingMonth = month,
            ReadingDate = month.AddDays(20),
            CurrentValue = currentValue,
            PreviousValue = 0m,
            Consumption = currentValue,
            IsCanceled = isCanceled
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
