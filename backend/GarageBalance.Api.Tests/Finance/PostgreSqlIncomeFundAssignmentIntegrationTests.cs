using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlIncomeFundAssignmentIntegrationTests
{
    private const string PreviousMigration = "20260718172521_RouteFeeCampaignAccrualsToOtherIncome";

    [PostgreSqlFact]
    public async Task RoutedIncomeCancellationAndRestorationStayAtomicOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        Guid incomeTypeId;
        Guid destinationFundId;
        await using (var setupContext = database.CreateContext())
        {
            var garage = new Garage
            {
                Number = $"ROUTED-PG-{Guid.NewGuid():N}",
                PeopleCount = 1,
                FloorCount = 1
            };
            var incomeType = await setupContext.IncomeTypes
                .Include(item => item.DestinationFund)
                .SingleAsync(item => item.Code == "other_income");
            setupContext.Garages.Add(garage);
            await setupContext.SaveChangesAsync();
            garageId = garage.Id;
            incomeTypeId = incomeType.Id;
            destinationFundId = incomeType.DestinationFundId!.Value;
        }

        Guid operationId;
        await using (var createContext = database.CreateContext())
        {
            var created = await FinanceServiceTestFactory.Create(createContext).CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    garageId,
                    incomeTypeId,
                    new DateOnly(2026, 7, 19),
                    new DateOnly(2026, 7, 1),
                    325m,
                    "PG-ROUTED-INCOME",
                    null),
                null,
                CancellationToken.None);
            Assert.True(created.Succeeded, created.ErrorMessage);
            operationId = created.Value!.Id;
        }

        await using (var cancelContext = database.CreateContext())
        {
            var canceled = await FinanceServiceTestFactory.Create(cancelContext).CancelOperationAsync(
                operationId,
                new CancelFinanceEntryRequest("Интеграционная проверка"),
                null,
                CancellationToken.None);
            Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        }

        await using (var canceledStateContext = database.CreateContext())
        {
            var source = await canceledStateContext.FinancialOperations.SingleAsync(item => item.Id == operationId);
            var assignment = await canceledStateContext.FundOperations.SingleAsync(item => item.SourceFinancialOperationId == operationId);
            Assert.True(source.IsCanceled);
            Assert.True(assignment.IsCanceled);
            Assert.Equal(0m, await canceledStateContext.Funds
                .Where(item => item.Id == destinationFundId)
                .Select(item => item.Balance)
                .SingleAsync());
            Assert.Contains(canceledStateContext.AuditEvents, item => item.Action == "fund.income_assignment_canceled");
        }

        await using (var restoreContext = database.CreateContext())
        {
            var restored = await FinanceServiceTestFactory.Create(restoreContext)
                .RestoreOperationAsync(operationId, null, CancellationToken.None);
            Assert.True(restored.Succeeded, restored.ErrorMessage);
        }

        await using var verificationContext = database.CreateContext();
        var restoredSource = await verificationContext.FinancialOperations.SingleAsync(item => item.Id == operationId);
        var restoredAssignment = await verificationContext.FundOperations.SingleAsync(item => item.SourceFinancialOperationId == operationId);
        Assert.False(restoredSource.IsCanceled);
        Assert.False(restoredAssignment.IsCanceled);
        Assert.Equal(325m, restoredAssignment.Amount);
        Assert.Equal(325m, await verificationContext.Funds
            .Where(item => item.Id == destinationFundId)
            .Select(item => item.Balance)
            .SingleAsync());
        Assert.Contains(verificationContext.AuditEvents, item => item.Action == "fund.income_assignment_restored");

        verificationContext.ChangeTracker.Clear();
        verificationContext.FundOperations.Add(new FundOperation
        {
            FundId = destinationFundId,
            SourceFinancialOperationId = operationId,
            OperationKind = FundOperationKinds.Deposit,
            Amount = 1m,
            BalanceBefore = 325m,
            BalanceAfter = 326m,
            Reason = "Проверка уникальности связи"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => verificationContext.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task MigrationBackfillsHistoricalRoutedIncomeAndPreservesCanceledState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var downgradeContext = database.CreateContext())
        {
            await downgradeContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        Guid activeOperationId;
        Guid canceledOperationId;
        Guid destinationFundId;
        decimal balanceBeforeMigration;
        await using (var legacyContext = database.CreateContext())
        {
            var incomeType = await legacyContext.IncomeTypes
                .Include(item => item.DestinationFund)
                .SingleAsync(item => item.Code == "other_income");
            var garage = new Garage
            {
                Number = $"ROUTED-MIGRATION-{Guid.NewGuid():N}",
                PeopleCount = 1,
                FloorCount = 1
            };
            legacyContext.Garages.Add(garage);
            await legacyContext.SaveChangesAsync();
            activeOperationId = Guid.NewGuid();
            canceledOperationId = Guid.NewGuid();
            await InsertLegacyIncomeAsync(
                legacyContext,
                activeOperationId,
                garage.Id,
                incomeType.Id,
                125m,
                "MIGRATION-ACTIVE",
                isCanceled: false);
            await InsertLegacyIncomeAsync(
                legacyContext,
                canceledOperationId,
                garage.Id,
                incomeType.Id,
                75m,
                "MIGRATION-CANCELED",
                isCanceled: true);
            destinationFundId = incomeType.DestinationFundId!.Value;
            balanceBeforeMigration = incomeType.DestinationFund!.Balance;
        }

        await using (var migrateContext = database.CreateContext())
        {
            await migrateContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var assignments = await verificationContext.FundOperations
            .Where(item => item.SourceFinancialOperationId == activeOperationId || item.SourceFinancialOperationId == canceledOperationId)
            .OrderBy(item => item.Amount)
            .ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.True(assignments[0].IsCanceled);
        Assert.Equal(75m, assignments[0].Amount);
        Assert.Contains("Автоматическое назначение поступления", assignments[0].Reason, StringComparison.Ordinal);
        Assert.False(assignments[1].IsCanceled);
        Assert.Equal(125m, assignments[1].Amount);
        Assert.Equal(balanceBeforeMigration + 125m, await verificationContext.Funds
            .Where(item => item.Id == destinationFundId)
            .Select(item => item.Balance)
            .SingleAsync());
        Assert.Equal(2, await verificationContext.AuditEvents.CountAsync(item =>
            item.Action == "fund.income_assignment_created" &&
            (item.RelatedDocumentId == activeOperationId.ToString() || item.RelatedDocumentId == canceledOperationId.ToString())));
    }

    private static Task InsertLegacyIncomeAsync(
        GarageBalanceDbContext context,
        Guid operationId,
        Guid garageId,
        Guid incomeTypeId,
        decimal amount,
        string documentNumber,
        bool isCanceled)
    {
        var now = DateTimeOffset.UtcNow;
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_operations
                ("Id", "OperationKind", "OperationDate", "AccountingMonth", "Amount",
                 "DocumentNumber", "GarageId", "IncomeTypeId", "IsCanceled", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES
                ({operationId}, {FinancialOperationKinds.Income}, {new DateOnly(2026, 7, 18)},
                 {new DateOnly(2026, 7, 1)}, {amount}, {documentNumber}, {garageId}, {incomeTypeId},
                 {isCanceled}, {now}, {now});
            """);
    }
}
