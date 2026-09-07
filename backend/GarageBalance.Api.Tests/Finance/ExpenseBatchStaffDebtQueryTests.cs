using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class ExpenseBatchStaffDebtQueryTests
{
    [PostgreSqlFact]
    public async Task GetAsync_DoesNotAccrueBeforeRegistrationEvenWhenEmploymentHistoryPredatesIt()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var member = Member();
        member.CreatedAtUtc = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
        member.EmploymentPeriods = [new() { EffectiveFrom = new DateOnly(1970, 1, 1) }];
        context.Add(member);
        await context.SaveChangesAsync();
        var query = new EfExpenseBatchStaffDebtQuery(context, new BusinessDate());
        var result = await query.GetAsync([member.Id], January.AddMonths(3), January.AddMonths(3), CancellationToken.None);
        Assert.Equal(new[] { January.AddMonths(2), January.AddMonths(3) }, result.Select(item => item.AccountingMonth));
        Assert.Equal(new[] { 100m, 100m }, result.Select(item => item.RemainingAmount));
    }

    private static readonly DateOnly January = new(2026, 1, 1);

    [PostgreSqlFact]
    public async Task GetAsync_AggregatesHistoryAndMatchesSalaryPeriodsWithoutLoadingOperations()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var member = Member();
        var salary = await context.ExpenseTypes.SingleAsync(item => item.Code == "salary");
        member.SalaryRatePeriods =
        [
            new() { EffectiveFrom = January, Rate = 100m },
            new() { EffectiveFrom = January.AddMonths(2), Rate = 200m }
        ];
        member.EmploymentPeriods =
        [
            new() { EffectiveFrom = January, EffectiveTo = January },
            new() { EffectiveFrom = January.AddMonths(2) }
        ];
        context.Add(member);
        foreach (var (month, amount, canceled) in new[] { (0, 40m, false), (0, 900m, true), (1, 10m, false), (2, 50m, false), (4, 900m, false) })
        {
            context.Add(new FinancialOperation
            {
                StaffMember = member,
                ExpenseType = salary,
                OperationKind = "expense",
                AccountingMonth = January.AddMonths(month),
                OperationDate = January.AddMonths(month),
                Amount = amount,
                IsCanceled = canceled,
                ExpensePaymentSource = "cash"
            });
        }
        foreach (var (type, amount, canceled) in new[] { ("bonus", 30m, false), ("penalty", 5m, false), ("bonus", 900m, true) })
        {
            context.Add(new StaffSalaryAdjustment
            {
                StaffMember = member,
                AccountingMonth = January.AddMonths(2),
                AdjustmentType = type,
                Amount = amount,
                IsCanceled = canceled,
                Reason = "Контроль расчёта"
            });
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var query = new EfExpenseBatchStaffDebtQuery(context, new BusinessDate());

        var result = await query.GetAsync([member.Id, member.Id], January.AddMonths(3), January.AddMonths(3), CancellationToken.None);

        Assert.Equal(new[] { 60m, -10m, 175m, 200m }, result.Select(item => item.RemainingAmount));
        Assert.All(result, item => { Assert.Equal(member.Id, item.RecipientId); Assert.Equal(salary.Id, item.ExpenseTypeId); Assert.Null(item.FundId); });
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(425m, ExpenseBatchPlanner.Build(January.AddMonths(3), result).CashAmount);

        var beforeAccrualDay = await query.GetAsync([member.Id], January.AddMonths(3), January.AddMonths(2), CancellationToken.None);
        Assert.Equal(new[] { 60m, -10m, 175m }, beforeAccrualDay.Select(item => item.RemainingAmount));
        var noAccruals = await query.GetAsync([member.Id], January.AddMonths(3), null, CancellationToken.None);
        Assert.Equal(new[] { -40m, -10m, -25m }, noAccruals.Select(item => item.RemainingAmount));
    }

    [PostgreSqlFact]
    public async Task GetAsync_UsesArchivedFallbackAndDoesNotIncludeUnselectedEmployees()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var member = Member();
        member.IsArchived = true;
        member.UpdatedAtUtc = new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);
        var unselected = Member();
        unselected.Department = member.Department;
        context.AddRange(member, unselected);
        await context.SaveChangesAsync();

        var result = await new EfExpenseBatchStaffDebtQuery(context, new BusinessDate())
            .GetAsync([member.Id], January.AddMonths(3), January.AddMonths(3), CancellationToken.None);

        Assert.Equal(new[] { January, January.AddMonths(1) }, result.Select(item => item.AccountingMonth));
        Assert.All(result, item => Assert.Equal(100m, item.RemainingAmount));
    }

    [PostgreSqlFact]
    public async Task GetAsync_RejectsMissingEmployeesSalaryAndExcessiveHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var query = new EfExpenseBatchStaffDebtQuery(context, new BusinessDate());
        var missing = await Assert.ThrowsAsync<ExpenseBatchPreparationException>(() => query.GetAsync([Guid.NewGuid()], January, January, CancellationToken.None));
        Assert.Contains("Состав сотрудников изменился", missing.Message);
        var member = Member();
        member.CreatedAtUtc = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
        context.Add(member);
        await context.SaveChangesAsync();
        var tooLong = await Assert.ThrowsAsync<ExpenseBatchPreparationException>(() => query.GetAsync([member.Id], January, January, CancellationToken.None));
        Assert.Contains("600 месяцев", tooLong.Message);
        (await context.ExpenseTypes.SingleAsync(item => item.Code == "salary")).IsArchived = true;
        await context.SaveChangesAsync();
        var missingSalary = await Assert.ThrowsAsync<ExpenseBatchPreparationException>(() => query.GetAsync([member.Id], January, January, CancellationToken.None));
        Assert.Contains("статья «Зарплата» не найдена", missingSalary.Message);
    }

    [Fact]
    public async Task GetAsync_ValidatesBeforeAccessingDatabaseAndHonorsCancellation()
    {
        await using var context = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().Options);
        var query = new EfExpenseBatchStaffDebtQuery(context, new BusinessDate());
        Assert.Empty(await query.GetAsync([], January, January, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => query.GetAsync(null!, January, January, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => query.GetAsync([Guid.Empty], January, January, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => query.GetAsync([], January.AddDays(1), January, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => query.GetAsync([], January, January.AddDays(1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => query.GetAsync([], January, January.AddMonths(1), CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetAsync([], January, January, new CancellationToken(true)));
    }

    private static StaffMember Member() => new()
    {
        FullName = "Контрольный сотрудник",
        Rate = 100m,
        Department = new StaffDepartment { Name = "Контрольный отдел" },
        CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
    };

    private sealed class BusinessDate : IBusinessDateProvider
    {
        public DateOnly SystemDate => new(2026, 4, 30);
        public DateOnly Today => SystemDate;
        public DateOnly? OverrideDate => null;
        public void SetOverride(DateOnly? value) => throw new NotSupportedException();
    }
}
