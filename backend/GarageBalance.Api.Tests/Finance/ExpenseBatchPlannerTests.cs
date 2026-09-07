using GarageBalance.Api.Application.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class ExpenseBatchPlannerTests
{
    [Fact]
    public void Build_ReconcilesSalaryAgainstClosingDebtAndRequiresCompleteMonthlyCoverage()
    {
        var staff = Guid.NewGuid();
        var type = Guid.NewGuid();
        ExpenseBatchDebt[] debts = [new("staff", staff, type, null, Month.AddMonths(-1), 50m), new("staff", staff, type, null, Month, 70m)];
        var result = ExpenseBatchPlanner.Build(Month, debts, new Dictionary<Guid, decimal> { [staff] = 60m });
        Assert.Equal(new[] { 50m, 10m }, result.Lines.Select(line => line.Amount));
        Assert.Equal(60m, result.CashAmount);
        Assert.Empty(ExpenseBatchPlanner.Build(Month, debts, new Dictionary<Guid, decimal> { [staff] = 0m }).Lines);
        foreach (var closing in new[] { -1m, 121m })
            Assert.Throws<ArgumentException>(() => ExpenseBatchPlanner.Build(Month, debts, new Dictionary<Guid, decimal> { [staff] = closing }));
        Assert.Throws<ArgumentException>(() => ExpenseBatchPlanner.Build(Month, debts, new Dictionary<Guid, decimal>()));
        Assert.Throws<ArgumentException>(() => ExpenseBatchPlanner.Build(Month, [], new Dictionary<Guid, decimal> { [staff] = 1m }));
        Assert.Throws<ArgumentException>(() => ExpenseBatchPlanner.Build(Month, [], new Dictionary<Guid, decimal> { [Guid.Empty] = 0m }));
    }

    private static readonly DateOnly Month = new(2026, 8, 1);
    private static readonly Guid Recipient = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ExpenseType = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Fund = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public void Build_CombinesBankSupplierDebtsAndCashSalaryMonths()
    {
        var result = ExpenseBatchPlanner.Build(Month,
        [
            Debt(25m),
            Debt(35m) with { RecipientId = Guid.NewGuid() },
            Debt(80m, "staff") with { AccountingMonth = Month.AddMonths(-1) },
            Debt(20m, "staff")
        ]);

        Assert.Equal(4, result.Lines.Count);
        Assert.Equal(60m, result.BankAmount);
        Assert.Equal(100m, result.CashAmount);
        Assert.Equal(60m, result.FundAmounts[Fund]);
        Assert.All(result.Lines.Where(item => item.RecipientKind == "supplier"), item => Assert.Equal("bank", item.PaymentSource));
        Assert.All(result.Lines.Where(item => item.RecipientKind == "staff"), item => Assert.Equal("cash", item.PaymentSource));
        Assert.Equal(new[] { Month.AddMonths(-1), Month }, result.Lines.Where(item => item.RecipientKind == "staff").Select(item => item.AccountingMonth));
    }

    [Fact]
    public void Build_OffsetsSalaryCreditsAndPaysOldestDebtFirst()
    {
        var result = ExpenseBatchPlanner.Build(Month,
        [
            Debt(70m, "staff"),
            Debt(-30m, "staff") with { AccountingMonth = Month.AddMonths(-1) },
            Debt(100m, "staff") with { AccountingMonth = Month.AddMonths(-2) }
        ]);

        Assert.Equal(140m, result.CashAmount);
        Assert.Equal(new[] { 100m, 40m }, result.Lines.Select(item => item.Amount));
        Assert.Empty(result.FundAmounts);
    }

    [Fact]
    public void Build_DoesNotOffsetDifferentRecipientsOrSupplierServices()
    {
        var result = ExpenseBatchPlanner.Build(Month,
        [
            Debt(-100m),
            Debt(40m) with { ExpenseTypeId = Guid.NewGuid() },
            Debt(-100m, "staff"),
            Debt(30m, "staff") with { RecipientId = Guid.NewGuid() }
        ]);

        Assert.Equal(40m, result.BankAmount);
        Assert.Equal(30m, result.CashAmount);
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public void Build_EmptySettledAndOverpaidRowsDoNotCreatePayments()
    {
        Assert.Empty(ExpenseBatchPlanner.Build(Month, []).Lines);
        var result = ExpenseBatchPlanner.Build(Month,
        [Debt(0), Debt(-10m, "staff"), Debt(5m, "staff") with { AccountingMonth = Month.AddMonths(-1) }]);
        Assert.Empty(result.Lines);
        Assert.Equal(0m, result.BankAmount + result.CashAmount);
    }

    [Fact]
    public void Build_RoundsEachDebtToKopecksAndAllowsUnassignedSupplierFund()
    {
        var result = ExpenseBatchPlanner.Build(Month, [Debt(1.005m) with { FundId = null }]);
        Assert.Equal(1.01m, Assert.Single(result.Lines).Amount);
        Assert.Equal(1.01m, result.BankAmount);
        Assert.Empty(result.FundAmounts);
    }

    [Fact]
    public void Build_IsIndependentOfInputOrder()
    {
        ExpenseBatchDebt[] debts = [Debt(50m), Debt(30m, "staff"), Debt(20m, "staff") with { AccountingMonth = Month.AddMonths(-1) }];
        Assert.Equal(ExpenseBatchPlanner.Build(Month, debts).Lines, ExpenseBatchPlanner.Build(Month, debts.Reverse().ToArray()).Lines);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("recipient")]
    [InlineData("expense-type")]
    [InlineData("fund")]
    [InlineData("future")]
    [InlineData("day")]
    [InlineData("supplier-past")]
    [InlineData("staff-fund")]
    [InlineData("duplicate")]
    public void Build_RejectsInvalidInputWithoutProducingPartialPlan(string scenario)
    {
        var valid = Debt(10m);
        var invalid = scenario switch
        {
            "kind" => valid with { RecipientKind = "episodic" },
            "recipient" => valid with { RecipientId = Guid.Empty },
            "expense-type" => valid with { ExpenseTypeId = Guid.Empty },
            "fund" => valid with { FundId = Guid.Empty },
            "future" => valid with { AccountingMonth = Month.AddMonths(1) },
            "day" => valid with { AccountingMonth = Month.AddDays(1) },
            "supplier-past" => valid with { AccountingMonth = Month.AddMonths(-1) },
            "staff-fund" => valid with { RecipientKind = "staff" },
            _ => valid
        };
        Assert.Throws<ArgumentException>(() => ExpenseBatchPlanner.Build(Month, [valid, invalid]));
    }

    [Fact]
    public void Build_RejectsInvalidMonthAndMissingInput()
    {
        Assert.Throws<ArgumentException>(() => ExpenseBatchPlanner.Build(Month.AddDays(1), []));
        Assert.Throws<ArgumentNullException>(() => ExpenseBatchPlanner.Build(Month, null!));
    }

    private static ExpenseBatchDebt Debt(decimal amount, string kind = "supplier") =>
        new(kind, Recipient, ExpenseType, kind == "supplier" ? Fund : null, Month, amount);
}
