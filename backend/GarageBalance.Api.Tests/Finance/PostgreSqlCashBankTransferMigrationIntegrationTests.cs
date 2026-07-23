using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlCashBankTransferMigrationIntegrationTests
{
    private const string PreviousMigration = "20260723140042_AddStaffSalaryAdjustments";

    [PostgreSqlFact]
    public async Task MigrationMovesLegacyBankDepositOutOfFundAndRecalculatesFundBalance()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fundId = Guid.NewGuid();
        var regularOperationId = Guid.NewGuid();
        var legacyTransferId = Guid.NewGuid();

        await using (var downgradeContext = database.CreateContext())
        {
            await downgradeContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            downgradeContext.Funds.Add(new Fund
            {
                Id = fundId,
                Name = "Резервный",
                NormalizedName = "РЕЗЕРВНЫЙ",
                Balance = 1200m,
                SortOrder = 1
            });
            downgradeContext.FundOperations.AddRange(
                new FundOperation
                {
                    Id = regularOperationId,
                    FundId = fundId,
                    OperationKind = FundOperationKinds.Deposit,
                    Amount = 1000m,
                    BalanceBefore = 0m,
                    BalanceAfter = 1000m,
                    Reason = "Обычное пополнение",
                    CreatedAtUtc = new DateTimeOffset(2026, 6, 1, 3, 0, 0, TimeSpan.Zero)
                },
                new FundOperation
                {
                    Id = legacyTransferId,
                    FundId = fundId,
                    OperationKind = FundOperationKinds.Deposit,
                    Amount = 200m,
                    BalanceBefore = 1000m,
                    BalanceAfter = 1200m,
                    Reason = "Сдача кассы в банк 2026-06-15: Инкассация",
                    CreatedAtUtc = new DateTimeOffset(2026, 6, 15, 3, 0, 0, TimeSpan.Zero)
                });
            await downgradeContext.SaveChangesAsync();
            await downgradeContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE fund_operations SET \"IsCashToBankTransfer\" = TRUE WHERE \"Id\" = {legacyTransferId}");

            await downgradeContext.Database.MigrateAsync();
        }

        await using (var migratedContext = database.CreateContext())
        {
            var transfer = await migratedContext.CashBankTransfers.SingleAsync();
            Assert.Equal(legacyTransferId, transfer.Id);
            Assert.Equal(new DateOnly(2026, 6, 15), transfer.TransferDate);
            Assert.Equal(200m, transfer.Amount);
            Assert.Equal("Инкассация", transfer.Comment);

            var operation = await migratedContext.FundOperations.SingleAsync();
            Assert.Equal(regularOperationId, operation.Id);
            Assert.Equal(0m, operation.BalanceBefore);
            Assert.Equal(1000m, operation.BalanceAfter);
            Assert.Equal(1000m, (await migratedContext.Funds.SingleAsync(fund => fund.Id == fundId)).Balance);

            var legacyColumn = await migratedContext.Database
                .SqlQueryRaw<string?>(
                    """
                    SELECT column_name AS "Value"
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'fund_operations'
                      AND column_name = 'IsCashToBankTransfer'
                    """)
                .SingleOrDefaultAsync();
            Assert.Null(legacyColumn);
        }

        await using var invalidContext = database.CreateContext();
        invalidContext.CashBankTransfers.Add(new CashBankTransfer
        {
            TransferDate = new DateOnly(2026, 6, 16),
            Amount = 0m
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => invalidContext.SaveChangesAsync());
    }
}
