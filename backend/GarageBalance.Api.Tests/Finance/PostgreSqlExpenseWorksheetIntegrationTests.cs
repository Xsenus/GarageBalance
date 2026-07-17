using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlExpenseWorksheetIntegrationTests
{
    [PostgreSqlFact]
    public async Task BankExpense_AllowsNegativeServiceDifferenceButRejectsInsufficientBankBalance()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        Guid expenseTypeId;
        await using (var seedContext = database.CreateContext())
        {
            seedContext.FinancialOperations.RemoveRange(seedContext.FinancialOperations);
            seedContext.FundOperations.RemoveRange(seedContext.FundOperations);
            seedContext.Funds.RemoveRange(seedContext.Funds);
            var owner = new Owner { LastName = "Проверка", FirstName = "Банка" };
            var garage = new Garage { Number = "PG-BANK-RULE", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var incomeType = new IncomeType { Name = "Банковское правило PG", Code = "pg_bank_rule" };
            var supplierGroup = new SupplierGroup { Name = "Банковское правило PG" };
            var supplier = new Supplier { Name = "Поставщик банковского правила PG", Group = supplierGroup };
            var expenseType = new ExpenseType { Name = incomeType.Name, Code = incomeType.Code };
            var bankFund = new Fund { Name = "Банк правила PG", NormalizedName = "БАНК ПРАВИЛА PG", Balance = 300m };
            supplierId = supplier.Id;
            expenseTypeId = expenseType.Id;
            seedContext.AddRange(
                owner,
                garage,
                incomeType,
                supplierGroup,
                supplier,
                expenseType,
                bankFund,
                new FundOperation
                {
                    Fund = bankFund,
                    OperationKind = FundOperationKinds.Deposit,
                    Amount = 300m,
                    BalanceBefore = 0m,
                    BalanceAfter = 300m,
                    Reason = "Остаток банка для проверки",
                    IsCashToBankTransfer = true,
                    CreatedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)
                },
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Income,
                    OperationDate = new DateOnly(2026, 6, 10),
                    AccountingMonth = new DateOnly(2026, 6, 1),
                    Amount = 100m,
                    Garage = garage,
                    IncomeType = incomeType
                },
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 6, 1), 500m));
            await seedContext.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var service = FinanceServiceTestFactory.Create(context);
        var allowed = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                supplierId,
                expenseTypeId,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                250m,
                "PG-BANK-ALLOWED",
                null),
            Guid.NewGuid(),
            CancellationToken.None);
        var rejected = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                supplierId,
                expenseTypeId,
                new DateOnly(2026, 6, 21),
                new DateOnly(2026, 6, 1),
                50.01m,
                "PG-BANK-REJECTED",
                null),
            Guid.NewGuid(),
            CancellationToken.None);
        var worksheet = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        Assert.True(allowed.Succeeded);
        Assert.False(rejected.Succeeded);
        Assert.Equal("bank_amount_insufficient", rejected.ErrorCode);
        var row = Assert.Single(worksheet.Value!.Rows, item => item.SupplierId == supplierId && item.ExpenseTypeId == expenseTypeId);
        Assert.Equal(100m, row.CollectedAmount);
        Assert.Equal(-400m, row.Difference);
        Assert.Equal(250m, row.ExpenseAmount);
        Assert.Equal(50m, worksheet.Value.BankAmount);
        Assert.Equal(1, await context.FinancialOperations.CountAsync(operation =>
            operation.OperationKind == FinancialOperationKinds.Expense &&
            operation.SupplierId == supplierId &&
            operation.ExpenseTypeId == expenseTypeId));
        Assert.Equal(1, await context.AuditEvents.CountAsync(audit => audit.Action == "finance.expense_created"));
    }

    [PostgreSqlFact]
    public async Task CashAndBankInvariant_DistinguishesBankTransferFromFundRedistributionOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        context.FinancialOperations.RemoveRange(context.FinancialOperations);
        context.FundOperations.RemoveRange(context.FundOperations);
        context.Funds.RemoveRange(context.Funds);
        var month = new DateOnly(2026, 6, 1);
        var garage = new Garage { Number = "PG-INVARIANT", PeopleCount = 1, FloorCount = 1 };
        var incomeType = new IncomeType { Name = "Инвариант PG", Code = "pg_balance_invariant" };
        var bankFund = new Fund { Name = "Банк инварианта PG", NormalizedName = "БАНК ИНВАРИАНТА PG", AllowOperations = true };
        var reserveFund = new Fund { Name = "Резерв инварианта PG", NormalizedName = "РЕЗЕРВ ИНВАРИАНТА PG", AllowOperations = true };
        context.AddRange(garage, incomeType, bankFund, reserveFund);
        await context.SaveChangesAsync();
        var financeService = FinanceServiceTestFactory.Create(context);
        var fundService = new FundService(
            new EfFundRepository(context),
            new EfFinanceAvailableBalanceQuery(context),
            new AuditEventWriter(context));

        async Task AssertInvariantAsync(decimal expectedCash, decimal expectedBank)
        {
            var worksheet = await financeService.GetExpenseWorksheetAsync(new ExpenseWorksheetRequest(month), CancellationToken.None);
            var activeIncome = await context.FinancialOperations
                .Where(operation => !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income)
                .SumAsync(operation => operation.Amount);
            var activeExpense = await context.FinancialOperations
                .Where(operation => !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Expense)
                .SumAsync(operation => operation.Amount);

            Assert.True(worksheet.Succeeded);
            Assert.Equal(expectedCash, worksheet.Value!.CashAmount);
            Assert.Equal(expectedBank, worksheet.Value.BankAmount);
            Assert.Equal(activeIncome - activeExpense, worksheet.Value.CashAmount + worksheet.Value.BankAmount);
        }

        var income = await financeService.CreateIncomeAsync(
            new CreateIncomeOperationRequest(garage.Id, incomeType.Id, new DateOnly(2026, 6, 10), month, 1000m, "PG-INV-INCOME", null),
            null,
            CancellationToken.None);
        var transfer = await fundService.CreateOperationAsync(
            bankFund.Id,
            new CreateFundOperationRequest("deposit", 400m, "Сдача кассы в банк PG", IsCashToBankTransfer: true),
            null,
            CancellationToken.None);
        Assert.True(income.Succeeded);
        Assert.True(transfer.Succeeded);
        await AssertInvariantAsync(600m, 400m);

        var withdrawal = await fundService.CreateOperationAsync(
            bankFund.Id,
            new CreateFundOperationRequest("withdraw", 200m, "Возврат для перераспределения PG"),
            null,
            CancellationToken.None);
        var redistribution = await fundService.CreateOperationAsync(
            reserveFund.Id,
            new CreateFundOperationRequest("deposit", 200m, "Перераспределение PG"),
            null,
            CancellationToken.None);
        Assert.True(withdrawal.Succeeded);
        Assert.True(redistribution.Succeeded);
        await AssertInvariantAsync(600m, 400m);

        Assert.True((await fundService.CancelOperationAsync(redistribution.Value!.Id, new CancelFundOperationRequest("Отмена перераспределения PG"), null, CancellationToken.None)).Succeeded);
        Assert.True((await fundService.CancelOperationAsync(withdrawal.Value!.Id, new CancelFundOperationRequest("Отмена изъятия PG"), null, CancellationToken.None)).Succeeded);
        Assert.True((await fundService.CancelOperationAsync(transfer.Value!.Id, new CancelFundOperationRequest("Отмена сдачи PG"), null, CancellationToken.None)).Succeeded);
        await AssertInvariantAsync(1000m, 0m);
        Assert.True((await fundService.RestoreOperationAsync(transfer.Value.Id, null, CancellationToken.None)).Succeeded);
        await AssertInvariantAsync(600m, 400m);
    }

    [PostgreSqlFact]
    public async Task AtomicCashExpense_SerializesConcurrentPayoutsAndKeepsCostEqualToPayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        Guid expenseTypeId;
        await using (var seedContext = database.CreateContext())
        {
            seedContext.FinancialOperations.RemoveRange(seedContext.FinancialOperations);
            seedContext.FundOperations.RemoveRange(seedContext.FundOperations);
            seedContext.Funds.RemoveRange(seedContext.Funds);
            var owner = new Owner { LastName = "Проверка", FirstName = "Атомарности" };
            var garage = new Garage { Number = "PG-ATOMIC", PeopleCount = 1, FloorCount = 1, Owner = owner };
            var incomeType = new IncomeType { Name = "Поступление для атомарной проверки PG", Code = "pg_atomic_income" };
            var supplierGroup = new SupplierGroup { Name = "Атомарные выплаты PG" };
            var supplier = new Supplier { Name = "Получатель атомарной выплаты PG", Group = supplierGroup };
            var expenseType = new ExpenseType { Name = "Авансовые выплаты PG", Code = "advance_payment" };
            supplierId = supplier.Id;
            expenseTypeId = expenseType.Id;
            seedContext.AddRange(
                owner,
                garage,
                incomeType,
                supplierGroup,
                supplier,
                expenseType,
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Income,
                    OperationDate = new DateOnly(2026, 6, 10),
                    AccountingMonth = new DateOnly(2026, 6, 1),
                    Amount = 100m,
                    Garage = garage,
                    IncomeType = incomeType
                });
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstService = FinanceServiceTestFactory.Create(firstContext);
        var secondService = FinanceServiceTestFactory.Create(secondContext);
        var firstRequest = new CreateExpenseOperationRequest(
            supplierId, expenseTypeId, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 70m, "PG-ATOMIC-1", null);
        var secondRequest = firstRequest with { DocumentNumber = "PG-ATOMIC-2" };

        var results = await Task.WhenAll(
            firstService.CreateExpenseAsync(firstRequest, Guid.NewGuid(), CancellationToken.None),
            secondService.CreateExpenseAsync(secondRequest, Guid.NewGuid(), CancellationToken.None));

        Assert.Single(results, result => result.Succeeded);
        var rejected = Assert.Single(results, result => !result.Succeeded);
        Assert.Equal("cash_amount_insufficient", rejected.ErrorCode);

        await using var assertionContext = database.CreateContext();
        Assert.Equal(1, await assertionContext.FinancialOperations.CountAsync(operation =>
            operation.OperationKind == FinancialOperationKinds.Expense &&
            operation.SupplierId == supplierId &&
            operation.ExpenseTypeId == expenseTypeId));
        Assert.Equal(70m, await assertionContext.SupplierAccruals
            .Where(accrual => accrual.SupplierId == supplierId && accrual.ExpenseTypeId == expenseTypeId)
            .SumAsync(accrual => accrual.Amount));
        Assert.Equal(70m, await assertionContext.FinancialOperations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Expense && operation.SupplierId == supplierId && operation.ExpenseTypeId == expenseTypeId)
            .SumAsync(operation => operation.Amount));
        Assert.Equal(1, await assertionContext.AuditEvents.CountAsync(audit => audit.Action == "finance.atomic_cash_expense_created"));
    }

    [PostgreSqlFact]
    public async Task ExpenseWorksheet_AggregatesOpeningBalancesOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var supplierGroup = new SupplierGroup { Name = "Проверка коммунальных услуг PG" };
            var supplier = new Supplier { Name = "Водоканал", Group = supplierGroup };
            var waterType = new ExpenseType { Name = "Проверка водоснабжения PG", Code = "pg_water" };
            var salaryType = await seedContext.ExpenseTypes.SingleAsync(item => item.Code == "salary");
            var department = new StaffDepartment { Name = "Проверка бухгалтерии PG" };
            var staffMember = new StaffMember
            {
                FullName = "Петрова Ольга",
                Department = department,
                Rate = 100m,
                CreatedAtUtc = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)
            };
            seedContext.AddRange(
                supplierGroup,
                supplier,
                waterType,
                department,
                staffMember,
                new SupplierAccrual
                {
                    Supplier = supplier,
                    ExpenseType = waterType,
                    AccountingMonth = new DateOnly(2026, 1, 1),
                    Amount = 100m,
                    Source = AccrualSources.Manual
                },
                new SupplierAccrual
                {
                    Supplier = supplier,
                    ExpenseType = waterType,
                    AccountingMonth = new DateOnly(2026, 2, 1),
                    Amount = 30m,
                    Source = AccrualSources.Manual
                },
                CreateExpense(supplier, null, waterType, 40m),
                CreateExpense(null, staffMember, salaryType, 50m));
            await seedContext.SaveChangesAsync();
        }

        await using var assertionContext = database.CreateContext();
        var result = await FinanceServiceTestFactory.Create(assertionContext).GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 2, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(110m, result.Value!.OpeningBalanceTotal);
        Assert.Equal(110m, result.Value.OpeningDebtTotal);
        Assert.Equal(0m, result.Value.OpeningAdvanceTotal);
        var supplierRow = Assert.Single(result.Value.Rows, row => row.SupplierId.HasValue);
        Assert.Equal(60m, supplierRow.OpeningBalance);
        Assert.Equal(60m, supplierRow.OpeningDebt);
        Assert.Equal(90m, supplierRow.ClosingDebt);
        var staffRow = Assert.Single(result.Value.Rows, row => row.StaffMemberId.HasValue);
        Assert.Equal(50m, staffRow.OpeningBalance);
        Assert.Equal(50m, staffRow.OpeningDebt);
        Assert.Equal(150m, staffRow.ClosingDebt);
        Assert.Equal(240m, result.Value.ClosingDebtTotal);
        Assert.Equal(0m, result.Value.ClosingAdvanceTotal);
    }

    [PostgreSqlFact]
    public async Task ExpenseWorksheet_CarriesDebtAndAdvanceAcrossMonthsWithoutPersistingTransfers()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        Guid expenseTypeId;
        await using (var seedContext = database.CreateContext())
        {
            var supplierGroup = new SupplierGroup { Name = "Перенос выплат PG" };
            var supplier = new Supplier { Name = "Поставщик переноса PG", Group = supplierGroup };
            var expenseType = new ExpenseType { Name = "Услуга переноса PG", Code = "pg_carry_service" };
            supplierId = supplier.Id;
            expenseTypeId = expenseType.Id;
            seedContext.AddRange(
                supplierGroup,
                supplier,
                expenseType,
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 1, 1), 100m),
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 2, 1), 200m),
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 3, 1), 100m),
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 4, 1), 80m),
                CreateExpense(supplier, null, expenseType, new DateOnly(2026, 1, 1), 100m),
                CreateExpense(supplier, null, expenseType, new DateOnly(2026, 2, 1), 50m),
                CreateExpense(supplier, null, expenseType, new DateOnly(2026, 3, 1), 300m));
            await seedContext.SaveChangesAsync();
        }

        await using var assertionContext = database.CreateContext();
        var service = FinanceServiceTestFactory.Create(assertionContext);
        var february = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 2, 1)), CancellationToken.None);
        var march = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 3, 1)), CancellationToken.None);
        var april = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 4, 1)), CancellationToken.None);
        var repeatedApril = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 4, 1)), CancellationToken.None);

        AssertExpenseCarry(FindRow(february.Value!, supplierId, expenseTypeId), 0m, 0m, 150m, 0m);
        AssertExpenseCarry(FindRow(march.Value!, supplierId, expenseTypeId), 150m, 0m, 0m, 50m);
        AssertExpenseCarry(FindRow(april.Value!, supplierId, expenseTypeId), 0m, 50m, 30m, 0m);
        AssertExpenseCarry(FindRow(repeatedApril.Value!, supplierId, expenseTypeId), 0m, 50m, 30m, 0m);
        Assert.Equal(4, await assertionContext.SupplierAccruals.CountAsync());
        Assert.Equal(3, await assertionContext.FinancialOperations.CountAsync());
    }

    private static SupplierAccrual CreateAccrual(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly accountingMonth,
        decimal amount) =>
        new()
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = accountingMonth,
            Amount = amount,
            Source = AccrualSources.Manual
        };

    private static FinancialOperation CreateExpense(
        Supplier? supplier,
        StaffMember? staffMember,
        ExpenseType expenseType,
        decimal amount) => CreateExpense(
            supplier,
            staffMember,
            expenseType,
            new DateOnly(2026, 1, 1),
            amount);

    private static FinancialOperation CreateExpense(
        Supplier? supplier,
        StaffMember? staffMember,
        ExpenseType expenseType,
        DateOnly accountingMonth,
        decimal amount) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = accountingMonth.AddDays(19),
            AccountingMonth = accountingMonth,
            Amount = amount,
            Supplier = supplier,
            StaffMember = staffMember,
            ExpenseType = expenseType
        };

    private static ExpenseWorksheetRowDto FindRow(ExpenseWorksheetDto worksheet, Guid supplierId, Guid expenseTypeId) =>
        Assert.Single(worksheet.Rows, row => row.SupplierId == supplierId && row.ExpenseTypeId == expenseTypeId);

    private static void AssertExpenseCarry(
        ExpenseWorksheetRowDto row,
        decimal openingDebt,
        decimal openingAdvance,
        decimal closingDebt,
        decimal closingAdvance)
    {
        Assert.Equal(openingDebt, row.OpeningDebt);
        Assert.Equal(openingAdvance, row.OpeningAdvance);
        Assert.Equal(closingDebt, row.ClosingDebt);
        Assert.Equal(closingAdvance, row.ClosingAdvance);
    }
}
