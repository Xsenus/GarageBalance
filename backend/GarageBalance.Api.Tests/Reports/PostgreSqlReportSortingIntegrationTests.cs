using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlReportSortingIntegrationTests
{
    [PostgreSqlFact]
    public async Task GarageAndExpenseReportsSupportSingleMultipleAllAndUnknownSelections()
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

        var unknownGarageSelection = await garageQuery.GetRowsAsync(january, february, null, new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>(), false, 0, 20, new ReportSort("garageNumber", false), CancellationToken.None);
        Assert.Empty(unknownGarageSelection.Rows);
        Assert.Equal(0, unknownGarageSelection.RowCount);
        Assert.Equal(0m, unknownGarageSelection.AccrualTotal);
        Assert.Equal(0m, unknownGarageSelection.IncomeTotal);

        var unknownOwnerSelection = await garageQuery.GetRowsAsync(january, february, null, new HashSet<Guid>(), new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), false, 0, 20, new ReportSort("garageNumber", false), CancellationToken.None);
        Assert.Empty(unknownOwnerSelection.Rows);
        Assert.Equal(0, unknownOwnerSelection.RowCount);

        var unknownIncomeTypeSelection = await garageQuery.GetRowsAsync(january, february, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid> { Guid.NewGuid() }, false, 0, 20, new ReportSort("garageNumber", false), CancellationToken.None);
        Assert.Empty(unknownIncomeTypeSelection.Rows);
        Assert.Equal(0, unknownIncomeTypeSelection.RowCount);

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

        var unknownCounterparties = await expenseQuery.GetRowsAsync(january, periodEnd, "all", new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), null, 20, 0, new ReportSort("date", false), CancellationToken.None);
        Assert.Empty(unknownCounterparties.Rows);
        Assert.Equal(0, unknownCounterparties.RowCount);
        Assert.Equal(0m, unknownCounterparties.AccrualTotal);
        Assert.Equal(0m, unknownCounterparties.ExpenseTotal);

        var validStaffAndUnknownSupplier = await expenseQuery.GetRowsAsync(january, periodEnd, "all", new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid> { staff.Id }, new HashSet<Guid> { salaryType.Id }, null, 20, 0, new ReportSort("date", false), CancellationToken.None);
        Assert.Equal(3, validStaffAndUnknownSupplier.RowCount);
        Assert.All(validStaffAndUnknownSupplier.Rows, row => Assert.Equal("staff", row.CounterpartyKind));

        var validSupplierAndUnknownStaff = await expenseQuery.GetRowsAsync(january, periodEnd, "all", new HashSet<Guid> { supplier.Id }, new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid> { supplierExpenseType.Id }, null, 20, 0, new ReportSort("date", false), CancellationToken.None);
        Assert.Equal(2, validSupplierAndUnknownStaff.RowCount);
        Assert.All(validSupplierAndUnknownStaff.Rows, row => Assert.Equal("supplier", row.CounterpartyKind));

        var unknownExpenseTypeSelection = await expenseQuery.GetRowsAsync(january, periodEnd, "all", new HashSet<Guid> { supplier.Id }, new HashSet<Guid> { staff.Id }, new HashSet<Guid> { Guid.NewGuid() }, null, 20, 0, new ReportSort("date", false), CancellationToken.None);
        Assert.Empty(unknownExpenseTypeSelection.Rows);
        Assert.Equal(0, unknownExpenseTypeSelection.RowCount);
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
        var secondIncomeType = new IncomeType { Name = $"Дополнительный взнос сортировка {Guid.NewGuid():N}" };
        var expenseType = new ExpenseType { Name = $"Выплата сортировка {Guid.NewGuid():N}" };
        var fund = new Fund
        {
            Name = $"Фонд сортировка {Guid.NewGuid():N}",
            NormalizedName = $"report-sort-{Guid.NewGuid():N}",
            SortOrder = 50
        };
        context.AddRange(ownerA, ownerB, garageA, garageB, supplierGroup, supplierA, supplierB, incomeType, secondIncomeType, expenseType, fund);
        context.Accruals.AddRange(
            CreateAccrual(garageA, incomeType, january, 1000m),
            CreateAccrual(garageA, secondIncomeType, january, 200m),
            CreateAccrual(garageB, incomeType, february, 500m));
        context.SupplierAccruals.AddRange(
            CreateSupplierAccrual(supplierA, expenseType, january, 400m),
            CreateSupplierAccrual(supplierB, expenseType, february, 100m));
        context.FinancialOperations.AddRange(
            CreateIncome(garageA, incomeType, january, 100m, "IN-100"),
            CreateIncome(garageA, secondIncomeType, january, 50m, "IN-50"),
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
        Assert.Equal(400m, income.IncomeTotal);
        Assert.Equal(300m, Assert.Single(income.Rows).IncomeAmount);
        var incomeSecondPage = await new EfIncomeReportQuery(context).GetRowsAsync(
            january,
            february.AddMonths(1).AddDays(-1),
            "payments",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid> { incomeType.Id },
            null,
            1,
            1,
            new ReportSort("incomeAmount", true),
            CancellationToken.None);
        Assert.Equal(income.RowCount, incomeSecondPage.RowCount);
        Assert.Equal(income.AccrualTotal, incomeSecondPage.AccrualTotal);
        Assert.Equal(income.IncomeTotal, incomeSecondPage.IncomeTotal);
        Assert.Single(incomeSecondPage.Rows);

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
        Assert.Equal(250m, expense.ExpenseTotal);
        Assert.Equal(200m, Assert.Single(expense.Rows).ExpenseAmount);
        var expenseSecondPage = await new EfExpenseReportQuery(context).GetRowsAsync(
            january,
            february.AddMonths(1).AddDays(-1),
            "payments",
            new HashSet<Guid>(),
            new HashSet<Guid> { expenseType.Id },
            null,
            1,
            1,
            new ReportSort("expenseAmount", true),
            CancellationToken.None);
        Assert.Equal(expense.RowCount, expenseSecondPage.RowCount);
        Assert.Equal(expense.AccrualTotal, expenseSecondPage.AccrualTotal);
        Assert.Equal(expense.ExpenseTotal, expenseSecondPage.ExpenseTotal);
        Assert.Single(expenseSecondPage.Rows);

        var consolidated = await new EfConsolidatedMonthlyReportQuery(context).GetMonthlyDataAsync(
            january,
            february,
            new ReportSort("incomeTotal", true),
            0,
            1,
            CancellationToken.None);
        Assert.Equal(2, consolidated.MonthlyRowCount);
        Assert.Equal(february, Assert.Single(consolidated.MonthlyRows).AccountingMonth);
        var consolidatedSecondPage = await new EfConsolidatedMonthlyReportQuery(context).GetMonthlyDataAsync(
            january,
            february,
            new ReportSort("incomeTotal", true),
            1,
            1,
            CancellationToken.None);
        Assert.Equal(consolidated.MonthlyRowCount, consolidatedSecondPage.MonthlyRowCount);
        Assert.Equal(consolidated.IncomeByMonth, consolidatedSecondPage.IncomeByMonth);
        Assert.Equal(consolidated.ExpenseByMonth, consolidatedSecondPage.ExpenseByMonth);
        Assert.Equal(consolidated.AccrualByMonth, consolidatedSecondPage.AccrualByMonth);
        Assert.Equal(consolidated.IncomeBreakdown, consolidatedSecondPage.IncomeBreakdown);
        Assert.Equal(consolidated.ExpenseBreakdown, consolidatedSecondPage.ExpenseBreakdown);
        Assert.Single(consolidatedSecondPage.MonthlyRows);

        var garages = await new EfGarageReportQuery(context).GetRowsAsync(
            january,
            february,
            null,
            false,
            0,
            1,
            new ReportSort("difference", true),
            CancellationToken.None);
        Assert.Equal(3, garages.RowCount);
        Assert.Equal(1700m, garages.AccrualTotal);
        Assert.Equal(450m, garages.IncomeTotal);
        Assert.Equal(900m, Assert.Single(garages.Rows).AccrualAmount - garages.Rows[0].IncomeAmount);
        var garageSecondPage = await new EfGarageReportQuery(context).GetRowsAsync(
            january,
            february,
            null,
            false,
            1,
            1,
            new ReportSort("difference", true),
            CancellationToken.None);
        Assert.Equal(garages.RowCount, garageSecondPage.RowCount);
        Assert.Equal(garages.AccrualTotal, garageSecondPage.AccrualTotal);
        Assert.Equal(garages.IncomeTotal, garageSecondPage.IncomeTotal);
        Assert.Single(garageSecondPage.Rows);
        var groupedGarages = await new EfGarageReportQuery(context).GetRowsAsync(
            january,
            february,
            null,
            true,
            1,
            1,
            new ReportSort("difference", true),
            CancellationToken.None);
        Assert.Equal(2, groupedGarages.RowCount);
        Assert.Equal(garages.AccrualTotal, groupedGarages.AccrualTotal);
        Assert.Equal(garages.IncomeTotal, groupedGarages.IncomeTotal);
        Assert.Single(groupedGarages.Rows);

        var cash = await new EfCashMovementReportQuery(context).GetCashPaymentsAsync(
            january,
            february.AddMonths(1).AddDays(-1),
            null,
            0,
            1,
            new ReportSort("amount", true),
            CancellationToken.None);
        Assert.Equal(2, cash.RowCount);
        Assert.Equal(250m, cash.Total);
        Assert.Equal(200m, Assert.Single(cash.Operations).Amount);
        var cashSecondPage = await new EfCashMovementReportQuery(context).GetCashPaymentsAsync(january, february.AddMonths(1).AddDays(-1), null, 1, 1, new ReportSort("amount", true), CancellationToken.None);
        Assert.Equal(cash.RowCount, cashSecondPage.RowCount);
        Assert.Equal(cash.Total, cashSecondPage.Total);
        Assert.Single(cashSecondPage.Operations);

        var bank = await new EfCashMovementReportQuery(context).GetBankDepositsAsync(
            january,
            february.AddMonths(1).AddDays(-1),
            null,
            0,
            1,
            new ReportSort("amount", true),
            CancellationToken.None);
        Assert.Equal(2, bank.RowCount);
        Assert.Equal(260m, bank.Total);
        Assert.Equal(180m, Assert.Single(bank.Operations).Amount);
        var bankSecondPage = await new EfCashMovementReportQuery(context).GetBankDepositsAsync(january, february.AddMonths(1).AddDays(-1), null, 1, 1, new ReportSort("amount", true), CancellationToken.None);
        Assert.Equal(bank.RowCount, bankSecondPage.RowCount);
        Assert.Equal(bank.Total, bankSecondPage.Total);
        Assert.Single(bankSecondPage.Operations);

        var fundChanges = await new EfFundChangeReportQuery(context).GetFundChangesAsync(
            january,
            february.AddMonths(1).AddDays(-1),
            null,
            0,
            1,
            new ReportSort("amount", true),
            CancellationToken.None);
        Assert.Equal(2, fundChanges.RowCount);
        Assert.Equal(260m, fundChanges.DepositTotal);
        Assert.Equal(0m, fundChanges.WithdrawalTotal);
        Assert.Equal(180m, Assert.Single(fundChanges.Rows).Amount);
        var fundChangesSecondPage = await new EfFundChangeReportQuery(context).GetFundChangesAsync(january, february.AddMonths(1).AddDays(-1), null, 1, 1, new ReportSort("amount", true), CancellationToken.None);
        Assert.Equal(fundChanges.RowCount, fundChangesSecondPage.RowCount);
        Assert.Equal(fundChanges.DepositTotal, fundChangesSecondPage.DepositTotal);
        Assert.Equal(fundChanges.WithdrawalTotal, fundChangesSecondPage.WithdrawalTotal);
        Assert.Single(fundChangesSecondPage.Rows);

        var fees = await new EfFeeReportQuery(context).GetFeeReportPageAsync(
            [incomeType.Id],
            false,
            new ReportSort("debt", true),
            0,
            1,
            CancellationToken.None);
        Assert.Equal(2, fees.GarageRowCount);
        Assert.Equal(1100m, fees.DebtTotal);
        Assert.Equal(1500m, fees.AccrualTotals[incomeType.Id]);
        Assert.Equal(400m, fees.CollectedTotals[incomeType.Id]);
        Assert.Equal(900m, Assert.Single(fees.GarageRows).Debt);
        var feesSecondPage = await new EfFeeReportQuery(context).GetFeeReportPageAsync(
            [incomeType.Id],
            false,
            new ReportSort("debt", true),
            1,
            1,
            CancellationToken.None);
        Assert.Equal(fees.GarageRowCount, feesSecondPage.GarageRowCount);
        Assert.Equal(fees.DebtTotal, feesSecondPage.DebtTotal);
        Assert.Equal(fees.AccrualTotals[incomeType.Id], feesSecondPage.AccrualTotals[incomeType.Id]);
        Assert.Equal(fees.CollectedTotals[incomeType.Id], feesSecondPage.CollectedTotals[incomeType.Id]);
        Assert.Single(feesSecondPage.GarageRows);

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

    [PostgreSqlFact]
    public async Task HistoricalReportQueriesStayPagedAndCompleteAtRealisticVolume()
    {
        const int garageCount = 300;
        const int monthCount = 36;
        const int pageSize = 25;
        var expectedRowCount = garageCount * monthCount;
        var expectedAccrualTotal = expectedRowCount * 100m;
        var expectedIncomeTotal = expectedRowCount * 60m;
        var firstMonth = new DateOnly(2023, 1, 1);
        var lastMonth = firstMonth.AddMonths(monthCount - 1);
        var dateTo = lastMonth.AddMonths(1).AddDays(-1);

        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var incomeType = new IncomeType { Name = $"REPORT-PERFORMANCE-{Guid.NewGuid():N}" };
        var garages = Enumerable.Range(1, garageCount)
            .Select(index => new Garage
            {
                Number = $"PERF-{index:000}",
                PeopleCount = 1,
                FloorCount = 1
            })
            .ToArray();
        context.Add(incomeType);
        context.Garages.AddRange(garages);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var accruals = new List<Accrual>(expectedRowCount);
        var payments = new List<FinancialOperation>(expectedRowCount);
        foreach (var garage in garages)
        {
            for (var monthOffset = 0; monthOffset < monthCount; monthOffset++)
            {
                var month = firstMonth.AddMonths(monthOffset);
                accruals.Add(new Accrual
                {
                    GarageId = garage.Id,
                    IncomeTypeId = incomeType.Id,
                    AccountingMonth = month,
                    DueDate = month.AddMonths(1).AddDays(-1),
                    OverdueFromDate = month.AddMonths(1),
                    Amount = 100m,
                    Source = "report_performance_test"
                });
                payments.Add(new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Income,
                    OperationDate = month.AddDays(14),
                    AccountingMonth = month,
                    Amount = 60m,
                    GarageId = garage.Id,
                    IncomeTypeId = incomeType.Id,
                    DocumentNumber = $"PERF-{garage.Number}-{month:yyyyMM}",
                    CreatedAtUtc = new DateTimeOffset(month.AddDays(14).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)
                });
            }
        }

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        context.Accruals.AddRange(accruals);
        context.FinancialOperations.AddRange(payments);
        await context.SaveChangesAsync();
        context.ChangeTracker.AutoDetectChangesEnabled = true;
        context.ChangeTracker.Clear();
        await context.Database.ExecuteSqlRawAsync("ANALYZE accruals; ANALYZE financial_operations; ANALYZE garages;");

        var stopwatch = Stopwatch.StartNew();
        var debtors = await DictionaryServiceTestFactory.Create(context).GetGaragesPageAsync(
            null,
            0,
            pageSize,
            "overdueDebt",
            "desc",
            CancellationToken.None,
            debtorsOnly: true);
        var debtorsElapsed = stopwatch.Elapsed;
        Assert.Equal(garageCount, debtors.TotalCount);
        Assert.Equal(pageSize, debtors.Items.Count);
        Assert.All(debtors.Items, garage => Assert.Equal(1440m, garage.OverdueDebt));

        stopwatch.Restart();
        var worksheet = await FinanceServiceTestFactory.Create(context).GetGarageIncomeWorksheetAsync(
            garages[0].Id,
            new GarageIncomeWorksheetRequest(firstMonth, lastMonth),
            CancellationToken.None);
        var worksheetElapsed = stopwatch.Elapsed;
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal(monthCount, worksheet.Value!.Rows.Count);
        Assert.Equal(monthCount * 100m, worksheet.Value.AccrualTotal);
        Assert.Equal(monthCount * 60m, worksheet.Value.IncomeTotal);
        Assert.Equal(monthCount * 40m, worksheet.Value.ClosingDebt);

        stopwatch.Restart();
        var income = await new EfIncomeReportQuery(context).GetRowsAsync(
            firstMonth,
            dateTo,
            "all",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid> { incomeType.Id },
            null,
            pageSize,
            0,
            new ReportSort("date", true),
            CancellationToken.None);
        var incomeElapsed = stopwatch.Elapsed;
        Assert.Equal(expectedRowCount * 2, income.RowCount);
        Assert.Equal(expectedAccrualTotal, income.AccrualTotal);
        Assert.Equal(expectedIncomeTotal, income.IncomeTotal);
        Assert.Equal(pageSize, income.Rows.Count);

        stopwatch.Restart();
        var garagesReport = await new EfGarageReportQuery(context).GetRowsAsync(
            firstMonth,
            lastMonth,
            null,
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid> { incomeType.Id },
            false,
            0,
            pageSize,
            new ReportSort("difference", true),
            CancellationToken.None);
        var garagesElapsed = stopwatch.Elapsed;
        Assert.Equal(expectedRowCount, garagesReport.RowCount);
        Assert.Equal(expectedAccrualTotal, garagesReport.AccrualTotal);
        Assert.Equal(expectedIncomeTotal, garagesReport.IncomeTotal);
        Assert.Equal(pageSize, garagesReport.Rows.Count);

        stopwatch.Restart();
        var consolidated = await new EfConsolidatedMonthlyReportQuery(context).GetMonthlyDataAsync(
            firstMonth,
            lastMonth,
            new ReportSort("accountingMonth", true),
            0,
            12,
            CancellationToken.None);
        var consolidatedElapsed = stopwatch.Elapsed;
        Assert.Equal(monthCount, consolidated.MonthlyRowCount);
        Assert.Equal(12, consolidated.MonthlyRows.Count);
        Assert.Equal(expectedAccrualTotal, consolidated.AccrualByMonth.Sum(row => row.Amount));
        Assert.Equal(expectedIncomeTotal, consolidated.IncomeByMonth.Sum(row => row.Amount));

        stopwatch.Restart();
        var fees = await new EfFeeReportQuery(context).GetFeeReportPageAsync(
            [incomeType.Id],
            false,
            new ReportSort("debt", true),
            0,
            pageSize,
            CancellationToken.None);
        var feesElapsed = stopwatch.Elapsed;
        Assert.Equal(garageCount, fees.GarageRowCount);
        Assert.Equal(pageSize, fees.GarageRows.Count);
        Assert.Equal(expectedAccrualTotal, fees.AccrualTotals[incomeType.Id]);
        Assert.Equal(expectedIncomeTotal, fees.CollectedTotals[incomeType.Id]);
        Assert.Equal(expectedAccrualTotal - expectedIncomeTotal, fees.DebtTotal);

        var totalQueryTime = debtorsElapsed + worksheetElapsed + incomeElapsed + garagesElapsed + consolidatedElapsed + feesElapsed;
        Assert.True(
            totalQueryTime < TimeSpan.FromSeconds(15),
            $"Realistic finance queries took {totalQueryTime.TotalSeconds:F2}s: debtors {debtorsElapsed.TotalSeconds:F2}s, worksheet {worksheetElapsed.TotalSeconds:F2}s, income {incomeElapsed.TotalSeconds:F2}s, garages {garagesElapsed.TotalSeconds:F2}s, consolidated {consolidatedElapsed.TotalSeconds:F2}s, fees {feesElapsed.TotalSeconds:F2}s.");
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
