using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Tests.Finance;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GarageBalance.Api.Tests.Funds;

public sealed class PostgreSqlCustomerFundReconciliationIntegrationTests
{
    private static readonly DateOnly OperationDate = new(2026, 8, 31);

    [PostgreSqlFact]
    public async Task CashBankCorrectionsAndFundOperations_KeepPostgreSqlLedgerReconciled()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var actorUserId = Guid.NewGuid();
        Guid replenishedFundId;
        Guid reserveFundId;

        await using (var openingContext = database.CreateContext())
        {
            var opening = await CreateCashBankService(openingContext).UpdateOpeningBalancesAsync(
                new UpdateCashBankOpeningBalancesRequest(
                    CashOpeningBalance: 250m,
                    BankOpeningBalance: 250m,
                    Reason: "Начальные остатки для сквозной сверки"),
                actorUserId,
                CancellationToken.None);

            Assert.True(opening.Succeeded, opening.ErrorMessage);
            Assert.Equal(250m, opening.Value!.CashCurrentBalance);
            Assert.Equal(250m, opening.Value.BankCurrentBalance);
        }

        await AssertReconciledAsync(database, 500m, 500m, 0m);

        await using (var allocationContext = database.CreateContext())
        {
            var fundService = CreateFundService(allocationContext);
            var operationalFunds = (await fundService.GetFundsAsync(CancellationToken.None))
                .Where(fund => fund.AllowOperations)
                .Take(2)
                .ToArray();
            Assert.Equal(2, operationalFunds.Length);
            replenishedFundId = operationalFunds[0].Id;
            reserveFundId = operationalFunds[1].Id;

            var firstAllocation = await fundService.CreateOperationAsync(
                replenishedFundId,
                new CreateFundOperationRequest(
                    FundOperationKinds.Deposit,
                    120m,
                    "Первичное распределение в фонд с будущим перерасходом"),
                actorUserId,
                CancellationToken.None);
            var secondAllocation = await fundService.CreateOperationAsync(
                reserveFundId,
                new CreateFundOperationRequest(
                    FundOperationKinds.Deposit,
                    200m,
                    "Первичное распределение в резервный фонд"),
                actorUserId,
                CancellationToken.None);

            Assert.True(firstAllocation.Succeeded, firstAllocation.ErrorMessage);
            Assert.True(secondAllocation.Succeeded, secondAllocation.ErrorMessage);
        }

        await AssertReconciledAsync(database, 500m, 180m, 320m);

        // Реальное подтвержденное списание может увести целевой фонд в минус.
        // Связанная операция фонда исключает это списание из нераспределенного пула,
        // поэтому обе стороны бухгалтерского равенства уменьшаются на одну сумму.
        await using (var expenseContext = database.CreateContext())
        {
            var fund = await expenseContext.Funds.SingleAsync(item => item.Id == replenishedFundId);
            var expense = new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = OperationDate,
                AccountingMonth = new DateOnly(2026, 8, 1),
                Amount = 170m,
                ExpensePaymentType = ExpensePaymentTypes.WithReceipt,
                ExpensePaymentSource = ExpensePaymentSources.Bank,
                ExpenseFundId = fund.Id,
                NegativeFundBalanceConfirmed = true,
                DocumentNumber = "PG-CUSTOMER-FUND-NEGATIVE",
                Comment = "Подтвержденный перерасход фонда"
            };
            var createdAt = DateTimeOffset.UtcNow;
            expense.CreatedAtUtc = createdAt;
            expense.UpdatedAtUtc = createdAt;
            expenseContext.FinancialOperations.Add(expense);
            expenseContext.FundOperations.Add(new FundOperation
            {
                Fund = fund,
                SourceFinancialOperation = expense,
                OperationKind = FundOperationKinds.Withdraw,
                Amount = 170m,
                BalanceBefore = 120m,
                BalanceAfter = -50m,
                Reason = "Подтвержденная выплата из фонда",
                ActorUserId = actorUserId,
                CreatedAtUtc = createdAt
            });
            fund.Balance = -50m;
            fund.Version = Guid.NewGuid();
            fund.UpdatedAtUtc = createdAt;
            await expenseContext.SaveChangesAsync();
        }

        await AssertReconciledAsync(database, 330m, 180m, 150m);

        // Пункт 19: отрицательный фонд разрешено пополнить из нераспределенного остатка.
        await using (var replenishContext = database.CreateContext())
        {
            var replenishment = await CreateFundService(replenishContext).CreateOperationAsync(
                replenishedFundId,
                new CreateFundOperationRequest(
                    FundOperationKinds.Deposit,
                    30m,
                    "Погашение отрицательного остатка фонда"),
                actorUserId,
                CancellationToken.None);

            Assert.True(replenishment.Succeeded, replenishment.ErrorMessage);
            Assert.Equal(-50m, replenishment.Value!.BalanceBefore);
            Assert.Equal(-20m, replenishment.Value.BalanceAfter);
        }

        await AssertReconciledAsync(database, 330m, 150m, 180m);

        // Пункт 20: каждое увеличение кассы или банка пополняет общий пул.
        await using (var increaseContext = database.CreateContext())
        {
            var settings = CreateCashBankService(increaseContext);
            var cashIncrease = await settings.CreateAdjustmentAsync(
                new CreateCashBankBalanceAdjustmentRequest(
                    CashBankAccounts.Cash,
                    CashBankBalanceDirections.Increase,
                    OperationDate,
                    40m,
                    "Выявлен излишек кассы"),
                actorUserId,
                CancellationToken.None);
            var bankIncrease = await settings.CreateAdjustmentAsync(
                new CreateCashBankBalanceAdjustmentRequest(
                    CashBankAccounts.Bank,
                    CashBankBalanceDirections.Increase,
                    OperationDate,
                    60m,
                    "Уточнено зачисление банка"),
                actorUserId,
                CancellationToken.None);

            Assert.True(cashIncrease.Succeeded, cashIncrease.ErrorMessage);
            Assert.True(bankIncrease.Succeeded, bankIncrease.ErrorMessage);
            Assert.Equal(290m, bankIncrease.Value!.CashCurrentBalance);
            Assert.Equal(140m, bankIncrease.Value.BankCurrentBalance);
        }

        await AssertReconciledAsync(database, 430m, 250m, 180m);

        // Пункт 21: уменьшение сначала исчерпывает 250 ₽ общего пула,
        // а недостающие 30 ₽ снимает с первого положительного фонда.
        await using (var decreaseContext = database.CreateContext())
        {
            var decrease = await CreateCashBankService(decreaseContext).CreateAdjustmentAsync(
                new CreateCashBankBalanceAdjustmentRequest(
                    CashBankAccounts.Cash,
                    CashBankBalanceDirections.Decrease,
                    OperationDate,
                    280m,
                    "Исправление завышенного остатка кассы"),
                actorUserId,
                CancellationToken.None);

            Assert.True(decrease.Succeeded, decrease.ErrorMessage);
            Assert.Equal(10m, decrease.Value!.CashCurrentBalance);
            Assert.Equal(140m, decrease.Value.BankCurrentBalance);
        }

        await AssertReconciledAsync(database, 150m, 0m, 150m);
        await using (var decreaseVerificationContext = database.CreateContext())
        {
            Assert.Equal(
                -20m,
                await decreaseVerificationContext.Funds
                    .Where(fund => fund.Id == replenishedFundId)
                    .Select(fund => fund.Balance)
                    .SingleAsync());
            Assert.Equal(
                170m,
                await decreaseVerificationContext.Funds
                    .Where(fund => fund.Id == reserveFundId)
                    .Select(fund => fund.Balance)
                    .SingleAsync());
            var automaticWithdrawal = await decreaseVerificationContext.FundOperations
                .SingleAsync(operation =>
                    operation.FundId == reserveFundId &&
                    operation.Reason.StartsWith("Уменьшение остатка"));
            Assert.Equal(30m, automaticWithdrawal.Amount);
            Assert.Equal(200m, automaticWithdrawal.BalanceBefore);
            Assert.Equal(170m, automaticWithdrawal.BalanceAfter);
        }

        // Пункт 22: дополнительные ручные операции фондов также не нарушают итог.
        await using (var finalFundContext = database.CreateContext())
        {
            var fundService = CreateFundService(finalFundContext);
            var withdrawal = await fundService.CreateOperationAsync(
                reserveFundId,
                new CreateFundOperationRequest(
                    FundOperationKinds.Withdraw,
                    50m,
                    "Возврат части резерва в общий пул"),
                actorUserId,
                CancellationToken.None);
            var closeNegativeBalance = await fundService.CreateOperationAsync(
                replenishedFundId,
                new CreateFundOperationRequest(
                    FundOperationKinds.Deposit,
                    20m,
                    "Закрытие отрицательного остатка фонда"),
                actorUserId,
                CancellationToken.None);
            var redistributeRemainder = await fundService.CreateOperationAsync(
                reserveFundId,
                new CreateFundOperationRequest(
                    FundOperationKinds.Deposit,
                    30m,
                    "Распределение остатка после сверки"),
                actorUserId,
                CancellationToken.None);

            Assert.True(withdrawal.Succeeded, withdrawal.ErrorMessage);
            Assert.True(closeNegativeBalance.Succeeded, closeNegativeBalance.ErrorMessage);
            Assert.True(redistributeRemainder.Succeeded, redistributeRemainder.ErrorMessage);
        }

        await AssertReconciledAsync(database, 150m, 0m, 150m);
        await using var auditContext = database.CreateContext();
        Assert.Equal(
            2,
            await auditContext.AuditEvents.CountAsync(item => item.Action == "cash_bank_balance.increased"));
        Assert.Single(
            await auditContext.AuditEvents
                .Where(item => item.Action == "cash_bank_balance.decreased")
                .ToListAsync());
        Assert.Single(
            await auditContext.AuditEvents
                .Where(item => item.Action == "fund.balance_used_for_cash_bank_decrease")
                .ToListAsync());
        Assert.Equal(5, await auditContext.CashBankBalanceOperations.CountAsync());
    }

    [PostgreSqlFact]
    public async Task ConfirmedNegativeFundExpense_ServiceCreateCancelRestore_RebuildsPostgreSqlTailAndAudit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (supplierId, expenseTypeId, expenseFundId) = await SeedSupplierExpenseFundAsync(
            database,
            "PG confirmed negative lifecycle",
            200m,
            100m);
        await AssertReconciledAsync(database, 200m, 100m, 100m);

        Guid expenseId;
        await using (var lifecycleContext = database.CreateContext())
        {
            var finance = FinanceServiceTestFactory.Create(lifecycleContext);
            var created = await finance.CreateExpenseAsync(
                new CreateExpenseOperationRequest(
                    supplierId,
                    expenseTypeId,
                    new DateOnly(2026, 8, 20),
                    new DateOnly(2026, 8, 1),
                    150m,
                    "PG-NEGATIVE-CONFIRMED",
                    null,
                    ExpensePaymentTypes.WithReceipt,
                    ExpensePaymentSources.Bank,
                    expenseFundId,
                    null,
                    true),
                Guid.NewGuid(),
                CancellationToken.None);
            Assert.True(created.Succeeded, created.ErrorMessage);
            Assert.True(created.Value!.NegativeFundBalanceConfirmed);
            expenseId = created.Value.Id;
            Assert.True((await finance.CancelOperationAsync(
                expenseId,
                new CancelFinanceEntryRequest("PG cancel confirmed-negative expense"),
                Guid.NewGuid(),
                CancellationToken.None)).Succeeded);
            var restored = await finance.RestoreOperationAsync(
                expenseId,
                Guid.NewGuid(),
                CancellationToken.None);
            Assert.True(restored.Succeeded, restored.ErrorMessage);
            Assert.True(restored.Value!.NegativeFundBalanceConfirmed);
        }

        await AssertReconciledAsync(database, 50m, 100m, -50m);
        await using var verificationContext = database.CreateContext();
        Assert.Equal(-50m, await verificationContext.Funds
            .Where(item => item.Id == expenseFundId)
            .Select(item => item.Balance)
            .SingleAsync());
        var disbursement = await verificationContext.FundOperations
            .SingleAsync(item => item.SourceFinancialOperationId == expenseId);
        Assert.False(disbursement.IsCanceled);
        Assert.Equal(100m, disbursement.BalanceBefore);
        Assert.Equal(-50m, disbursement.BalanceAfter);
        var audit = await verificationContext.AuditEvents
            .SingleAsync(item => item.Action == "fund.expense_disbursement_restored");
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(
            "True",
            metadata.RootElement.GetProperty("negativeBalanceConfirmed").GetString());
    }

    [PostgreSqlFact]
    public async Task PositiveFundExpenseConfirmation_DoesNotAuthorizeLaterNegativeRestore()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (supplierId, expenseTypeId, expenseFundId) = await SeedSupplierExpenseFundAsync(
            database,
            "PG stale negative authorization",
            250m,
            100m);

        Guid earlyExpenseId;
        await using (var lifecycleContext = database.CreateContext())
        {
            var finance = FinanceServiceTestFactory.Create(lifecycleContext);
            var early = await finance.CreateExpenseAsync(
                new CreateExpenseOperationRequest(
                    supplierId,
                    expenseTypeId,
                    new DateOnly(2026, 8, 20),
                    new DateOnly(2026, 8, 1),
                    50m,
                    "PG-POSITIVE-CONFIRMATION",
                    null,
                    ExpensePaymentTypes.WithReceipt,
                    ExpensePaymentSources.Bank,
                    expenseFundId,
                    null,
                    true),
                Guid.NewGuid(),
                CancellationToken.None);
            var later = await finance.CreateExpenseAsync(
                new CreateExpenseOperationRequest(
                    supplierId,
                    expenseTypeId,
                    new DateOnly(2026, 8, 21),
                    new DateOnly(2026, 8, 1),
                    100m,
                    "PG-LATER-NEGATIVE",
                    null,
                    ExpensePaymentTypes.WithReceipt,
                    ExpensePaymentSources.Bank,
                    expenseFundId,
                    null,
                    true),
                Guid.NewGuid(),
                CancellationToken.None);
            Assert.True(early.Succeeded, early.ErrorMessage);
            Assert.False(early.Value!.NegativeFundBalanceConfirmed);
            Assert.True(later.Succeeded, later.ErrorMessage);
            Assert.True(later.Value!.NegativeFundBalanceConfirmed);
            earlyExpenseId = early.Value.Id;

            var canceled = await finance.CancelOperationAsync(
                earlyExpenseId,
                new CancelFinanceEntryRequest("PG cancel positive-tail expense"),
                Guid.NewGuid(),
                CancellationToken.None);
            Assert.True(canceled.Succeeded, canceled.ErrorMessage);
            var restored = await finance.RestoreOperationAsync(
                earlyExpenseId,
                Guid.NewGuid(),
                CancellationToken.None);
            Assert.False(restored.Succeeded);
            Assert.Equal("fund_balance_insufficient", restored.ErrorCode);
        }

        await AssertReconciledAsync(database, 150m, 150m, 0m);
        await using var verificationContext = database.CreateContext();
        var operation = await verificationContext.FinancialOperations
            .SingleAsync(item => item.Id == earlyExpenseId);
        Assert.True(operation.IsCanceled);
        Assert.False(operation.NegativeFundBalanceConfirmed);
        Assert.True((await verificationContext.FundOperations
            .SingleAsync(item => item.SourceFinancialOperationId == earlyExpenseId)).IsCanceled);
        Assert.Equal(0m, await verificationContext.Funds
            .Where(item => item.Id == expenseFundId)
            .Select(item => item.Balance)
            .SingleAsync());
    }

    private static async Task<(Guid SupplierId, Guid ExpenseTypeId, Guid ExpenseFundId)> SeedSupplierExpenseFundAsync(
        PostgreSqlTestDatabase database,
        string name,
        decimal bankOpeningBalance,
        decimal fundOpeningBalance)
    {
        await using var context = database.CreateContext();
        var normalizedName = name.ToUpperInvariant();
        var expenseFund = new Fund
        {
            Name = name,
            NormalizedName = normalizedName,
            Balance = 0m,
            AllowOperations = true
        };
        var expenseType = new ExpenseType { Name = $"{name} expense", Code = $"pg_{Guid.NewGuid():N}" };
        var supplierGroup = new SupplierGroup { Name = $"{name} suppliers" };
        var serviceSetting = new ChargeServiceSetting { Name = $"{name} service" };
        var supplier = new Supplier
        {
            Name = $"{name} supplier",
            StartingBalance = bankOpeningBalance,
            Group = supplierGroup,
            ExpenseType = expenseType,
            ExpenseFund = expenseFund,
            ChargeServiceSetting = serviceSetting
        };
        context.AddRange(expenseFund, expenseType, supplierGroup, serviceSetting, supplier);
        await context.SaveChangesAsync();

        var opening = await CreateCashBankService(context).UpdateOpeningBalancesAsync(
            new UpdateCashBankOpeningBalancesRequest(
                CashOpeningBalance: 0m,
                BankOpeningBalance: bankOpeningBalance,
                Reason: $"Opening balance for {name}"),
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.True(opening.Succeeded, opening.ErrorMessage);
        var allocation = await CreateFundService(context).CreateOperationAsync(
            expenseFund.Id,
            new CreateFundOperationRequest(
                FundOperationKinds.Deposit,
                fundOpeningBalance,
                $"Opening allocation for {name}"),
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.True(allocation.Succeeded, allocation.ErrorMessage);
        return (supplier.Id, expenseType.Id, expenseFund.Id);
    }

    private static FundService CreateFundService(GarageBalanceDbContext context) =>
        new(new EfFundRepository(context), new AuditEventWriter(context));

    private static CashBankBalanceSettingsService CreateCashBankService(GarageBalanceDbContext context) =>
        new(
            new EfCashBankBalanceOperationRepository(context),
            new EfFundRepository(context),
            new EfFinanceAvailableBalanceQuery(context),
            new EfApplicationUnitOfWork(context),
            new AuditEventWriter(context),
            new TestBusinessDateProvider(OperationDate),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

    private static async Task AssertReconciledAsync(
        PostgreSqlTestDatabase database,
        decimal expectedCashAndBank,
        decimal expectedUnallocated,
        decimal expectedFundTotal)
    {
        await using var context = database.CreateContext();
        var cashBank = await CreateCashBankService(context).GetAsync(CancellationToken.None);
        var unallocated = await new EfFundRepository(context)
            .GetAvailableToDistributeAsync(CancellationToken.None);
        var fundTotal = await context.Funds
            .Where(fund => !fund.IsArchived)
            .SumAsync(fund => fund.Balance);
        var cashAndBank = cashBank.CashCurrentBalance + cashBank.BankCurrentBalance;

        Assert.Equal(expectedCashAndBank, cashAndBank);
        Assert.Equal(expectedUnallocated, unallocated);
        Assert.Equal(expectedFundTotal, fundTotal);
        Assert.Equal(cashAndBank, unallocated + fundTotal);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
