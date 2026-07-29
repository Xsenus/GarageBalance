using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Funds;

public sealed class PostgreSqlAllIncomeFundsMigrationIntegrationTests
{
    private const string PreviousMigration = "20260728054806_NormalizeElectricityEnergyUnits";

    [PostgreSqlFact]
    public async Task Migration_LinksEveryActiveIncomeTypeAndBackfillsHistoricalAssignments()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var downgradeContext = database.CreateContext())
        {
            await downgradeContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        Guid waterOperationId;
        await using (var setupContext = database.CreateContext())
        {
            var water = await setupContext.IncomeTypes.SingleAsync(item => item.Code == "water");
            var membership = await setupContext.IncomeTypes.SingleAsync(item => item.Code == "membership");
            var garage = new Garage
            {
                Number = $"ALL-FUNDS-{Guid.NewGuid():N}",
                PeopleCount = 1,
                FloorCount = 1
            };
            var operation = new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 7, 29),
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 125m,
                DocumentNumber = $"ALL-FUNDS-{Guid.NewGuid():N}",
                Garage = garage,
                GarageId = garage.Id,
                IncomeType = water,
                IncomeTypeId = water.Id
            };

            water.DestinationFundId = null;
            setupContext.IncomeTypes
                .Where(item => item.Id != membership.Id && item.Code != "target" && item.Code != "other_income" && item.Code != "other_payments")
                .ToList()
                .ForEach(item => item.DestinationFundId = null);
            setupContext.AddRange(garage, operation);
            await setupContext.SaveChangesAsync();
            waterOperationId = operation.Id;
        }

        await using (var migrateContext = database.CreateContext())
        {
            await migrateContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var activeIncomeTypes = await verificationContext.IncomeTypes
            .Include(item => item.DestinationFund)
            .Where(item => !item.IsArchived)
            .ToListAsync();
        Assert.All(activeIncomeTypes, item =>
        {
            Assert.NotNull(item.DestinationFundId);
            Assert.NotNull(item.DestinationFund);
            Assert.True(item.DestinationFund!.AllowOperations);
            Assert.False(item.DestinationFund.IsArchived);
        });

        Assert.Equal(
            "Водоснабжение",
            Assert.Single(activeIncomeTypes, item => item.Code == "water").DestinationFund!.Name);
        Assert.Equal(
            "Вывоз мусора",
            Assert.Single(activeIncomeTypes, item => item.Code == "trash").DestinationFund!.Name);
        Assert.Equal(
            "Электроэнергия",
            Assert.Single(activeIncomeTypes, item => item.Code == "electricity").DestinationFund!.Name);
        Assert.Equal(
            "Наружное освещение",
            Assert.Single(activeIncomeTypes, item => item.Code == "outdoor_lighting").DestinationFund!.Name);
        Assert.Equal(
            "Прочее",
            Assert.Single(activeIncomeTypes, item => item.Code == "penalty").DestinationFund!.Name);

        var assignment = await verificationContext.FundOperations
            .SingleAsync(item => item.SourceFinancialOperationId == waterOperationId);
        Assert.Equal(assignment.BalanceBefore, assignment.BalanceAfter);
        Assert.Equal(
            Assert.Single(activeIncomeTypes, item => item.Code == "water").DestinationFundId,
            assignment.FundId);
    }
}
