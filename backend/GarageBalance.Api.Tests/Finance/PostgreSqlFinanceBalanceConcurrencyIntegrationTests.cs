using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFinanceBalanceConcurrencyIntegrationTests
{
    private static readonly DateOnly June = new(2026, 6, 1);

    [PostgreSqlFact]
    public async Task OpeningDebtPayments_RecheckRemainingDebtAfterConcurrentPayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var setup = database.CreateContext();
        var garage = new Garage { Number = "OPENING-DEBT-RACE", StartingBalance = 1000m };
        setup.Garages.Add(garage);
        if (!await setup.IncomeTypes.AnyAsync(item => item.Code == "debt_transfer"))
        {
            setup.IncomeTypes.Add(new IncomeType { Name = "Перенос задолженности", Code = "debt_transfer", IsSystem = true });
        }
        await setup.SaveChangesAsync();

        await using var firstContext = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(database.ConnectionString) { ApplicationName = "opening-debt-first" }.ConnectionString).Options);
        await using var secondContext = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(database.ConnectionString) { ApplicationName = "opening-debt-second" }.ConnectionString).Options);
        await using var blocker = new NpgsqlConnection(database.ConnectionString);
        await blocker.OpenAsync();
        const long fundAllocationLockKey = 0x474246554E44;
        await using (var acquire = new NpgsqlCommand($"SELECT pg_advisory_lock({fundAllocationLockKey})", blocker))
        {
            await acquire.ExecuteNonQueryAsync();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var first = FinanceServiceTestFactory.Create(firstContext).CreateGarageDebtPaymentAsync(
            new(garage.Id, June.AddDays(10), June, 700m, "Первая оплата"), null, timeout.Token);
        var second = FinanceServiceTestFactory.Create(secondContext).CreateGarageDebtPaymentAsync(
            new(garage.Id, June.AddDays(10), June, 700m, "Вторая оплата"), null, timeout.Token);
        try
        {
            // Both requests must reach the shared money lock before either can save.
            // This exposes an opening-debt read performed before the lock without
            // relying on task scheduling or on an arbitrary delay.
            await using var waiting = new NpgsqlCommand("""
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event = 'advisory'
                  AND application_name IN ('opening-debt-first', 'opening-debt-second')
                """, blocker);
            while ((long)(await waiting.ExecuteScalarAsync(timeout.Token))! < 2)
            {
                await Task.Delay(20, timeout.Token);
            }
        }
        finally
        {
            await using var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({fundAllocationLockKey})", blocker);
            await release.ExecuteNonQueryAsync();
        }

        var results = await Task.WhenAll(first, second);
        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => result.ErrorCode == "debt_payment_amount_exceeds_opening_debt");
        await using var assertion = database.CreateContext();
        Assert.Equal(700m, await assertion.FinancialOperations.Where(item => item.GarageId == garage.Id).SumAsync(item => item.Amount));
        Assert.Single(await assertion.AuditEvents.Where(item => item.Action == "finance.income_created").ToListAsync());
    }

    [PostgreSqlFact]
    public async Task StaffPayments_SerializeCashAndSalaryBalances()
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
                OpeningBalance(CashBankAccounts.Cash, 100m));
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
        Assert.Contains(rejected.ErrorCode, new[] { "staff_payment_amount_exceeds_available", "cash_amount_insufficient" });

        await using var assertionContext = database.CreateContext();
        Assert.Equal(70m, await assertionContext.FinancialOperations
            .Where(operation => !operation.IsCanceled && operation.StaffMemberId == staffMemberId)
            .SumAsync(operation => operation.Amount));
    }

    [PostgreSqlFact]
    public async Task StaffPenalties_SerializeMonthlySalaryBalance()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid staffMemberId;
        await using (var seedContext = database.CreateContext())
        {
            var department = new StaffDepartment { Name = "Отдел конкурентных штрафов" };
            var staffMember = new StaffMember
            {
                FullName = "Сотрудник конкурентных штрафов",
                Department = department,
                Rate = 100m,
                CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };
            staffMemberId = staffMember.Id;
            seedContext.AddRange(department, staffMember);
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var results = await Task.WhenAll(
            FinanceServiceTestFactory.Create(firstContext).CreateStaffSalaryAdjustmentAsync(
                new CreateStaffSalaryAdjustmentRequest(staffMemberId, June, "penalty", 60m, "PENALTY-RACE-1", "Первый конкурентный штраф"),
                Guid.NewGuid(),
                CancellationToken.None),
            FinanceServiceTestFactory.Create(secondContext).CreateStaffSalaryAdjustmentAsync(
                new CreateStaffSalaryAdjustmentRequest(staffMemberId, June, "penalty", 60m, "PENALTY-RACE-2", "Второй конкурентный штраф"),
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded && result.ErrorCode == "staff_penalty_exceeds_available");

        await using var assertionContext = database.CreateContext();
        Assert.Equal(60m, await assertionContext.StaffSalaryAdjustments
            .Where(adjustment => adjustment.StaffMemberId == staffMemberId && adjustment.AccountingMonth == June)
            .SumAsync(adjustment => adjustment.Amount));
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
    public async Task RestoredStaffPayments_CannotOverdrawSharedCashBalance()
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
                OpeningBalance(CashBankAccounts.Cash, 100m));
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var results = await Task.WhenAll(
            FinanceServiceTestFactory.Create(firstContext).RestoreOperationAsync(firstOperationId, Guid.NewGuid(), CancellationToken.None),
            FinanceServiceTestFactory.Create(secondContext).RestoreOperationAsync(secondOperationId, Guid.NewGuid(), CancellationToken.None));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded && result.ErrorCode == "cash_amount_insufficient");

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
            ExpensePaymentSource = ExpensePaymentSources.Cash,
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
