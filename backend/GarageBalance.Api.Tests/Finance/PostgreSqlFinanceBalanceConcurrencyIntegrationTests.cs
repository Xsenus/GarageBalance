using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFinanceBalanceConcurrencyIntegrationTests
{
    private static readonly DateOnly June = new(2026, 6, 1);

    [PostgreSqlFact]
    public async Task StaffPayments_SerializeBankAndSalaryBalances()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid staffMemberId;
        await using (var seedContext = database.CreateContext())
        {
            var department = new StaffDepartment { Name = "Бухгалтерия конкурентных выплат" };
            var staffMember = new StaffMember
            {
                FullName = "Сотрудник конкурентных выплат",
                Department = department,
                Rate = 100m,
                CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };
            staffMemberId = staffMember.Id;
            seedContext.AddRange(
                department,
                staffMember,
                OpeningBalance(CashBankAccounts.Bank, 100m));
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstService = FinanceServiceTestFactory.Create(firstContext);
        var secondService = FinanceServiceTestFactory.Create(secondContext);

        var results = await Task.WhenAll(
            firstService.CreateStaffPaymentAsync(
                new CreateStaffPaymentRequest(staffMemberId, June.AddDays(20), June, 70m, "SALARY-RACE-1", null),
                Guid.NewGuid(),
                CancellationToken.None),
            secondService.CreateStaffPaymentAsync(
                new CreateStaffPaymentRequest(staffMemberId, June.AddDays(21), June, 70m, "SALARY-RACE-2", null),
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Single(results, result => result.Succeeded);
        var rejected = Assert.Single(results, result => !result.Succeeded);
        Assert.Contains(rejected.ErrorCode, new[] { "staff_payment_amount_exceeds_available", "bank_amount_insufficient" });

        await using var assertionContext = database.CreateContext();
        Assert.Equal(70m, await assertionContext.FinancialOperations
            .Where(operation => !operation.IsCanceled && operation.StaffMemberId == staffMemberId)
            .SumAsync(operation => operation.Amount));
    }

    [PostgreSqlFact]
    public async Task IncomeReductionAndCashBankTransfer_CannotSpendTheSameCash()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fixture = await SeedIncomeAsync(database, 100m);
        await using var updateContext = database.CreateContext();
        await using var transferContext = database.CreateContext();
        var updateService = FinanceServiceTestFactory.Create(updateContext);
        var transferService = FinanceServiceTestFactory.Create(transferContext);

        var results = await Task.WhenAll(
            AsBalanceResult(updateService.UpdateIncomeAsync(
                fixture.OperationId,
                new CreateIncomeOperationRequest(
                    fixture.GarageId,
                    fixture.IncomeTypeId,
                    June.AddDays(10),
                    June,
                    20m,
                    "INCOME-RACE",
                    null),
                Guid.NewGuid(),
                CancellationToken.None)),
            AsBalanceResult(transferService.CreateCashBankTransferAsync(
                new CreateCashBankTransferRequest(June.AddDays(15), 70m, "Конкурентная сдача кассы"),
                Guid.NewGuid(),
                CancellationToken.None)));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded && result.ErrorCode == "cash_amount_insufficient");

        await AssertNonNegativeBalancesAsync(database);
    }

    [PostgreSqlFact]
    public async Task IncomeCancellationAndCashBankTransfer_CannotSpendTheSameCash()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fixture = await SeedIncomeAsync(database, 100m);
        await using var cancelContext = database.CreateContext();
        await using var transferContext = database.CreateContext();
        var cancelService = FinanceServiceTestFactory.Create(cancelContext);
        var transferService = FinanceServiceTestFactory.Create(transferContext);

        var results = await Task.WhenAll(
            AsBalanceResult(cancelService.CancelOperationAsync(
                fixture.OperationId,
                new CancelFinanceEntryRequest("Конкурентная отмена поступления"),
                Guid.NewGuid(),
                CancellationToken.None)),
            AsBalanceResult(transferService.CreateCashBankTransferAsync(
                new CreateCashBankTransferRequest(June.AddDays(15), 70m, "Конкурентная сдача кассы"),
                Guid.NewGuid(),
                CancellationToken.None)));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded && result.ErrorCode == "cash_amount_insufficient");

        await AssertNonNegativeBalancesAsync(database);
    }

    [PostgreSqlFact]
    public async Task RestoredStaffPayments_CannotOverdrawSharedBankBalance()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid firstOperationId;
        Guid secondOperationId;
        await using (var seedContext = database.CreateContext())
        {
            var department = new StaffDepartment { Name = "Отдел восстановления выплат" };
            var firstStaff = Staff("Первый сотрудник", department);
            var secondStaff = Staff("Второй сотрудник", department);
            var salaryType = await seedContext.ExpenseTypes.SingleAsync(type => type.Code == "salary");
            var firstOperation = CanceledStaffPayment(firstStaff, salaryType, "RESTORE-RACE-1");
            var secondOperation = CanceledStaffPayment(secondStaff, salaryType, "RESTORE-RACE-2");
            firstOperationId = firstOperation.Id;
            secondOperationId = secondOperation.Id;
            seedContext.AddRange(
                department,
                firstStaff,
                secondStaff,
                firstOperation,
                secondOperation,
                OpeningBalance(CashBankAccounts.Bank, 100m));
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var results = await Task.WhenAll(
            FinanceServiceTestFactory.Create(firstContext).RestoreOperationAsync(firstOperationId, Guid.NewGuid(), CancellationToken.None),
            FinanceServiceTestFactory.Create(secondContext).RestoreOperationAsync(secondOperationId, Guid.NewGuid(), CancellationToken.None));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded && result.ErrorCode == "bank_amount_insufficient");

        await using var assertionContext = database.CreateContext();
        Assert.Equal(70m, await assertionContext.FinancialOperations
            .Where(operation => !operation.IsCanceled && operation.StaffMemberId != null)
            .SumAsync(operation => operation.Amount));
    }

    private static async Task<(Guid OperationId, Guid GarageId, Guid IncomeTypeId)> SeedIncomeAsync(
        PostgreSqlTestDatabase database,
        decimal amount)
    {
        await using var context = database.CreateContext();
        var garage = new Garage { Number = $"BALANCE-RACE-{Guid.NewGuid():N}", PeopleCount = 1, FloorCount = 1 };
        var incomeType = new IncomeType { Name = $"Поступление для гонки {Guid.NewGuid():N}" };
        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = June.AddDays(10),
            AccountingMonth = June,
            Amount = amount,
            DocumentNumber = "INCOME-RACE",
            Garage = garage,
            IncomeType = incomeType
        };
        context.AddRange(garage, incomeType, operation);
        await context.SaveChangesAsync();
        return (operation.Id, garage.Id, incomeType.Id);
    }

    private static CashBankBalanceOperation OpeningBalance(string account, decimal amount) => new()
    {
        Account = account,
        OperationKind = CashBankBalanceOperationKinds.OpeningBalance,
        Direction = CashBankBalanceDirections.Increase,
        OperationDate = June,
        Amount = amount,
        Reason = "Начальный остаток для конкурентной проверки"
    };

    private static StaffMember Staff(string name, StaffDepartment department) => new()
    {
        FullName = name,
        Department = department,
        Rate = 100m,
        CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
    };

    private static FinancialOperation CanceledStaffPayment(
        StaffMember staffMember,
        ExpenseType salaryType,
        string documentNumber) => new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = June.AddDays(20),
            AccountingMonth = June,
            Amount = 70m,
            DocumentNumber = documentNumber,
            StaffMember = staffMember,
            ExpenseType = salaryType,
            ExpensePaymentSource = ExpensePaymentSources.Bank,
            IsCanceled = true
        };

    private static async Task<BalanceResult> AsBalanceResult<T>(Task<FinanceResult<T>> task)
    {
        var result = await task;
        return new BalanceResult(result.Succeeded, result.ErrorCode);
    }

    private static async Task AssertNonNegativeBalancesAsync(PostgreSqlTestDatabase database)
    {
        await using var context = database.CreateContext();
        var data = await new GarageBalance.Api.Infrastructure.Data.EfFinanceAvailableBalanceQuery(context)
            .GetAsync([], [], CancellationToken.None);
        var cashAmount = data.IncomeTotal - data.BankDepositTotal - data.CashExpenseTotal + data.CashAdjustmentTotal;
        var bankAmount = data.BankDepositTotal - data.BankExpenseTotal + data.BankAdjustmentTotal;
        Assert.True(cashAmount >= 0m);
        Assert.True(bankAmount >= 0m);
    }

    private sealed record BalanceResult(bool Succeeded, string? ErrorCode);
}
