using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFinancialReportPeriodIntegrationTests
{
    [PostgreSqlFact]
    public async Task GaragePeriod_ReturnsFirstUnpaidAccrualMonthOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        await using (var seedContext = database.CreateContext())
        {
            var garage = new Garage { Number = $"PG-{Guid.NewGuid():N}" };
            var incomeType = new IncomeType { Name = $"Взнос {Guid.NewGuid():N}", Code = $"pg_fee_{Guid.NewGuid():N}" };
            var paidAccrual = CreateGarageAccrual(garage, incomeType, new DateOnly(2023, 2, 1), 100m);
            var unpaidAccrual = CreateGarageAccrual(garage, incomeType, new DateOnly(2024, 5, 1), 250m);
            var payment = new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                Garage = garage,
                IncomeType = incomeType,
                OperationDate = new DateOnly(2025, 1, 10),
                AccountingMonth = new DateOnly(2025, 1, 1),
                Amount = 100m
            };
            garageId = garage.Id;
            seedContext.AddRange(
                garage,
                incomeType,
                paidAccrual,
                unpaidAccrual,
                payment,
                new AccrualPaymentAllocation
                {
                    Accrual = paidAccrual,
                    FinancialOperation = payment,
                    Amount = 100m
                });
            await seedContext.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var result = await FinanceServiceTestFactory.Create(
            context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero)))
            .GetFinancialReportPeriodAsync(
                new FinancialReportPeriodRequest(garageId, null, null),
                CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2023, 2, 1), result.Value!.MonthFrom);
        Assert.Equal(new DateOnly(2026, 7, 1), result.Value.MonthTo);
        Assert.Equal(new DateOnly(2024, 5, 1), result.Value.DefaultMonthFrom);
        Assert.Equal(new DateOnly(2026, 7, 1), result.Value.DefaultMonthTo);
    }

    [PostgreSqlFact]
    public async Task SupplierPeriod_UsesFirstAndLastActiveMonthsOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        await using (var seedContext = database.CreateContext())
        {
            var group = new SupplierGroup { Name = "Группа полного периода PG" };
            var supplier = new Supplier { Name = "Поставщик полного периода PG", Group = group };
            var expenseType = new ExpenseType { Name = "Услуга полного периода PG", Code = "pg_full_period" };
            supplierId = supplier.Id;
            seedContext.AddRange(
                group,
                supplier,
                expenseType,
                CreateAccrual(supplier, expenseType, new DateOnly(2023, 2, 1)),
                CreateAccrual(supplier, expenseType, new DateOnly(2022, 1, 1), isCanceled: true),
                CreateExpense(supplier, expenseType, new DateOnly(2027, 3, 1)),
                CreateExpense(supplier, expenseType, new DateOnly(2028, 4, 1), isCanceled: true));
            await seedContext.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var result = await FinanceServiceTestFactory.Create(context).GetFinancialReportPeriodAsync(
            new FinancialReportPeriodRequest(null, supplierId, null),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2023, 2, 1), result.Value!.MonthFrom);
        Assert.Equal(new DateOnly(2027, 3, 1), result.Value.MonthTo);
    }

    [PostgreSqlFact]
    public async Task StaffPeriod_IncludesSalaryAdjustmentMonthsOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid staffMemberId;
        await using (var seedContext = database.CreateContext())
        {
            var department = new StaffDepartment { Name = $"Отдел периода {Guid.NewGuid():N}" };
            var staffMember = new StaffMember
            {
                FullName = $"Сотрудник периода {Guid.NewGuid():N}",
                Department = department,
                Rate = 100m,
                CreatedAtUtc = new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.Zero),
                UpdatedAtUtc = new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.Zero)
            };
            staffMemberId = staffMember.Id;
            seedContext.AddRange(
                department,
                staffMember,
                new StaffSalaryAdjustment
                {
                    StaffMember = staffMember,
                    AccountingMonth = new DateOnly(2023, 11, 1),
                    AdjustmentType = StaffSalaryAdjustmentTypes.Bonus,
                    Amount = 10m,
                    Reason = "Ретроспективная премия"
                },
                new StaffSalaryAdjustment
                {
                    StaffMember = staffMember,
                    AccountingMonth = new DateOnly(2027, 4, 1),
                    AdjustmentType = StaffSalaryAdjustmentTypes.Penalty,
                    Amount = 5m,
                    Reason = "Штраф"
                });
            await seedContext.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var result = await FinanceServiceTestFactory.Create(context).GetFinancialReportPeriodAsync(
            new FinancialReportPeriodRequest(null, null, staffMemberId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2023, 11, 1), result.Value!.MonthFrom);
        Assert.Equal(new DateOnly(2027, 4, 1), result.Value.MonthTo);
    }

    private static SupplierAccrual CreateAccrual(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly month,
        bool isCanceled = false) => new()
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = month,
            Amount = 100m,
            Source = AccrualSources.Manual,
            Comment = "Проверка полного периода",
            IsCanceled = isCanceled
        };

    private static Accrual CreateGarageAccrual(
        Garage garage,
        IncomeType incomeType,
        DateOnly month,
        decimal amount) => new()
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = month,
            DueDate = month,
            OverdueFromDate = month,
            Amount = amount,
            Source = AccrualSources.Manual,
            Comment = "Проверка первого непогашенного начисления"
        };

    private static FinancialOperation CreateExpense(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly month,
        bool isCanceled = false) => new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = month,
            AccountingMonth = month,
            Amount = 100m,
            Supplier = supplier,
            ExpenseType = expenseType,
            IsCanceled = isCanceled
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
