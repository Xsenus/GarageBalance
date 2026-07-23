using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlReportControlTotalsIntegrationTests
{
    [PostgreSqlFact]
    public async Task AllEightReportsMatchIndependentControlTotals()
    {
        var month = new DateOnly(2041, 4, 1);
        var dateTo = month.AddMonths(1).AddDays(-1);
        var marker = $"REPORT-CONTROL-{Guid.NewGuid():N}";
        var control = new ReportControlTotals(
            GarageAccruals: 1200m + 800m,
            GarageIncome: 700m + 600m,
            HistoricalGarageAccruals: 999m,
            HistoricalGarageIncome: 333m,
            SupplierAccruals: 900m,
            SupplierExpenses: 450m,
            FundDeposits: 1000m,
            FundWithdrawals: 250m,
            BankDeposits: 400m);

        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var owner = new Owner { LastName = marker, FirstName = "Control" };
        var firstGarage = new Garage { Number = $"{marker}-1", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var secondGarage = new Garage { Number = $"{marker}-2", PeopleCount = 1, FloorCount = 1 };
        var incomeType = new IncomeType { Name = $"{marker}-INCOME" };
        var supplierGroup = new SupplierGroup { Name = $"{marker}-GROUP" };
        var supplier = new Supplier { Name = $"{marker}-SUPPLIER", Group = supplierGroup };
        var expenseType = new ExpenseType { Name = $"{marker}-EXPENSE" };
        var fund = new Fund { Name = $"{marker}-FUND", NormalizedName = marker, SortOrder = 900 };
        context.AddRange(owner, firstGarage, secondGarage, incomeType, supplierGroup, supplier, expenseType, fund);

        context.Accruals.AddRange(
            CreateAccrual(firstGarage, incomeType, month, 1200m),
            CreateAccrual(secondGarage, incomeType, month, 800m),
            CreateAccrual(firstGarage, incomeType, month, 500m, isCanceled: true),
            CreateAccrual(firstGarage, incomeType, month.AddMonths(-1), 999m));
        context.SupplierAccruals.AddRange(
            CreateSupplierAccrual(supplier, expenseType, month, 900m),
            CreateSupplierAccrual(supplier, expenseType, month, 300m, isCanceled: true),
            CreateSupplierAccrual(supplier, expenseType, month.AddMonths(-1), 777m));
        context.FinancialOperations.AddRange(
            CreateIncome(firstGarage, incomeType, month, 700m, $"{marker}-PKO-1"),
            CreateIncome(secondGarage, incomeType, month, 600m, $"{marker}-PKO-2"),
            CreateIncome(firstGarage, incomeType, month, 100m, $"{marker}-PKO-CANCELED", isCanceled: true),
            CreateIncome(firstGarage, incomeType, month.AddMonths(-1), 333m, $"{marker}-PKO-OLD"),
            CreateExpense(supplier, expenseType, month, 450m, $"{marker}-RKO"),
            CreateExpense(supplier, expenseType, month, 200m, $"{marker}-RKO-CANCELED", isCanceled: true),
            CreateExpense(supplier, expenseType, month.AddMonths(-1), 555m, $"{marker}-RKO-OLD"));
        context.FundOperations.AddRange(
            CreateFundOperation(fund, month.AddDays(4), FundOperationKinds.Deposit, 1000m, 0m, 1000m, $"{marker}-DEPOSIT"),
            CreateFundOperation(fund, month.AddDays(5), FundOperationKinds.Withdraw, 250m, 1000m, 750m, $"{marker}-WITHDRAW"),
            CreateFundOperation(fund, month.AddDays(7), FundOperationKinds.Deposit, 600m, 1150m, 1750m, $"{marker}-CANCELED", isCanceled: true),
            CreateFundOperation(fund, month.AddMonths(-1), FundOperationKinds.Deposit, 888m, 0m, 888m, $"{marker}-OLD"));
        context.CashBankTransfers.Add(new CashBankTransfer
        {
            TransferDate = month.AddDays(6),
            Amount = 400m,
            Comment = $"{marker}-BANK",
            CreatedAtUtc = new DateTimeOffset(month.AddDays(6).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var consolidated = await new EfConsolidatedMonthlyReportQuery(context).GetMonthlyDataAsync(
            month, month, new ReportSort("accountingMonth", false), 0, 20, CancellationToken.None);
        Assert.Equal(control.GarageAccruals, consolidated.AccrualByMonth.Sum(row => row.Amount));
        Assert.Equal(control.GarageIncome, consolidated.IncomeByMonth.Sum(row => row.Amount));
        Assert.Equal(control.SupplierExpenses, consolidated.ExpenseByMonth.Sum(row => row.Amount));
        Assert.Equal(control.GarageIncome, Assert.Single(consolidated.IncomeBreakdownByMonth).Amount);
        Assert.Equal(control.SupplierExpenses, Assert.Single(consolidated.ExpenseBreakdownByMonth).Amount);
        var consolidatedMonth = Assert.Single(consolidated.MonthlyRows);
        Assert.Equal(control.CashBalance, consolidatedMonth.Balance);
        Assert.Equal(control.GarageDebt, consolidatedMonth.Debt);
        Assert.Equal(-555m, consolidatedMonth.BankBalanceOpening);
        Assert.Equal(-605m, consolidatedMonth.BankBalanceClosing);

        var garages = await new EfGarageReportQuery(context).GetRowsAsync(
            month, month, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid> { incomeType.Id },
            false, 0, 20, new ReportSort("garageNumber", false), CancellationToken.None);
        Assert.Equal(control.GarageAccruals, garages.AccrualTotal);
        Assert.Equal(control.GarageIncome, garages.IncomeTotal);
        Assert.Equal(control.GarageDebt, garages.AccrualTotal - garages.IncomeTotal);

        var income = await new EfIncomeReportQuery(context).GetRowsAsync(
            month, dateTo, "all", new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid> { incomeType.Id },
            null, 20, 0, new ReportSort("date", false), CancellationToken.None);
        Assert.Equal(control.GarageAccruals, income.AccrualTotal);
        Assert.Equal(control.GarageIncome, income.IncomeTotal);
        Assert.Equal(control.GarageDebt, income.AccrualTotal - income.IncomeTotal);

        var expenses = await new EfExpenseReportQuery(context).GetRowsAsync(
            month, dateTo, "all", new HashSet<Guid> { supplier.Id }, new HashSet<Guid>(), new HashSet<Guid> { expenseType.Id },
            null, 20, 0, new ReportSort("date", false), CancellationToken.None);
        Assert.Equal(control.SupplierAccruals, expenses.AccrualTotal);
        Assert.Equal(control.SupplierExpenses, expenses.ExpenseTotal);
        Assert.Equal(control.SupplierDebt, expenses.AccrualTotal - expenses.ExpenseTotal);

        var cash = await new EfCashMovementReportQuery(context).GetCashPaymentsAsync(
            month, dateTo, marker, 0, 20, new ReportSort("date", false), CancellationToken.None);
        Assert.Equal(control.SupplierExpenses, cash.Total);

        var bank = await new EfCashMovementReportQuery(context).GetBankDepositsAsync(
            month, dateTo, marker, 0, 20, new ReportSort("date", false), CancellationToken.None);
        Assert.Equal(control.BankDeposits, bank.Total);

        var fundChanges = await new EfFundChangeReportQuery(context).GetFundChangesAsync(
            month, dateTo, marker, 0, 20, new ReportSort("date", false), CancellationToken.None);
        Assert.Equal(control.FundDeposits, fundChanges.DepositTotal);
        Assert.Equal(control.FundWithdrawals, fundChanges.WithdrawalTotal);
        Assert.Equal(control.FundNetChange, fundChanges.DepositTotal - fundChanges.WithdrawalTotal);

        var fees = await new EfFeeReportQuery(context).GetFeeReportPageAsync(
            [incomeType.Id], false, new ReportSort("garageNumber", false), 0, 20, CancellationToken.None);
        Assert.Equal(control.FeeAccruals, fees.AccrualTotals[incomeType.Id]);
        Assert.Equal(control.FeeIncome, fees.CollectedTotals[incomeType.Id]);
        Assert.Equal(control.FeeDebt, fees.DebtTotal);
    }

    private static Accrual CreateAccrual(Garage garage, IncomeType incomeType, DateOnly month, decimal amount, bool isCanceled = false) =>
        new()
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = month,
            DueDate = month.AddMonths(1).AddDays(-1),
            OverdueFromDate = month.AddMonths(1),
            Amount = amount,
            Source = "report_control_test",
            IsCanceled = isCanceled
        };

    private static SupplierAccrual CreateSupplierAccrual(Supplier supplier, ExpenseType expenseType, DateOnly month, decimal amount, bool isCanceled = false) =>
        new()
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = month,
            Amount = amount,
            Source = "report_control_test",
            IsCanceled = isCanceled
        };

    private static FinancialOperation CreateIncome(Garage garage, IncomeType incomeType, DateOnly month, decimal amount, string documentNumber, bool isCanceled = false) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = month.AddDays(9),
            AccountingMonth = month,
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType,
            DocumentNumber = documentNumber,
            IsCanceled = isCanceled,
            CreatedAtUtc = new DateTimeOffset(month.AddDays(9).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)
        };

    private static FinancialOperation CreateExpense(Supplier supplier, ExpenseType expenseType, DateOnly month, decimal amount, string documentNumber, bool isCanceled = false) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = month.AddDays(10),
            AccountingMonth = month,
            Amount = amount,
            Supplier = supplier,
            ExpenseType = expenseType,
            DocumentNumber = documentNumber,
            IsCanceled = isCanceled,
            CreatedAtUtc = new DateTimeOffset(month.AddDays(10).ToDateTime(new TimeOnly(11, 0)), TimeSpan.Zero)
        };

    private static FundOperation CreateFundOperation(
        Fund fund,
        DateOnly date,
        string operationKind,
        decimal amount,
        decimal balanceBefore,
        decimal balanceAfter,
        string reason,
        bool isCanceled = false) =>
        new()
        {
            Fund = fund,
            OperationKind = operationKind,
            Amount = amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            Reason = reason,
            IsCanceled = isCanceled,
            CreatedAtUtc = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)
        };

    private sealed record ReportControlTotals(
        decimal GarageAccruals,
        decimal GarageIncome,
        decimal HistoricalGarageAccruals,
        decimal HistoricalGarageIncome,
        decimal SupplierAccruals,
        decimal SupplierExpenses,
        decimal FundDeposits,
        decimal FundWithdrawals,
        decimal BankDeposits)
    {
        public decimal GarageDebt => GarageAccruals - GarageIncome;

        public decimal SupplierDebt => SupplierAccruals - SupplierExpenses;

        public decimal CashBalance => GarageIncome - SupplierExpenses;

        public decimal FundNetChange => FundDeposits - FundWithdrawals;

        public decimal FeeAccruals => GarageAccruals + HistoricalGarageAccruals;

        public decimal FeeIncome => GarageIncome + HistoricalGarageIncome;

        public decimal FeeDebt => FeeAccruals - FeeIncome;
    }
}
