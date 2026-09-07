using System.Reflection;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Tests.Finance;

public sealed class ExpenseBatchPreviewServiceTests
{
    [Fact]
    public async Task PreviewAsync_PaysOnlyVisibleClosingDebtAndExcludesSettledStaff()
    {
        var settled = Guid.NewGuid();
        var query = new StaffQuery
        {
            Debts = [
            new("staff", Staff, Type, null, Month, 63000m),
            new("staff", settled, Type, null, Month, 30000m)]
        };
        var result = await Service(Sheet([
            StaffRow() with { ClosingDebt = 18000m },
            StaffRow() with { StaffMemberId = settled, ClosingDebt = 0m }
        ]) with
        { CashAmount = 18000m }, query).PreviewAsync(new(Month, Month), CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(result.Value!.CanSubmit);
        var line = Assert.Single(result.Value.Items).Payment;
        Assert.Equal(Staff, line.RecipientId);
        Assert.Equal(18000m, line.Amount);
        Assert.Equal(18000m, result.Value.CashAmount);
    }

    [Fact]
    public async Task PreviewAsync_RejectsMissingMonthlyCoverageInsteadOfPartiallyPayingVisibleDebt()
    {
        var result = await Service(Sheet([StaffRow()]), new StaffQuery { Debts = [new("staff", Staff, Type, null, Month, 20m)] })
            .PreviewAsync(new(Month, Month), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("expense_batch_data_changed", result.ErrorCode);
    }

    private static readonly DateOnly Month = new(2026, 8, 1);
    private static readonly Guid Supplier = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Staff = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Type = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Fund = Guid.Parse("00000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task PreviewAsync_ListsRecipientsAndMonthsAndWarnsAboutFundShortfall()
    {
        var staffQuery = new StaffQuery { Debts = [new("staff", Staff, Type, null, Month.AddMonths(-1), 70m)] };
        var service = Service(Sheet([SupplierRow(), StaffRow()]), staffQuery);
        var result = await service.PreviewAsync(new(Month, Month.AddDays(10)), CancellationToken.None);
        var preview = Assert.IsType<ExpenseBatchPreviewDto>(result.Value);
        Assert.True(preview.CanSubmit);
        Assert.Equal(30m, preview.BankAmount);
        Assert.Equal(70m, preview.CashAmount);
        Assert.Equal("Поставщик", preview.Items[0].RecipientName);
        Assert.Equal("Сотрудник", preview.Items[1].RecipientName);
        Assert.Equal(Month.AddMonths(-1), preview.Items[1].Payment.AccountingMonth);
        Assert.True(preview.RequiresNegativeFundConfirmation);
        Assert.Equal(20m, Assert.Single(preview.Funds).AvailableAmount);
        Assert.Equal(64, preview.Fingerprint.Length);
        Assert.Equal(Month, staffQuery.LastAccrualMonth);
    }

    [Theory]
    [InlineData(20, 100, "банковском")]
    [InlineData(100, 20, "кассе")]
    public async Task PreviewAsync_StopsSubmissionWhenSourceCannotCoverAllPayments(int bank, int cash, string message)
    {
        var staff = new StaffQuery { Debts = [new("staff", Staff, Type, null, Month, 70m)] };
        var result = await Service(Sheet([SupplierRow(), StaffRow()]) with { BankAmount = bank, CashAmount = cash }, staff)
            .PreviewAsync(new(Month, Month), CancellationToken.None);
        Assert.False(result.Value!.CanSubmit);
        Assert.Contains(message, Assert.Single(result.Value.Issues));
    }

    [Fact]
    public async Task PreviewAsync_AggregatesSharedFundAndDoesNotRequireConfirmationAtExactBalance()
    {
        var rows = new[] { SupplierRow() with { Difference = 60m }, SupplierRow() with { SupplierId = Guid.NewGuid(), Difference = 60m } };
        var result = await Service(Sheet(rows)).PreviewAsync(new(Month, Month), CancellationToken.None);
        Assert.Equal(60m, Assert.Single(result.Value!.Funds).Amount);
        Assert.False(result.Value.RequiresNegativeFundConfirmation);
    }

    [Fact]
    public async Task PreviewAsync_EmptyOrPaidSheetCannotSubmitAndDoesNotFetchSalarySettings()
    {
        var staff = new StaffQuery();
        var result = await Service(Sheet([SupplierRow() with { ClosingDebt = 0 }]), staff, failSettings: true)
            .PreviewAsync(new(Month, Month), CancellationToken.None);
        Assert.False(result.Value!.CanSubmit);
        Assert.Empty(result.Value.Items);
        Assert.False(staff.Called);
        Assert.Empty(result.Value.Issues);
    }

    [Fact]
    public async Task PreviewAsync_UnusedNegativeAccountDoesNotBlockOtherSource()
    {
        var result = await Service(Sheet([SupplierRow() with { CounterpartyName = null }]) with { CashAmount = -10 })
            .PreviewAsync(new(Month, Month), CancellationToken.None);
        Assert.True(result.Value!.CanSubmit);
        Assert.Equal("Получатель", Assert.Single(result.Value.Items).RecipientName);
    }

    [Fact]
    public async Task PreviewAsync_BlocksSupplierWithoutConfiguredFund()
    {
        var result = await Service(Sheet([SupplierRow() with { ExpenseFundId = null }]))
            .PreviewAsync(new(Month, Month), CancellationToken.None);
        Assert.False(result.Value!.CanSubmit);
        Assert.Contains("фонд расходования", Assert.Single(result.Value.Issues));
        Assert.Empty(result.Value.Funds);
    }

    [Fact]
    public async Task PreviewAsync_UsesPriorMonthBeforeSalaryDayAndNeverAccruesFutureSalary()
    {
        var staff = new StaffQuery();
        var service = Service(Sheet([StaffRow()]), staff, salaryDay: 20);
        await service.PreviewAsync(new(Month.AddMonths(2), Month), CancellationToken.None);
        Assert.Equal(Month.AddMonths(-1), staff.LastAccrualMonth);
        Assert.Equal(Month.AddMonths(2), staff.MonthTo);
    }

    [Fact]
    public async Task PreviewAsync_FingerprintIsStableButChangesWithDateDebtOrAvailableMoney()
    {
        var sheet = Sheet([SupplierRow()]);
        var first = (await Service(sheet).PreviewAsync(new(Month, Month), CancellationToken.None)).Value!;
        var repeated = (await Service(sheet).PreviewAsync(new(Month, Month), CancellationToken.None)).Value!;
        Assert.Equal(first.Fingerprint, repeated.Fingerprint);
        var changedDate = (await Service(sheet).PreviewAsync(new(Month, Month.AddDays(1)), CancellationToken.None)).Value!;
        var changedDebt = (await Service(Sheet([SupplierRow() with { ClosingDebt = 31m }])).PreviewAsync(new(Month, Month), CancellationToken.None)).Value!;
        var changedBank = (await Service(sheet with { BankAmount = 90m }).PreviewAsync(new(Month, Month), CancellationToken.None)).Value!;
        Assert.All(new[] { changedDate, changedDebt, changedBank }, value => Assert.NotEqual(first.Fingerprint, value.Fingerprint));
    }

    [Fact]
    public async Task PreviewAsync_RejectsInvalidPeriodAndPreservesUpstreamFailure()
    {
        var service = Service(Sheet([]));
        foreach (var request in new[] { new ExpenseBatchPreviewRequest(Month.AddMonths(-1), Month), new(Month.AddDays(1), Month), new(Month, DateOnly.MinValue) })
        {
            Assert.Equal("expense_batch_period_invalid", (await service.PreviewAsync(request, CancellationToken.None)).ErrorCode);
        }
        var failed = Service(Sheet([]), worksheetFailure: true);
        Assert.Equal("worksheet_unavailable", (await failed.PreviewAsync(new(Month, Month), CancellationToken.None)).ErrorCode);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PreviewAsync(new(Month, Month), new CancellationToken(true)));
    }

    [Fact]
    public async Task PreviewAsync_RejectsMalformedDuplicateOrStaleRows()
    {
        foreach (var rows in new[] { new[] { SupplierRow() with { SupplierId = null } }, new[] { SupplierRow(), SupplierRow() }, new[] { StaffRow(), StaffRow() } })
        {
            Assert.Equal("expense_batch_data_changed", (await Service(Sheet(rows)).PreviewAsync(new(Month, Month), CancellationToken.None)).ErrorCode);
        }
        var changedStaff = new StaffQuery { Debts = [new("staff", Guid.NewGuid(), Type, null, Month, 10m)] };
        Assert.Equal("expense_batch_data_changed", (await Service(Sheet([StaffRow()]), changedStaff).PreviewAsync(new(Month, Month), CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task PreviewAsync_MapsPreparationErrorsButDoesNotSwallowCancellationOrUnexpectedFailure()
    {
        var staff = new StaffQuery { Error = new ExpenseBatchPreparationException("salary_expense_type_not_found", "Статья недоступна") };
        var result = await Service(Sheet([StaffRow()]), staff).PreviewAsync(new(Month, Month), CancellationToken.None);
        Assert.Equal("salary_expense_type_not_found", result.ErrorCode);
        Assert.Equal("Статья недоступна", result.ErrorMessage);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(Sheet([StaffRow()]),
            new StaffQuery { Error = new OperationCanceledException() }).PreviewAsync(new(Month, Month), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(Sheet([StaffRow()]),
            new StaffQuery { Error = new InvalidOperationException("Unexpected") }).PreviewAsync(new(Month, Month), CancellationToken.None));
    }

    private static ExpenseWorksheetRowDto SupplierRow() => new("supplier", Supplier, null, "Поставщик", Type, "Услуга", 30, 0, 30, 20, 20)
    { ClosingDebt = 30, ExpenseFundId = Fund, ExpenseFundName = "Фонд" };
    private static ExpenseWorksheetRowDto StaffRow() => new("staff", null, Staff, "Сотрудник", Type, "Зарплата", 70, 0, 70, null, null)
    { ClosingDebt = 70 };
    private static ExpenseWorksheetDto Sheet(IReadOnlyList<ExpenseWorksheetRowDto> rows) => new(Month, 0, 0, 0, 0, 0, 100, 100, rows);

    private static ExpenseBatchPreviewService Service(ExpenseWorksheetDto sheet, StaffQuery? staff = null, int salaryDay = 1,
        bool failSettings = false, bool worksheetFailure = false)
    {
        var finance = DispatchProxy.Create<IFinanceService, TestProxy>();
        ((TestProxy)(object)finance).Handler = name => name == nameof(IFinanceService.GetExpenseWorksheetAsync)
            ? Task.FromResult(worksheetFailure ? FinanceResult<ExpenseWorksheetDto>.Failure("worksheet_unavailable", "Недоступно") : FinanceResult<ExpenseWorksheetDto>.Success(sheet))
            : throw new InvalidOperationException(name);
        var settings = DispatchProxy.Create<IApplicationSettingsService, TestProxy>();
        ((TestProxy)(object)settings).Handler = name => name == nameof(IApplicationSettingsService.GetSalaryAccrualSettingsAsync) && !failSettings
            ? Task.FromResult(new SalaryAccrualSettingsDto(salaryDay)) : throw new InvalidOperationException(name);
        return new(finance, staff ?? new StaffQuery(), settings, new BusinessDate());
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object>? Handler { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler!(targetMethod!.Name);
    }

    private sealed class StaffQuery : IExpenseBatchStaffDebtQuery
    {
        public Exception? Error { get; init; }
        public IReadOnlyList<ExpenseBatchDebt> Debts { get; init; } = [];
        public DateOnly? LastAccrualMonth { get; private set; }
        public DateOnly MonthTo { get; private set; }
        public bool Called { get; private set; }
        public Task<IReadOnlyList<ExpenseBatchDebt>> GetAsync(IReadOnlyList<Guid> ids, DateOnly monthTo, DateOnly? lastAccrualMonth, CancellationToken token)
        {
            Called = true; MonthTo = monthTo; LastAccrualMonth = lastAccrualMonth;
            return Error is null ? Task.FromResult(Debts) : Task.FromException<IReadOnlyList<ExpenseBatchDebt>>(Error);
        }
    }

    private sealed class BusinessDate : IBusinessDateProvider
    {
        public DateOnly SystemDate => Month.AddDays(9);
        public DateOnly Today => SystemDate;
        public DateOnly? OverrideDate => null;
        public void SetOverride(DateOnly? value) => throw new NotSupportedException();
    }
}
