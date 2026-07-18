using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlReportSortingIntegrationTests
{
    [PostgreSqlFact]
    public async Task GarageAndExpenseReportsSupportSingleMultipleAndAllSelections()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var january = new DateOnly(2026, 1, 1);
        var february = new DateOnly(2026, 2, 1);
        var periodEnd = february.AddMonths(1).AddDays(-1);
        var createdBeforePeriod = new DateTimeOffset(2025, 12, 15, 0, 0, 0, TimeSpan.Zero);
        var firstOwner = new Owner { LastName = "Фильтр", FirstName = "Первый" };
        var secondOwner = new Owner { LastName = "Фильтр", FirstName = "Второй" };
        var firstGarage = new Garage { Number = "F-1", PeopleCount = 1, FloorCount = 1, Owner = firstOwner };
        var secondGarage = new Garage { Number = "F-2", PeopleCount = 1, FloorCount = 1, Owner = secondOwner };
        var incomeType = new IncomeType { Name = $"Фильтр дохода {Guid.NewGuid():N}" };
        var group = new SupplierGroup { Name = $"Фильтр группы {Guid.NewGuid():N}" };
        var supplier = new Supplier { Name = "Фильтр поставщика", Group = group };
        var supplierExpenseType = new ExpenseType { Name = $"Фильтр выплаты {Guid.NewGuid():N}" };
        var department = new StaffDepartment { Name = $"Фильтр отдела {Guid.NewGuid():N}" };
        var staff = new StaffMember
        {
            FullName = "Фильтр Сотрудник",
            Rate = 200m,
            Department = department,
            CreatedAtUtc = createdBeforePeriod,
            UpdatedAtUtc = createdBeforePeriod
        };
        var salaryType = await context.ExpenseTypes.SingleAsync(type => type.Code == "salary");

        context.AddRange(firstOwner, secondOwner, firstGarage, secondGarage, incomeType, group, supplier, supplierExpenseType, department, staff);
        context.Accruals.AddRange(
            CreateAccrual(firstGarage, incomeType, january, 100m),
            CreateAccrual(secondGarage, incomeType, february, 300m));
        context.SupplierAccruals.Add(CreateSupplierAccrual(supplier, supplierExpenseType, january, 400m));
        context.FinancialOperations.AddRange(
            CreateExpense(supplier, supplierExpenseType, january, 50m, "SUPPLIER-PAY"),
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = february.AddDays(10),
                AccountingMonth = february,
                Amount = 75m,
                StaffMember = staff,
                ExpenseType = salaryType,
                DocumentNumber = "STAFF-PAY",
                CreatedAtUtc = new DateTimeOffset(2026, 2, 11, 0, 0, 0, TimeSpan.Zero)
            });
        await context.SaveChangesAsync();

        var garageQuery = new EfGarageReportQuery(context);
        var allGarages = await garageQuery.GetRowsAsync(january, february, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), false, 0, 20, new ReportSort("garageNumber", false), CancellationToken.None);
        Assert.Equal(2, allGarages.RowCount);
        Assert.Equal([firstGarage.Id, secondGarage.Id], allGarages.Rows.Select(row => row.GarageId).ToArray());

        var oneGarage = await garageQuery.GetRowsAsync(january, february, null, new HashSet<Guid> { secondGarage.Id }, new HashSet<Guid>(), new HashSet<Guid> { incomeType.Id }, false, 0, 20, new ReportSort("garageNumber", false), CancellationToken.None);
        Assert.Equal(1, oneGarage.RowCount);
        Assert.Equal(secondGarage.Id, Assert.Single(oneGarage.Rows).GarageId);

        var ownerSelection = await garageQuery.GetRowsAsync(january, february, null, new HashSet<Guid> { firstGarage.Id, secondGarage.Id }, new HashSet<Guid> { firstOwner.Id }, new HashSet<Guid> { incomeType.Id }, false, 0, 20, new ReportSort("garageNumber", false), CancellationToken.None);
        Assert.Equal(firstGarage.Id, Assert.Single(ownerSelection.Rows).GarageId);

        var expenseQuery = new EfExpenseReportQuery(context);
        var allCounterparties = await expenseQuery.GetRowsAsync(january, periodEnd, "all", new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), null, 20, 0, new ReportSort("date", false), CancellationToken.None);
        Assert.Equal(5, allCounterparties.RowCount);
        Assert.Equal(800m, allCounterparties.AccrualTotal);
        Assert.Equal(125m, allCounterparties.ExpenseTotal);
        Assert.Contains(allCounterparties.Rows, row => row.CounterpartyKind == "supplier");
        Assert.Contains(allCounterparties.Rows, row => row.CounterpartyKind == "staff" && row.StaffMemberId == staff.Id);

        var supplierOnly = await expenseQuery.GetRowsAsync(january, periodEnd, "all", new HashSet<Guid> { supplier.Id }, new HashSet<Guid>(), new HashSet<Guid> { supplierExpenseType.Id }, null, 20, 0, new ReportSort("date", false), CancellationToken.None);
        Assert.Equal(2, supplierOnly.RowCount);
        Assert.All(supplierOnly.Rows, row => Assert.Equal("supplier", row.CounterpartyKind));

        var staffOnly = await expenseQuery.GetRowsAsync(january, periodEnd, "all", new HashSet<Guid>(), new HashSet<Guid> { staff.Id }, new HashSet<Guid> { salaryType.Id }, null, 20, 0, new ReportSort("date", false), CancellationToken.None);
        Assert.Equal(3, staffOnly.RowCount);
        Assert.Equal(400m, staffOnly.AccrualTotal);
        Assert.Equal(75m, staffOnly.ExpenseTotal);
        Assert.All(staffOnly.Rows, row => Assert.Equal(staff.Id, row.StaffMemberId));

        var mixed = await expenseQuery.GetRowsAsync(january, periodEnd, "all", new HashSet<Guid> { supplier.Id }, new HashSet<Guid> { staff.Id }, new HashSet<Guid> { supplierExpenseType.Id, salaryType.Id }, null, 3, 0, new ReportSort("date", false), CancellationToken.None);
        Assert.Equal(5, mixed.RowCount);
        Assert.Equal(3, mixed.Rows.Count);
        Assert.Equal(800m, mixed.AccrualTotal);
        Assert.Equal(125m, mixed.ExpenseTotal);
    }

    [PostgreSqlFact]
    public async Task AllReportQueriesSortBeforePagingOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var january = new DateOnly(2026, 1, 1);
        var february = new DateOnly(2026, 2, 1);
        var ownerA = new Owner { LastName = "Альфа", FirstName = "Анна" };
        var ownerB = new Owner { LastName = "Бета", FirstName = "Борис" };
        var garageA = new Garage { Number = "10", PeopleCount = 1, FloorCount = 1, Owner = ownerA };
        var garageB = new Garage { Number = "20", PeopleCount = 1, FloorCount = 1, Owner = ownerB };
        var supplierGroup = new SupplierGroup { Name = $"REPORT-SORT-{Guid.NewGuid():N}" };
        var supplierA = new Supplier { Name = "Поставщик Альфа", Group = supplierGroup };
        var supplierB = new Supplier { Name = "Поставщик Бета", Group = supplierGroup };
        var incomeType = new IncomeType { Name = $"Взнос сортировка {Guid.NewGuid():N}" };
        var expenseType = new ExpenseType { Name = $"Выплата сортировка {Guid.NewGuid():N}" };
        var fund = new Fund
        {
            Name = $"Фонд сортировка {Guid.NewGuid():N}",
            NormalizedName = $"report-sort-{Guid.NewGuid():N}",
            SortOrder = 50
        };
        context.AddRange(ownerA, ownerB, garageA, garageB, supplierGroup, supplierA, supplierB, incomeType, expenseType, fund);
        context.Accruals.AddRange(
            CreateAccrual(garageA, incomeType, january, 1000m),
            CreateAccrual(garageB, incomeType, february, 500m));
        context.SupplierAccruals.AddRange(
            CreateSupplierAccrual(supplierA, expenseType, january, 400m),
            CreateSupplierAccrual(supplierB, expenseType, february, 100m));
        context.FinancialOperations.AddRange(
            CreateIncome(garageA, incomeType, january, 100m, "IN-100"),
            CreateIncome(garageB, incomeType, february, 300m, "IN-300"),
            CreateExpense(supplierA, expenseType, january, 50m, "OUT-50"),
            CreateExpense(supplierB, expenseType, february, 200m, "OUT-200"));
        context.FundOperations.AddRange(
            CreateFundOperation(fund, january, 80m, "Первое пополнение"),
            CreateFundOperation(fund, february, 180m, "Второе пополнение"));
        await context.SaveChangesAsync();

        var income = await new EfIncomeReportQuery(context).GetRowsAsync(
            january,
            february.AddMonths(1).AddDays(-1),
            "payments",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid> { incomeType.Id },
            null,
            1,
            0,
            new ReportSort("incomeAmount", true),
            CancellationToken.None);
        Assert.Equal(2, income.RowCount);
        Assert.Equal(300m, Assert.Single(income.Rows).IncomeAmount);

        var expense = await new EfExpenseReportQuery(context).GetRowsAsync(
            january,
            february.AddMonths(1).AddDays(-1),
            "payments",
            new HashSet<Guid>(),
            new HashSet<Guid> { expenseType.Id },
            null,
            1,
            0,
            new ReportSort("expenseAmount", true),
            CancellationToken.None);
        Assert.Equal(2, expense.RowCount);
        Assert.Equal(200m, Assert.Single(expense.Rows).ExpenseAmount);

        var consolidated = await new EfConsolidatedMonthlyReportQuery(context).GetMonthlyDataAsync(
            january,
            february,
            new ReportSort("incomeTotal", true),
            0,
            1,
            CancellationToken.None);
        Assert.Equal(2, consolidated.MonthlyRowCount);
        Assert.Equal(february, Assert.Single(consolidated.MonthlyRows).AccountingMonth);

        var garages = await new EfGarageReportQuery(context).GetRowsAsync(
            january,
            february,
            null,
            false,
            0,
            1,
            new ReportSort("difference", true),
            CancellationToken.None);
        Assert.Equal(2, garages.RowCount);
        Assert.Equal(900m, Assert.Single(garages.Rows).AccrualAmount - garages.Rows[0].IncomeAmount);

        var cash = await new EfCashMovementReportQuery(context).GetCashPaymentsAsync(
            january,
            february.AddMonths(1).AddDays(-1),
            null,
            0,
            1,
            new ReportSort("amount", true),
            CancellationToken.None);
        Assert.Equal(2, cash.RowCount);
        Assert.Equal(200m, Assert.Single(cash.Operations).Amount);

        var bank = await new EfCashMovementReportQuery(context).GetBankDepositsAsync(
            january,
            february.AddMonths(1).AddDays(-1),
            null,
            0,
            1,
            new ReportSort("amount", true),
            CancellationToken.None);
        Assert.Equal(2, bank.RowCount);
        Assert.Equal(180m, Assert.Single(bank.Operations).Amount);

        var fundChanges = await new EfFundChangeReportQuery(context).GetFundChangesAsync(
            january,
            february.AddMonths(1).AddDays(-1),
            null,
            0,
            1,
            new ReportSort("amount", true),
            CancellationToken.None);
        Assert.Equal(2, fundChanges.RowCount);
        Assert.Equal(180m, Assert.Single(fundChanges.Rows).Amount);

        var fees = await new EfFeeReportQuery(context).GetFeeReportPageAsync(
            [incomeType.Id],
            false,
            new ReportSort("debt", true),
            0,
            1,
            CancellationToken.None);
        Assert.Equal(2, fees.GarageRowCount);
        Assert.Equal(900m, Assert.Single(fees.GarageRows).Debt);

        foreach (var descending in new[] { false, true })
        {
            foreach (var field in new[] { "date", "accountingMonth", "garageNumber", "ownerName", "incomeTypeName", "accrualAmount", "incomeAmount", "debt", "documentNumber" })
            {
                var page = await new EfIncomeReportQuery(context).GetRowsAsync(january, february.AddMonths(1).AddDays(-1), "payments", new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid> { incomeType.Id }, null, 1, 0, new ReportSort(field, descending), CancellationToken.None);
                Assert.Single(page.Rows);
            }

            foreach (var field in new[] { "date", "accountingMonth", "supplierName", "expenseTypeName", "accrualAmount", "expenseAmount", "difference", "documentNumber" })
            {
                var page = await new EfExpenseReportQuery(context).GetRowsAsync(january, february.AddMonths(1).AddDays(-1), "payments", new HashSet<Guid>(), new HashSet<Guid> { expenseType.Id }, null, 1, 0, new ReportSort(field, descending), CancellationToken.None);
                Assert.Single(page.Rows);
            }

            foreach (var field in new[] { "accountingMonth", "incomeTotal", "expenseTotal", "accrualTotal", "balance", "debt", "operationCount", "accrualCount", "meterReadingCount" })
            {
                var page = await new EfConsolidatedMonthlyReportQuery(context).GetMonthlyDataAsync(january, february, new ReportSort(field, descending), 0, 1, CancellationToken.None);
                Assert.Single(page.MonthlyRows);
            }

            foreach (var field in new[] { "accountingMonth", "garageNumber", "ownerName", "incomeTypeName", "accrualAmount", "incomeAmount", "difference" })
            {
                var page = await new EfGarageReportQuery(context).GetRowsAsync(january, february, null, false, 0, 1, new ReportSort(field, descending), CancellationToken.None);
                Assert.Single(page.Rows);
            }

            foreach (var field in new[] { "date", "amount", "hasReceipt", "purpose", "supplierName", "expenseTypeName", "documentNumber" })
            {
                var page = await new EfCashMovementReportQuery(context).GetCashPaymentsAsync(january, february.AddMonths(1).AddDays(-1), null, 0, 1, new ReportSort(field, descending), CancellationToken.None);
                Assert.Single(page.Operations);
            }

            foreach (var field in new[] { "date", "amount", "fundName", "comment" })
            {
                var page = await new EfCashMovementReportQuery(context).GetBankDepositsAsync(january, february.AddMonths(1).AddDays(-1), null, 0, 1, new ReportSort(field, descending), CancellationToken.None);
                Assert.Single(page.Operations);
            }

            foreach (var field in new[] { "date", "fundName", "changeName", "amount", "balanceBefore", "balanceAfter", "actorDisplayName", "reason" })
            {
                var page = await new EfFundChangeReportQuery(context).GetFundChangesAsync(january, february.AddMonths(1).AddDays(-1), null, 0, 1, new ReportSort(field, descending), CancellationToken.None);
                Assert.Single(page.Rows);
            }

            foreach (var field in new[] { "garageNumber", "ownerName", "feeName", "accrued", "paid", "lastPaymentDate", "debt" })
            {
                var page = await new EfFeeReportQuery(context).GetFeeReportPageAsync([incomeType.Id], false, new ReportSort(field, descending), 0, 1, CancellationToken.None);
                Assert.Single(page.GarageRows);
            }
        }
    }

    private static Accrual CreateAccrual(Garage garage, IncomeType incomeType, DateOnly month, decimal amount) =>
        new()
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = month,
            DueDate = month.AddMonths(1).AddDays(-1),
            OverdueFromDate = month.AddMonths(1),
            Amount = amount,
            Source = "report_sort_test"
        };

    private static SupplierAccrual CreateSupplierAccrual(Supplier supplier, ExpenseType expenseType, DateOnly month, decimal amount) =>
        new()
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = month,
            Amount = amount,
            Source = "report_sort_test"
        };

    private static FinancialOperation CreateIncome(Garage garage, IncomeType incomeType, DateOnly month, decimal amount, string documentNumber) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = month.AddDays(10),
            AccountingMonth = month,
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType,
            DocumentNumber = documentNumber,
            CreatedAtUtc = new DateTimeOffset(month.AddDays(10).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)
        };

    private static FinancialOperation CreateExpense(Supplier supplier, ExpenseType expenseType, DateOnly month, decimal amount, string documentNumber) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = month.AddDays(11),
            AccountingMonth = month,
            Amount = amount,
            Supplier = supplier,
            ExpenseType = expenseType,
            DocumentNumber = documentNumber,
            CreatedAtUtc = new DateTimeOffset(month.AddDays(11).ToDateTime(new TimeOnly(11, 0)), TimeSpan.Zero)
        };

    private static FundOperation CreateFundOperation(Fund fund, DateOnly month, decimal amount, string reason) =>
        new()
        {
            Fund = fund,
            OperationKind = FundOperationKinds.Deposit,
            Amount = amount,
            BalanceBefore = 0m,
            BalanceAfter = amount,
            Reason = reason,
            IsCashToBankTransfer = true,
            CreatedAtUtc = new DateTimeOffset(month.AddDays(12).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)
        };
}
