using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlExpenseReportAccrualQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task StaffAccrual_UsesBusinessMonthAndSalaryDayButKeepsAdjustments()
    {
        var january = new DateOnly(2043, 1, 1);
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid staffId;
        Guid salaryTypeId;
        await using (var seedContext = database.CreateContext())
        {
            var salarySetting = await seedContext.ApplicationSettings
                .SingleAsync(setting => setting.Key == ApplicationSettingsService.SalaryAccrualDayKey);
            salarySetting.IntegerValue = 15;
            var salaryType = await seedContext.ExpenseTypes.SingleAsync(type => type.Code == "salary");
            var department = new StaffDepartment { Name = $"Отдел Петрова {Guid.NewGuid():N}" };
            var staff = new StaffMember
            {
                FullName = $"Петров {Guid.NewGuid():N}",
                Rate = 300m,
                Department = department,
                CreatedAtUtc = new DateTimeOffset(2042, 12, 31, 18, 30, 0, TimeSpan.Zero)
            };
            seedContext.AddRange(department, staff);
            seedContext.StaffSalaryAdjustments.AddRange(
                new StaffSalaryAdjustment
                {
                    StaffMember = staff,
                    AccountingMonth = january,
                    AdjustmentType = StaffSalaryAdjustmentTypes.Bonus,
                    Amount = 50m,
                    Reason = "Премия"
                },
                new StaffSalaryAdjustment
                {
                    StaffMember = staff,
                    AccountingMonth = january,
                    AdjustmentType = StaffSalaryAdjustmentTypes.Penalty,
                    Amount = 20m,
                    Reason = "Штраф"
                });
            await seedContext.SaveChangesAsync();
            staffId = staff.Id;
            salaryTypeId = salaryType.Id;
        }

        await using var context = database.CreateContext();
        var beforeDay = await new EfExpenseReportQuery(
                context,
                new TestBusinessDateProvider(new DateOnly(2043, 1, 2), "Asia/Novosibirsk"))
            .GetRowsAsync(
                january.AddMonths(-1),
                january.AddMonths(1).AddDays(-1),
                "accruals",
                new HashSet<Guid>(),
                new HashSet<Guid> { staffId },
                new HashSet<Guid> { salaryTypeId },
                null,
                25,
                0,
                new ReportSort("date", false),
                CancellationToken.None);

        var beforeRow = Assert.Single(beforeDay.Rows);
        Assert.Equal(january, beforeRow.AccountingMonth);
        Assert.Equal(30m, beforeDay.AccrualTotal);

        var afterDay = await new EfExpenseReportQuery(
                context,
                new TestBusinessDateProvider(new DateOnly(2043, 1, 15), "Asia/Novosibirsk"))
            .GetRowsAsync(
                january.AddMonths(-1),
                january.AddMonths(1).AddDays(-1),
                "accruals",
                new HashSet<Guid>(),
                new HashSet<Guid> { staffId },
                new HashSet<Guid> { salaryTypeId },
                null,
                25,
                0,
                new ReportSort("date", false),
                CancellationToken.None);

        var afterRow = Assert.Single(afterDay.Rows);
        Assert.Equal(january, afterRow.AccountingMonth);
        Assert.Equal(330m, afterDay.AccrualTotal);
    }

    [PostgreSqlFact]
    public async Task AccrualPageLoadsStartingBalanceSupplierAndStaffTotalsInOneCommand()
    {
        var month = new DateOnly(2043, 1, 1);
        var suffix = Guid.NewGuid().ToString("N");
        var supplierName = $"Поставщик {suffix}";
        var staffName = $"Сотрудник {suffix}";
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid serviceExpenseTypeId;
        Guid staffId;
        await using (var seedContext = database.CreateContext())
        {
            var group = new SupplierGroup { Name = $"Expense accrual {suffix}" };
            var supplier = new Supplier
            {
                Name = supplierName,
                StartingBalance = 100m,
                Group = group,
                CreatedAtUtc = new DateTimeOffset(2043, 1, 20, 0, 0, 0, TimeSpan.Zero),
                UpdatedAtUtc = new DateTimeOffset(2043, 1, 20, 0, 0, 0, TimeSpan.Zero)
            };
            var department = new StaffDepartment { Name = $"Department {suffix}" };
            var staff = new StaffMember
            {
                FullName = staffName,
                Rate = 200m,
                Department = department,
                CreatedAtUtc = new DateTimeOffset(2042, 12, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatedAtUtc = new DateTimeOffset(2042, 12, 1, 0, 0, 0, TimeSpan.Zero)
            };
            var serviceExpenseType = new ExpenseType { Name = $"Эксплуатация {suffix}", Code = $"service_{suffix}" };
            var salaryExpenseType = await seedContext.ExpenseTypes.SingleAsync(expenseType => expenseType.Code == "salary");
            seedContext.AddRange(group, supplier, department, staff, serviceExpenseType);
            seedContext.SupplierAccruals.AddRange(
                CreateAccrual(supplier, serviceExpenseType, month, 500m),
                CreateAccrual(supplier, serviceExpenseType, month, 999m, true),
                CreateAccrual(supplier, serviceExpenseType, month.AddMonths(-1), 300m));
            seedContext.StaffSalaryAdjustments.AddRange(
                new StaffSalaryAdjustment
                {
                    StaffMember = staff,
                    AccountingMonth = month,
                    AdjustmentType = StaffSalaryAdjustmentTypes.Bonus,
                    Amount = 70m,
                    Reason = "Премия"
                },
                new StaffSalaryAdjustment
                {
                    StaffMember = staff,
                    AccountingMonth = month,
                    AdjustmentType = StaffSalaryAdjustmentTypes.Penalty,
                    Amount = 20m,
                    Reason = "Штраф"
                });
            await seedContext.SaveChangesAsync();
            serviceExpenseTypeId = serviceExpenseType.Id;
            staffId = staff.Id;
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var query = new EfExpenseReportQuery(context);

        var page = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "accruals",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            1,
            0,
            new ReportSort("accrualAmount", true),
            CancellationToken.None);

        Assert.Equal(3, page.RowCount);
        Assert.Equal(850m, page.AccrualTotal);
        Assert.Equal(0m, page.ExpenseTotal);
        var supplierAccrual = Assert.Single(page.Rows);
        Assert.Equal("accruals", supplierAccrual.RowType);
        Assert.Equal(supplierName, supplierAccrual.SupplierName);
        Assert.Equal(500m, supplierAccrual.AccrualAmount);
        var pageCommand = Assert.Single(capture.Commands);
        Assert.Contains("WITH filtered_rows AS", pageCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(SUM(accrual_amount), 0)", pageCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generate_series", pageCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(pageCommand, "FROM supplier_accruals"));

        foreach (var descending in new[] { false, true })
        {
            foreach (var field in new[] { "date", "accountingMonth", "supplierName", "expenseTypeName", "accrualAmount", "expenseAmount", "difference", "documentNumber" })
            {
                capture.Commands.Clear();
                var sortedPage = await query.GetRowsAsync(
                    month,
                    month.AddMonths(1).AddDays(-1),
                    "accruals",
                    new HashSet<Guid>(),
                    new HashSet<Guid>(),
                    new HashSet<Guid>(),
                    null,
                    1,
                    1,
                    new ReportSort(field, descending),
                    CancellationToken.None);

                Assert.Equal(3, sortedPage.RowCount);
                Assert.Equal(850m, sortedPage.AccrualTotal);
                Assert.Single(sortedPage.Rows);
                Assert.Single(capture.Commands);
            }
        }

        capture.Commands.Clear();
        var staffOnly = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "accruals",
            new HashSet<Guid>(),
            new HashSet<Guid> { staffId },
            new HashSet<Guid>(),
            staffName.ToUpperInvariant(),
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(1, staffOnly.RowCount);
        Assert.Equal(250m, staffOnly.AccrualTotal);
        var staffRow = Assert.Single(staffOnly.Rows);
        Assert.Equal(staffName, staffRow.SupplierName);
        Assert.Equal(250m, staffRow.AccrualAmount);
        Assert.Equal("Оклад с учетом премий и штрафов", staffRow.Comment);
        Assert.Equal("staff", staffRow.CounterpartyKind);
        Assert.Equal(staffId, staffRow.StaffMemberId);
        Assert.Single(capture.Commands);

        capture.Commands.Clear();
        var typeFiltered = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "accruals",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid> { serviceExpenseTypeId },
            null,
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(1, typeFiltered.RowCount);
        Assert.Equal(500m, typeFiltered.AccrualTotal);
        Assert.Equal("supplier", Assert.Single(typeFiltered.Rows).CounterpartyKind);
        Assert.Single(capture.Commands);

        capture.Commands.Clear();
        var startingBalanceSearch = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "accruals",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            "СТАРТОВЫЙ",
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(1, startingBalanceSearch.RowCount);
        Assert.Equal(100m, startingBalanceSearch.AccrualTotal);
        var startingBalanceRow = Assert.Single(startingBalanceSearch.Rows);
        Assert.Equal("starting_balance", startingBalanceRow.RowType);
        Assert.Equal(month, startingBalanceRow.AccountingMonth);
        Assert.Single(capture.Commands);

        capture.Commands.Clear();
        var laterPeriod = await query.GetRowsAsync(
            month.AddMonths(1),
            month.AddMonths(2).AddDays(-1),
            "accruals",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            "СТАРТОВЫЙ",
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(0, laterPeriod.RowCount);
        Assert.Equal(0m, laterPeriod.AccrualTotal);
        Assert.Empty(laterPeriod.Rows);
        Assert.Single(capture.Commands);
    }

    private static SupplierAccrual CreateAccrual(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly accountingMonth,
        decimal amount,
        bool isCanceled = false) =>
        new()
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = accountingMonth,
            Amount = amount,
            Source = "expense_accrual_query_integration",
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
