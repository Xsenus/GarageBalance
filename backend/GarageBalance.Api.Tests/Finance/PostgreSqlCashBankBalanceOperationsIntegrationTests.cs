using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlCashBankBalanceOperationsIntegrationTests
{
    [PostgreSqlFact]
    public async Task MigrationPersistsValidOperationAndEnforcesFinancialConstraints()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            context.CashBankBalanceOperations.Add(new CashBankBalanceOperation
            {
                Account = CashBankAccounts.Cash,
                OperationKind = CashBankBalanceOperationKinds.OpeningBalance,
                Direction = CashBankBalanceDirections.Increase,
                OperationDate = new DateOnly(2026, 7, 27),
                Amount = 1500.25m,
                Reason = "Стартовый остаток кассы"
            });
            await context.SaveChangesAsync();
        }

        await using (var verificationContext = database.CreateContext())
        {
            var stored = await verificationContext.CashBankBalanceOperations.SingleAsync();
            Assert.Equal(1500.25m, stored.Amount);
            Assert.Equal(CashBankAccounts.Cash, stored.Account);
            Assert.Equal(CashBankBalanceOperationKinds.OpeningBalance, stored.OperationKind);
        }

        await AssertInvalidAsync(database, new CashBankBalanceOperation
        {
            Account = "unknown",
            OperationKind = CashBankBalanceOperationKinds.Adjustment,
            Direction = CashBankBalanceDirections.Increase,
            OperationDate = new DateOnly(2026, 7, 27),
            Amount = 1m,
            Reason = "Неверный счёт"
        });
        await AssertInvalidAsync(database, new CashBankBalanceOperation
        {
            Account = CashBankAccounts.Bank,
            OperationKind = CashBankBalanceOperationKinds.Adjustment,
            Direction = CashBankBalanceDirections.Decrease,
            OperationDate = new DateOnly(2026, 7, 27),
            Amount = 0m,
            Reason = "Нулевая сумма"
        });
    }

    private static async Task AssertInvalidAsync(
        PostgreSqlTestDatabase database,
        CashBankBalanceOperation operation)
    {
        await using var context = database.CreateContext();
        context.CashBankBalanceOperations.Add(operation);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
