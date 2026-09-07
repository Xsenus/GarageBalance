using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Funds;

public sealed class PostgreSqlDefaultFeeFundMigrationTests
{
    private const string PreviousMigration = "20260905122505_AddGaragePeopleCountHistory";
    private static readonly Guid MembershipFundId = Guid.Parse("718b671d-9133-4ffd-a130-3f2f03a8d150");
    private static readonly Guid TargetFundId = Guid.Parse("edc66ae5-29a3-4dbd-921e-4a1958612760");
    private static readonly Guid OtherFundId = Guid.Parse("58bc1538-5f77-46ce-9edf-8ed6aa73a701");

    [PostgreSqlFact]
    public async Task FreshInstallation_RoutesBothFeesToOtherWithoutSeparateFunds()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync("0");
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        var fees = await context.IncomeTypes.Where(item => item.Code == "membership" || item.Code == "target").ToListAsync();
        Assert.Equal(2, fees.Count);
        Assert.All(fees, item => Assert.Equal(OtherFundId, item.DestinationFundId));
        Assert.False(await context.Funds.AnyAsync(item => item.Id == MembershipFundId || item.Id == TargetFundId));
        Assert.Equal(2, await context.AuditEvents.CountAsync(item => item.Action == "dictionary.default_fee_fund_changed"));
        Assert.Equal(2, await context.ChargeServiceSettings.CountAsync(item => item.IncomeType!.Code == "membership" || item.IncomeType!.Code == "target"));
    }

    [PostgreSqlFact]
    public async Task UpgradeAndRollback_PreserveTariffsAndDoNotRepeatOrUndoDestinations()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(PreviousMigration);
        await using var context = database.CreateContext();
        var expectedTariffs = System.Text.Json.JsonSerializer.Serialize(await context.Tariffs.AsNoTracking().OrderBy(item => item.Id).ToListAsync());
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync(PreviousMigration);
        await context.Database.MigrateAsync();

        Assert.Equal(expectedTariffs, System.Text.Json.JsonSerializer.Serialize(await context.Tariffs.AsNoTracking().OrderBy(item => item.Id).ToListAsync()));
        Assert.All(await context.IncomeTypes.Where(item => item.Code == "membership" || item.Code == "target").ToListAsync(),
            item => Assert.Equal(OtherFundId, item.DestinationFundId));
        Assert.False(await context.Funds.AnyAsync(item => item.Id == MembershipFundId || item.Id == TargetFundId));
        Assert.Equal(2, await context.AuditEvents.CountAsync(item => item.Action == "dictionary.default_fee_fund_changed"));
    }

    [PostgreSqlFact]
    public async Task Upgrade_PreservesEveryKindOfHistoricalFundReference()
    {
        foreach (var scenario in new[] { "operation", "canceled-operation", "supplier", "accrual", "payment", "other-income" })
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync(PreviousMigration);
            await using var context = database.CreateContext();
            var fund = await context.Funds.SingleAsync(item => item.Id == MembershipFundId);
            var supplier = new Supplier { Name = "Контрольный поставщик", Group = new SupplierGroup { Name = "Контрольная группа" } };
            var expense = new ExpenseType { Name = "Контрольный расход" };
            switch (scenario)
            {
                case "operation":
                case "canceled-operation":
                    context.FundOperations.Add(new FundOperation
                    {
                        Fund = fund,
                        OperationKind = "deposit",
                        Amount = 10m,
                        BalanceBefore = 0,
                        BalanceAfter = 10m,
                        IsCanceled = scenario == "canceled-operation",
                        Reason = "История проверки"
                    });
                    break;
                case "supplier":
                    supplier.ExpenseFund = fund;
                    supplier.IsArchived = true;
                    context.Add(supplier);
                    break;
                case "accrual":
                    context.SupplierAccruals.Add(new SupplierAccrual
                    {
                        Supplier = supplier,
                        ExpenseType = expense,
                        ExpenseFund = fund,
                        AccountingMonth = new DateOnly(2026, 8, 1),
                        Amount = 10m,
                        Source = "manual"
                    });
                    break;
                case "payment":
                    context.FinancialOperations.Add(new FinancialOperation
                    {
                        OperationKind = "expense",
                        Supplier = supplier,
                        ExpenseType = expense,
                        ExpenseFund = fund,
                        OperationDate = new DateOnly(2026, 8, 1),
                        AccountingMonth = new DateOnly(2026, 8, 1),
                        Amount = 10m
                    });
                    break;
                case "other-income":
                    context.IncomeTypes.Add(new IncomeType { Name = "Другое назначение", DestinationFund = fund, IsArchived = true });
                    break;
            }
            await context.SaveChangesAsync();
            var oldVersion = fund.Version;
            var counts = new[] { await context.FundOperations.CountAsync(), await context.SupplierAccruals.CountAsync(), await context.FinancialOperations.CountAsync() };

            await context.Database.MigrateAsync();
            context.ChangeTracker.Clear();

            var saved = await context.Funds.SingleAsync(item => item.Id == MembershipFundId);
            Assert.Equal(oldVersion, saved.Version);
            Assert.Equal(0m, saved.Balance);
            Assert.Equal(MembershipFundId, (await context.IncomeTypes.SingleAsync(item => item.Code == "membership")).DestinationFundId);
            Assert.False(await context.Funds.AnyAsync(item => item.Id == TargetFundId), scenario);
            Assert.Equal(OtherFundId, (await context.IncomeTypes.SingleAsync(item => item.Code == "target")).DestinationFundId);
            Assert.Equal(counts, new[] { await context.FundOperations.CountAsync(), await context.SupplierAccruals.CountAsync(), await context.FinancialOperations.CountAsync() });
        }
    }

    [PostgreSqlFact]
    public async Task Upgrade_PreservesUserSettingsEvenWhenFundIsEmpty()
    {
        foreach (var scenario in new[] { "name", "sort", "balance", "archived", "disabled", "audit", "income-name", "destination" })
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync(PreviousMigration);
            await using var context = database.CreateContext();
            var fund = await context.Funds.SingleAsync(item => item.Id == MembershipFundId);
            var income = await context.IncomeTypes.SingleAsync(item => item.Code == "membership");
            switch (scenario)
            {
                case "name": fund.Name = "Фонд правления"; fund.NormalizedName = "ФОНД ПРАВЛЕНИЯ"; break;
                case "sort": fund.SortOrder = 80; break;
                case "balance": fund.Balance = 12m; break;
                case "archived": fund.IsArchived = true; break;
                case "disabled": fund.AllowOperations = false; break;
                case "audit": context.AuditEvents.Add(new AuditEvent { Action = "fund.updated", EntityType = "fund", EntityId = fund.Id.ToString(), Summary = "Настройка администратора" }); break;
                case "income-name": income.Name = "Взнос правления"; income.UpdatedAtUtc = DateTimeOffset.UtcNow; break;
                case "destination": income.DestinationFundId = OtherFundId; break;
            }
            await context.SaveChangesAsync();
            var expectedDestination = income.DestinationFundId;
            var expectedVersion = fund.Version;

            await context.Database.MigrateAsync();
            context.ChangeTracker.Clear();

            Assert.Equal(expectedVersion, (await context.Funds.SingleAsync(item => item.Id == MembershipFundId)).Version);
            Assert.Equal(expectedDestination, (await context.IncomeTypes.SingleAsync(item => item.Id == income.Id)).DestinationFundId);
            Assert.False(await context.Funds.AnyAsync(item => item.Id == TargetFundId), scenario);
        }
    }

    [PostgreSqlFact]
    public async Task Upgrade_DoesNotRestoreOrReplaceCustomizedOtherFund()
    {
        foreach (var scenario in new[] { "renamed", "archived", "disabled" })
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync(PreviousMigration);
            await using var context = database.CreateContext();
            var other = await context.Funds.SingleAsync(item => item.Id == OtherFundId);
            if (scenario == "renamed") { other.Name = "Резерв"; other.NormalizedName = "РЕЗЕРВ"; }
            if (scenario == "archived") other.IsArchived = true;
            if (scenario == "disabled") other.AllowOperations = false;
            await context.SaveChangesAsync();
            var expectedVersion = other.Version;

            await context.Database.MigrateAsync();
            context.ChangeTracker.Clear();

            Assert.Equal(expectedVersion, (await context.Funds.SingleAsync(item => item.Id == OtherFundId)).Version);
            Assert.Equal(MembershipFundId, (await context.IncomeTypes.SingleAsync(item => item.Code == "membership")).DestinationFundId);
            Assert.Equal(TargetFundId, (await context.IncomeTypes.SingleAsync(item => item.Code == "target")).DestinationFundId);
            Assert.Empty(await context.AuditEvents.Where(item => item.Action == "dictionary.default_fee_fund_changed").ToListAsync());
        }
    }
}
