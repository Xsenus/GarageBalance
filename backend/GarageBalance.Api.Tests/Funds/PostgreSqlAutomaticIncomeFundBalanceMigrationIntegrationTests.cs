using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Funds;

public sealed class PostgreSqlAutomaticIncomeFundBalanceMigrationIntegrationTests
{
    private const string PreviousMigration = "20260805051709_ChargeServiceTariffHistory";

    [PostgreSqlFact]
    public async Task Migration_IncludesActiveAutomaticIncomeDepositsAndKeepsCanceledDepositsNeutral()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var downgradeContext = database.CreateContext())
        {
            await downgradeContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await PostgreSqlLegacyModelCompatibility.AddCurrentVersionColumnsAsync(downgradeContext);
        }

        Guid fundId;
        Guid manualDepositId;
        Guid automaticDepositId;
        Guid withdrawalId;
        Guid canceledAutomaticDepositId;
        await using (var setupContext = database.CreateContext())
        {
            var fund = new Fund
            {
                Name = $"Автоматический фонд {Guid.NewGuid():N}",
                NormalizedName = $"AUTOMATIC-{Guid.NewGuid():N}",
                Balance = 70m
            };
            var incomeType = new IncomeType
            {
                Name = $"Поступление {Guid.NewGuid():N}",
                Code = $"automatic_{Guid.NewGuid():N}",
                DestinationFund = fund
            };
            var garage = new Garage
            {
                Number = $"AUTO-FUND-{Guid.NewGuid():N}",
                PeopleCount = 1,
                FloorCount = 1
            };
            var activeIncome = Income(garage, incomeType, 50m, "AUTO-ACTIVE");
            var canceledIncome = Income(garage, incomeType, 25m, "AUTO-CANCELED");
            var manualDeposit = FundOperation(fund, FundOperationKinds.Deposit, 100m, 0m, 100m, 1);
            var automaticDeposit = FundOperation(fund, FundOperationKinds.Deposit, 50m, 100m, 100m, 2);
            automaticDeposit.SourceFinancialOperation = activeIncome;
            automaticDeposit.SourceFinancialOperationId = activeIncome.Id;
            var withdrawal = FundOperation(fund, FundOperationKinds.Withdraw, 30m, 100m, 70m, 3);
            var canceledAutomaticDeposit = FundOperation(fund, FundOperationKinds.Deposit, 25m, 70m, 70m, 4);
            canceledAutomaticDeposit.SourceFinancialOperation = canceledIncome;
            canceledAutomaticDeposit.SourceFinancialOperationId = canceledIncome.Id;
            canceledAutomaticDeposit.IsCanceled = true;

            setupContext.AddRange(fund, incomeType, garage, activeIncome, canceledIncome, manualDeposit, automaticDeposit, withdrawal, canceledAutomaticDeposit);
            await setupContext.SaveChangesAsync();

            fundId = fund.Id;
            manualDepositId = manualDeposit.Id;
            automaticDepositId = automaticDeposit.Id;
            withdrawalId = withdrawal.Id;
            canceledAutomaticDepositId = canceledAutomaticDeposit.Id;
        }

        await using (var migrateContext = database.CreateContext())
        {
            await PostgreSqlLegacyModelCompatibility.RemoveCurrentVersionColumnsAsync(migrateContext);
            await migrateContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var operations = await verificationContext.FundOperations
            .Where(operation => operation.FundId == fundId)
            .ToDictionaryAsync(operation => operation.Id);
        AssertBalances(operations[manualDepositId], 0m, 100m);
        AssertBalances(operations[automaticDepositId], 100m, 150m);
        AssertBalances(operations[withdrawalId], 150m, 120m);
        AssertBalances(operations[canceledAutomaticDepositId], 120m, 120m);
        Assert.Equal(120m, await verificationContext.Funds
            .Where(fund => fund.Id == fundId)
            .Select(fund => fund.Balance)
            .SingleAsync());
    }

    private static FinancialOperation Income(Garage garage, IncomeType incomeType, decimal amount, string documentNumber) => new()
    {
        OperationKind = FinancialOperationKinds.Income,
        OperationDate = new DateOnly(2026, 8, 5),
        AccountingMonth = new DateOnly(2026, 8, 1),
        Amount = amount,
        DocumentNumber = documentNumber,
        Garage = garage,
        IncomeType = incomeType
    };

    private static FundOperation FundOperation(
        Fund fund,
        string operationKind,
        decimal amount,
        decimal balanceBefore,
        decimal balanceAfter,
        int order) => new()
        {
            Fund = fund,
            OperationKind = operationKind,
            Amount = amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            Reason = "Проверка миграции автоматического распределения",
            CreatedAtUtc = new DateTimeOffset(2026, 8, 5, 0, order, 0, TimeSpan.Zero)
        };

    private static void AssertBalances(FundOperation operation, decimal before, decimal after)
    {
        Assert.Equal(before, operation.BalanceBefore);
        Assert.Equal(after, operation.BalanceAfter);
    }
}
