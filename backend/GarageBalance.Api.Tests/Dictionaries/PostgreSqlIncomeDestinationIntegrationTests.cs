using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlIncomeDestinationIntegrationTests
{
    private const string PreviousMigration = "20260718135608_AnnualAccrualAccountingYear";

    [PostgreSqlFact]
    public async Task MigratedSystemDestinations_HaveStableCodesAndExplicitFundLinks()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid destinationFundId;

        await using (var context = database.CreateContext())
        {
            var destinations = await context.IncomeTypes
                .AsNoTracking()
                .Include(item => item.DestinationFund)
                .Where(item => item.Code == "other_payments" || item.Code == "other_income")
                .OrderBy(item => item.Code)
                .ToListAsync();

            Assert.Equal(2, destinations.Count);
            Assert.All(destinations, item =>
            {
                Assert.True(item.IsSystem);
                Assert.False(item.IsArchived);
                Assert.NotNull(item.DestinationFund);
                Assert.Equal("Прочее", item.DestinationFund.Name);
                Assert.True(item.DestinationFund.IsSystem);
            });
            Assert.Contains(destinations, item => item.Code == "other_payments" && item.Name == "Прочие оплаты");
            Assert.Contains(destinations, item => item.Code == "other_income" && item.Name == "Прочие доходы");
            destinationFundId = Assert.Single(destinations.Select(item => item.DestinationFundId!.Value).Distinct());
        }

        await using (var renameContext = database.CreateContext())
        {
            var otherPayments = await renameContext.IncomeTypes.SingleAsync(item => item.Code == "other_payments");
            var fund = await renameContext.Funds.SingleAsync(item => item.Id == destinationFundId);
            otherPayments.Name = "Отображаемое название изменено";
            fund.Name = "Отображаемое название фонда изменено";
            await renameContext.SaveChangesAsync();
        }

        await using (var verificationContext = database.CreateContext())
        {
            var repository = new EfIncomeTypeRepository(verificationContext);
            var destination = await repository.FindFirstActiveByCodeAsync("other_payments", CancellationToken.None);

            Assert.NotNull(destination);
            Assert.Equal(destinationFundId, destination.DestinationFundId);
            Assert.Equal("Отображаемое название изменено", destination.Name);

            var fund = await verificationContext.Funds.SingleAsync(item => item.Id == destinationFundId);
            verificationContext.Funds.Remove(fund);
            await Assert.ThrowsAsync<DbUpdateException>(() => verificationContext.SaveChangesAsync());
        }
    }

    [PostgreSqlFact]
    public async Task Migration_AdoptsExistingRussianCatalogRowsWithoutDependingOnTheirIdentifiers()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid existingFundId;
        Guid existingIncomeTypeId;

        await using (var setupContext = database.CreateContext())
        {
            await setupContext.Database.MigrateAsync(PreviousMigration);
            existingFundId = Guid.NewGuid();
            existingIncomeTypeId = Guid.NewGuid();
            await setupContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO funds (
                    "Id", "Name", "NormalizedName", "Balance", "SortOrder", "AllowOperations",
                    "IsSystem", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES (
                    {existingFundId}, 'Прочее', 'ПРОЧЕЕ', 0, 70, FALSE,
                    TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

                INSERT INTO income_types (
                    "Id", "Name", "Code", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES (
                    {existingIncomeTypeId}, 'Прочие оплаты', NULL, FALSE, FALSE,
                    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                """);

            await setupContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var adopted = await verificationContext.IncomeTypes
            .AsNoTracking()
            .SingleAsync(item => item.Code == "other_payments");
        var otherIncome = await verificationContext.IncomeTypes
            .AsNoTracking()
            .SingleAsync(item => item.Code == "other_income");

        Assert.Equal(existingIncomeTypeId, adopted.Id);
        Assert.Equal(existingFundId, adopted.DestinationFundId);
        Assert.Equal(existingFundId, otherIncome.DestinationFundId);
        Assert.True(adopted.IsSystem);
        Assert.Equal(2, await verificationContext.IncomeTypes.CountAsync(item =>
            item.Code == "other_payments" || item.Code == "other_income"));
    }
}
