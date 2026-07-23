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
            await ClearIncomeDestinationLinksAsync(seedContext);
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
        Assert.Equal(-150m, row.Difference);
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
        await ClearIncomeDestinationLinksAsync(context);
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
            await ClearIncomeDestinationLinksAsync(seedContext);
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
    public async Task ExpenseWorksheet_CarriesUnusedCollectionsAcrossMonthsOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        Guid expenseTypeId;
        await using (var seedContext = database.CreateContext())
        {
            await ClearIncomeDestinationLinksAsync(seedContext);
            seedContext.FinancialOperations.RemoveRange(seedContext.FinancialOperations);
            var garage = new Garage { Number = "PG-COLLECTION-CARRY", PeopleCount = 1, FloorCount = 1 };
            var supplierGroup = new SupplierGroup { Name = "Перенос собранных средств PG" };
            var supplier = new Supplier { Name = "Энергосбыт PG", Group = supplierGroup };
            var incomeType = new IncomeType { Name = "Электроэнергия PG", Code = "pg_electricity_carry" };
            var expenseType = new ExpenseType { Name = "Электроэнергия PG", Code = "pg_electricity_carry" };
            var june = new DateOnly(2026, 6, 1);
            var july = new DateOnly(2026, 7, 1);
            supplierId = supplier.Id;
            expenseTypeId = expenseType.Id;
            seedContext.AddRange(
                garage,
                supplierGroup,
                supplier,
                incomeType,
                expenseType,
                CreateAccrual(supplier, expenseType, june, 12000m),
                CreateAccrual(supplier, expenseType, july, 2000m),
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Income,
                    OperationDate = june.AddDays(10),
                    AccountingMonth = june,
                    Amount = 9243.81m,
                    Garage = garage,
                    IncomeType = incomeType
                },
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Income,
                    OperationDate = july.AddDays(10),
                    AccountingMonth = july,
                    Amount = 1000m,
                    Garage = garage,
                    IncomeType = incomeType
                },
                CreateExpense(supplier, null, expenseType, july, 4000m));
            await seedContext.SaveChangesAsync();
        }

        await using var assertionContext = database.CreateContext();
        var service = FinanceServiceTestFactory.Create(assertionContext);
        var julyWorksheet = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        var augustWorksheet = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 8, 1)),
            CancellationToken.None);

        Assert.True(julyWorksheet.Succeeded);
        Assert.True(augustWorksheet.Succeeded);
        var julyValue = julyWorksheet.Value!;
        var julyRow = FindRow(julyValue, supplierId, expenseTypeId);
        Assert.Equal(10243.81m, julyRow.CollectedAmount);
        Assert.Equal(6243.81m, julyRow.Difference);
        Assert.Equal(10243.81m, julyValue.CollectedTotal);
        Assert.Equal(6243.81m, julyValue.DifferenceTotal);
        var augustRow = FindRow(augustWorksheet.Value!, supplierId, expenseTypeId);
        Assert.Equal(6243.81m, augustRow.CollectedAmount);
        Assert.Equal(6243.81m, augustRow.Difference);
        Assert.Equal(3, await assertionContext.FinancialOperations.CountAsync());
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

    [PostgreSqlFact]
    public async Task SupplierThreeMonthScenario_KeepsAdvanceWithinExpenseTypeAndRecalculatesAfterCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        Guid serviceTypeId;
        Guid repairTypeId;
        await using (var seedContext = database.CreateContext())
        {
            await ClearIncomeDestinationLinksAsync(seedContext);
            seedContext.FinancialOperations.RemoveRange(seedContext.FinancialOperations);
            seedContext.FundOperations.RemoveRange(seedContext.FundOperations);
            seedContext.Funds.RemoveRange(seedContext.Funds);
            var supplierGroup = new SupplierGroup { Name = "Трёхмесячный сценарий поставщика PG" };
            var supplier = new Supplier { Name = "Поставщик трёхмесячного сценария PG", Group = supplierGroup };
            var serviceType = new ExpenseType { Name = "Основная услуга сценария PG", Code = "pg_supplier_three_month_service" };
            var repairType = new ExpenseType { Name = "Ремонт сценария PG", Code = "pg_supplier_three_month_repair" };
            var bankFund = new Fund
            {
                Name = "Банк трёхмесячного сценария PG",
                NormalizedName = "БАНК ТРЁХМЕСЯЧНОГО СЦЕНАРИЯ PG",
                Balance = 1000m,
                AllowOperations = true
            };
            supplierId = supplier.Id;
            serviceTypeId = serviceType.Id;
            repairTypeId = repairType.Id;
            seedContext.AddRange(
                supplierGroup,
                supplier,
                serviceType,
                repairType,
                bankFund,
                new FundOperation
                {
                    Fund = bankFund,
                    OperationKind = FundOperationKinds.Deposit,
                    Amount = 1000m,
                    BalanceBefore = 0m,
                    BalanceAfter = 1000m,
                    Reason = "Банковский остаток для трёхмесячного сценария PG",
                    IsCashToBankTransfer = true,
                    CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                });
            await seedContext.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var service = FinanceServiceTestFactory.Create(context);
        var actorUserId = Guid.NewGuid();
        var januaryAccrual = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(
                supplierId, serviceTypeId, new DateOnly(2026, 1, 1), 100m, "manual", "PG-SUP-01", "Счёт за январь PG"),
            actorUserId,
            CancellationToken.None);
        var januaryPartialPayment = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                supplierId, serviceTypeId, new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 1), 40m, "PG-PAY-01", null),
            actorUserId,
            CancellationToken.None);
        var februaryAdvancePayment = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                supplierId, serviceTypeId, new DateOnly(2026, 2, 20), new DateOnly(2026, 2, 1), 100m, "PG-PAY-02", null),
            actorUserId,
            CancellationToken.None);
        var februaryRepairAccrual = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(
                supplierId, repairTypeId, new DateOnly(2026, 2, 1), 30m, "manual", "PG-REP-02", "Отдельный счёт за ремонт PG"),
            actorUserId,
            CancellationToken.None);
        var februaryRepairPayment = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                supplierId, repairTypeId, new DateOnly(2026, 2, 21), new DateOnly(2026, 2, 1), 10m, "PG-REP-PAY-02", null),
            actorUserId,
            CancellationToken.None);
        var marchAccrual = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(
                supplierId, serviceTypeId, new DateOnly(2026, 3, 1), 25m, "manual", "PG-SUP-03", "Счёт за март PG"),
            actorUserId,
            CancellationToken.None);

        Assert.True(januaryAccrual.Succeeded);
        Assert.True(januaryPartialPayment.Succeeded);
        Assert.Equal(100m, januaryPartialPayment.Value!.SupplierDebtBefore);
        Assert.Equal(60m, januaryPartialPayment.Value.SupplierDebtAfter);
        Assert.True(februaryAdvancePayment.Succeeded);
        Assert.Equal(60m, februaryAdvancePayment.Value!.SupplierDebtBefore);
        Assert.Equal(-40m, februaryAdvancePayment.Value.SupplierDebtAfter);
        Assert.Collection(
            februaryAdvancePayment.Value.PaymentAllocations,
            debtAllocation =>
            {
                Assert.Equal("month", debtAllocation.AllocationKind);
                Assert.Equal(new DateOnly(2026, 1, 1), debtAllocation.AccountingMonth);
                Assert.Equal(60m, debtAllocation.PaidAmount);
            },
            advanceAllocation =>
            {
                Assert.Equal("overpayment", advanceAllocation.AllocationKind);
                Assert.Equal(40m, advanceAllocation.PaidAmount);
                Assert.Equal(-40m, advanceAllocation.DebtAfter);
            });
        Assert.True(februaryRepairAccrual.Succeeded);
        Assert.True(februaryRepairPayment.Succeeded);
        Assert.Equal(30m, februaryRepairPayment.Value!.SupplierDebtBefore);
        Assert.Equal(20m, februaryRepairPayment.Value.SupplierDebtAfter);
        Assert.True(marchAccrual.Succeeded);

        var marchBeforeCancellation = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 3, 1)), CancellationToken.None);
        Assert.True(marchBeforeCancellation.Succeeded);
        AssertExpenseCarry(FindRow(marchBeforeCancellation.Value!, supplierId, serviceTypeId), 0m, 40m, 0m, 15m);
        AssertExpenseCarry(FindRow(marchBeforeCancellation.Value!, supplierId, repairTypeId), 20m, 0m, 20m, 0m);

        var cancellation = await service.CancelOperationAsync(
            februaryAdvancePayment.Value.Id,
            new CancelFinanceEntryRequest("Отмена февральской выплаты в трёхмесячном сценарии PG"),
            actorUserId,
            CancellationToken.None);
        var marchAfterCancellation = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 3, 1)), CancellationToken.None);

        Assert.True(cancellation.Succeeded);
        Assert.True(marchAfterCancellation.Succeeded);
        AssertExpenseCarry(FindRow(marchAfterCancellation.Value!, supplierId, serviceTypeId), 60m, 0m, 85m, 0m);
        AssertExpenseCarry(FindRow(marchAfterCancellation.Value!, supplierId, repairTypeId), 20m, 0m, 20m, 0m);
        Assert.Equal(3, await context.SupplierAccruals.CountAsync(accrual => !accrual.IsCanceled));
        Assert.Equal(2, await context.FinancialOperations.CountAsync(operation => !operation.IsCanceled));
        Assert.Equal(3, await context.AuditEvents.CountAsync(audit => audit.Action == "finance.supplier_accrual_created"));
        Assert.Equal(3, await context.AuditEvents.CountAsync(audit => audit.Action == "finance.expense_created"));
        Assert.Equal(1, await context.AuditEvents.CountAsync(audit => audit.Action == "finance.operation_canceled"));
    }

    [PostgreSqlFact]
    public async Task ExpenseWorksheet_RecalculatesEmptyMonthsAcrossYearAfterPreviousPaymentCancellationOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        Guid expenseTypeId;
        Guid decemberPaymentId;
        await using (var seedContext = database.CreateContext())
        {
            var supplierGroup = new SupplierGroup { Name = "Перерасчет на границе года PG" };
            var supplier = new Supplier { Name = "Поставщик перерасчета PG", Group = supplierGroup };
            var expenseType = new ExpenseType { Name = "Услуга перерасчета PG", Code = "pg_year_boundary_recalculation" };
            var decemberPayment = CreateExpense(supplier, null, expenseType, new DateOnly(2026, 12, 1), 40m);
            supplierId = supplier.Id;
            expenseTypeId = expenseType.Id;
            decemberPaymentId = decemberPayment.Id;
            seedContext.AddRange(
                supplierGroup,
                supplier,
                expenseType,
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 12, 1), 100m),
                CreateAccrual(supplier, expenseType, new DateOnly(2027, 2, 1), 50m),
                decemberPayment,
                CreateExpense(supplier, null, expenseType, new DateOnly(2027, 2, 1), 30m));
            await seedContext.SaveChangesAsync();
        }

        await using var assertionContext = database.CreateContext();
        var service = FinanceServiceTestFactory.Create(assertionContext);
        var december = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 12, 1)), CancellationToken.None);
        var emptyJanuary = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2027, 1, 1)), CancellationToken.None);
        var february = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2027, 2, 1)), CancellationToken.None);
        var emptyMarch = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2027, 3, 1)), CancellationToken.None);

        AssertExpenseCarry(FindRow(december.Value!, supplierId, expenseTypeId), 0m, 0m, 60m, 0m);
        AssertEmptyExpenseMonth(FindRow(emptyJanuary.Value!, supplierId, expenseTypeId), 60m);
        AssertExpenseCarry(FindRow(february.Value!, supplierId, expenseTypeId), 60m, 0m, 80m, 0m);
        AssertEmptyExpenseMonth(FindRow(emptyMarch.Value!, supplierId, expenseTypeId), 80m);

        var canceled = await service.CancelOperationAsync(
            decemberPaymentId,
            new CancelFinanceEntryRequest("Отмена прошлогодней выплаты PG"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(canceled.Succeeded);
        var recalculatedJanuary = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2027, 1, 1)), CancellationToken.None);
        var recalculatedFebruary = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2027, 2, 1)), CancellationToken.None);
        var recalculatedMarch = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2027, 3, 1)), CancellationToken.None);

        AssertEmptyExpenseMonth(FindRow(recalculatedJanuary.Value!, supplierId, expenseTypeId), 100m);
        AssertExpenseCarry(FindRow(recalculatedFebruary.Value!, supplierId, expenseTypeId), 100m, 0m, 120m, 0m);
        AssertEmptyExpenseMonth(FindRow(recalculatedMarch.Value!, supplierId, expenseTypeId), 120m);
        Assert.Single(assertionContext.AuditEvents, audit => audit.Action == "finance.operation_canceled");
        Assert.Equal(2, await assertionContext.SupplierAccruals.CountAsync());
        Assert.Equal(2, await assertionContext.FinancialOperations.CountAsync());
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

    private static void AssertEmptyExpenseMonth(ExpenseWorksheetRowDto row, decimal carriedDebt)
    {
        Assert.Equal(0m, row.AccrualAmount);
        Assert.Equal(0m, row.ExpenseAmount);
        AssertExpenseCarry(row, carriedDebt, 0m, carriedDebt, 0m);
    }

    private static Task ClearIncomeDestinationLinksAsync(GarageBalanceDbContext context) =>
        context.IncomeTypes
            .Where(item => item.DestinationFundId.HasValue)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DestinationFundId, (Guid?)null));
}
