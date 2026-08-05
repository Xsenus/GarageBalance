using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Funds;

public sealed class PostgreSqlUnifiedIncomePoolMigrationIntegrationTests
{
    private const string PreviousMigration = "20260723150126_SeparateCashBankTransfers";

    [PostgreSqlFact]
    public async Task Migration_MovesMembershipTargetAndOtherIncomeIntoOnePoolWithoutDoubleAllocation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var downgradeContext = database.CreateContext())
        {
            await downgradeContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await downgradeContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE funds ADD COLUMN "IsArchived" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE financial_operations ADD COLUMN "ExpenseFundId" uuid NULL;
                ALTER TABLE financial_operations ADD COLUMN "ExpensePaymentSource" character varying(20) NULL;
                """);
            await PostgreSqlLegacyModelCompatibility.AddCurrentVersionColumnsAsync(downgradeContext);
        }

        await using (var legacyContext = database.CreateContext())
        {
            var garage = new Garage
            {
                Number = $"UNIFIED-POOL-{Guid.NewGuid():N}",
                PeopleCount = 1,
                FloorCount = 1
            };
            var incomeTypes = await legacyContext.IncomeTypes
                .Where(item => item.Code == "membership" || item.Code == "target" || item.Code == "other_income")
                .ToDictionaryAsync(item => item.Code!);
            var otherFund = await legacyContext.Funds.SingleAsync(item => item.NormalizedName == "ПРОЧЕЕ");
            var manualFund = new Fund
            {
                Name = $"Рабочий фонд {Guid.NewGuid():N}",
                NormalizedName = $"РАБОЧИЙ ФОНД {Guid.NewGuid():N}",
                Balance = 50m,
                AllowOperations = true
            };
            var membershipIncome = CreateIncome(garage, incomeTypes["membership"], 100m, "POOL-MEMBERSHIP");
            var targetIncome = CreateIncome(garage, incomeTypes["target"], 200m, "POOL-TARGET");
            var otherIncome = CreateIncome(garage, incomeTypes["other_income"], 300m, "POOL-OTHER");
            otherFund.Balance = 300m;
            legacyContext.AddRange(garage, manualFund, membershipIncome, targetIncome, otherIncome);
            legacyContext.FundOperations.AddRange(
                new FundOperation
                {
                    Fund = otherFund,
                    FundId = otherFund.Id,
                    SourceFinancialOperation = otherIncome,
                    SourceFinancialOperationId = otherIncome.Id,
                    OperationKind = FundOperationKinds.Deposit,
                    Amount = 300m,
                    BalanceBefore = 0m,
                    BalanceAfter = 300m,
                    Reason = "Автоматическое назначение поступления «Прочие доходы»",
                    CreatedAtUtc = otherIncome.CreatedAtUtc
                },
                new FundOperation
                {
                    Fund = manualFund,
                    FundId = manualFund.Id,
                    OperationKind = FundOperationKinds.Deposit,
                    Amount = 50m,
                    BalanceBefore = 0m,
                    BalanceAfter = 50m,
                    Reason = "Историческое ручное распределение"
                });
            await legacyContext.SaveChangesAsync();
        }

        await using (var migrateContext = database.CreateContext())
        {
            await PostgreSqlLegacyModelCompatibility.RemoveCurrentVersionColumnsAsync(migrateContext);
            await migrateContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE funds DROP COLUMN "IsArchived";
                ALTER TABLE financial_operations DROP COLUMN "ExpenseFundId";
                ALTER TABLE financial_operations DROP COLUMN "ExpensePaymentSource";
                """);
            await migrateContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var pooledFunds = await verificationContext.Funds
            .Where(item =>
                item.NormalizedName == "ЧЛЕНСКИЕ ВЗНОСЫ" ||
                item.NormalizedName == "ЦЕЛЕВЫЕ ВЗНОСЫ" ||
                item.NormalizedName == "ПРОЧЕЕ")
            .OrderBy(item => item.SortOrder)
            .ToListAsync();
        Assert.Equal(3, pooledFunds.Count);
        Assert.All(pooledFunds, fund => Assert.True(fund.AllowOperations));
        Assert.Equal(
            new[] { 100m, 200m, 300m },
            pooledFunds.Select(fund => fund.Balance).Order().ToArray());

        var linkedIncomeTypes = await verificationContext.IncomeTypes
            .Where(item => item.Code == "membership" || item.Code == "target" || item.Code == "other_income")
            .ToListAsync();
        Assert.All(linkedIncomeTypes, item => Assert.NotNull(item.DestinationFundId));
        var assignments = await verificationContext.FundOperations
            .Where(item => item.SourceFinancialOperationId != null)
            .ToListAsync();
        Assert.Equal(3, assignments.Count);
        Assert.All(assignments, assignment => Assert.Equal(0m, assignment.BalanceBefore));
        Assert.Equal(
            new[] { 100m, 200m, 300m },
            assignments.Select(assignment => assignment.BalanceAfter).Order().ToArray());

        var service = new FundService(
            new EfFundRepository(verificationContext),
            new AuditEventWriter(verificationContext));
        var funds = await service.GetFundsAsync(CancellationToken.None);
        Assert.All(funds, fund => Assert.Equal(0m, fund.AvailableToDistribute));

        var targetFund = Assert.Single(funds, fund => fund.Name == "Целевые взносы");
        var excessive = await service.CreateOperationAsync(
            targetFund.Id,
            new CreateFundOperationRequest(FundOperationKinds.Deposit, 550.01m, "Проверка двойного распределения"),
            null,
            CancellationToken.None);
        var duplicateDistribution = await service.CreateOperationAsync(
            targetFund.Id,
            new CreateFundOperationRequest(FundOperationKinds.Deposit, 550m, "Распределение общего пула"),
            null,
            CancellationToken.None);

        Assert.False(excessive.Succeeded);
        Assert.Equal("fund_distribution_amount_exceeded", excessive.ErrorCode);
        Assert.False(duplicateDistribution.Succeeded);
        Assert.Equal("fund_distribution_amount_exceeded", duplicateDistribution.ErrorCode);
        Assert.All(
            await service.GetFundsAsync(CancellationToken.None),
            fund => Assert.Equal(0m, fund.AvailableToDistribute));
    }

    private static FinancialOperation CreateIncome(
        Garage garage,
        IncomeType incomeType,
        decimal amount,
        string documentNumber) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 7, 23),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = amount,
            DocumentNumber = documentNumber,
            Garage = garage,
            GarageId = garage.Id,
            IncomeType = incomeType,
            IncomeTypeId = incomeType.Id
        };
}
