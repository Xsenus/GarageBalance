using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Tests.Common;
using GarageBalance.ShowcaseSeed;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class PostgreSqlStagingWorkingDataResetIntegrationTests
{
    [PostgreSqlFact]
    public async Task Reset_ClearsAllWorkingDataAndPreservesCatalogsAndUsers()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var user = new AppUser
        {
            Email = "reset-admin@example.test",
            NormalizedEmail = "RESET-ADMIN@EXAMPLE.TEST",
            DisplayName = "Reset administrator",
            PasswordHash = "not-a-real-password-hash"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var seeder = new ShowcaseDataSeeder(context);
        Assert.True((await seeder.PrepareAsync(CancellationToken.None)).IsReady);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO form_states ("Id", "CreatedAtUtc", "PayloadJson", "Scope", "UpdatedAtUtc")
            VALUES (gen_random_uuid(), now(), '{{}}', 'reset-test', now());
            """);

        var tariffState = await context.Tariffs
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.Name, item.Rate, item.EffectiveFrom, item.ElectricityTiersJson })
            .ToArrayAsync();
        var serviceState = await context.ChargeServiceSettings
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.IncomeTypeId, item.TariffId, item.IsArchived })
            .ToArrayAsync();
        var irregularPaymentIds = await context.IrregularPayments
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync();
        var fundIds = await context.Funds
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync();

        var result = await seeder.ResetWorkingDataAsync(CancellationToken.None);

        Assert.True(result.IsClean);
        Assert.True(result.ClearedRowCount > 0);
        Assert.Equal(result.PreservedBefore, result.PreservedAfter);
        Assert.Equal(0m, result.FundBalance);
        Assert.Equal(0m, result.GeneralPoolBalance);
        Assert.Equal(0, result.AuditEventCount);
        Assert.Equal([user.Id], await context.Users.AsNoTracking().Select(item => item.Id).ToArrayAsync());
        Assert.Equal(tariffState, await context.Tariffs
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.Name, item.Rate, item.EffectiveFrom, item.ElectricityTiersJson })
            .ToArrayAsync());
        Assert.Equal(serviceState, await context.ChargeServiceSettings
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.IncomeTypeId, item.TariffId, item.IsArchived })
            .ToArrayAsync());
        Assert.Equal(irregularPaymentIds, await context.IrregularPayments
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync());
        Assert.Equal(fundIds, await context.Funds
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync());
        Assert.All(await context.Funds.AsNoTracking().ToArrayAsync(), item => Assert.Equal(0m, item.Balance));

        Assert.Empty(await context.Owners.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.Garages.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.Suppliers.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.SupplierGroups.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.StaffMembers.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.StaffDepartments.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.StaffEmploymentPeriods.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.StaffSalaryRatePeriods.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.MeterDevices.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.MeterReadings.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.Accruals.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.AccrualPaymentAllocations.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.FinancialOperations.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.SupplierAccruals.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.StaffSalaryAdjustments.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.FundOperations.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.CashBankTransfers.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.CashBankBalanceOperations.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.OpeningBalanceAdjustments.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.FeeCampaigns.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.AuditEvents.AsNoTracking().ToArrayAsync());

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM form_states;";
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }
}
