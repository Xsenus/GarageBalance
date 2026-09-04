using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.ShowcaseSeed;
using Microsoft.EntityFrameworkCore;

try
{
    var mode = args.Length == 0 ? "audit" : args[0].Trim().ToLowerInvariant();
    if (mode is not "audit" and not "prepare" and not "reset")
    {
        throw new ArgumentException("Mode must be 'audit', 'prepare' or 'reset'.");
    }

    var allowIntegrationDatabase = string.Equals(
        Environment.GetEnvironmentVariable("GARAGEBALANCE_ALLOW_SHOWCASE_TEST_DATABASE"),
        "true",
        StringComparison.OrdinalIgnoreCase);
    var connectionString = Environment.GetEnvironmentVariable("GARAGEBALANCE_SHOWCASE_CONNECTION") ?? string.Empty;
    var confirmation = Environment.GetEnvironmentVariable("GARAGEBALANCE_SHOWCASE_CONFIRMATION") ?? string.Empty;
    var connection = mode == "reset"
        ? ShowcaseDatabaseGuard.ValidateReset(connectionString, confirmation, allowIntegrationDatabase)
        : ShowcaseDatabaseGuard.Validate(connectionString, confirmation, allowIntegrationDatabase);
    var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
        .UseNpgsql(connection)
        .Options;
    await using var context = new GarageBalanceDbContext(options);
    await context.Database.MigrateAsync();
    var seeder = new ShowcaseDataSeeder(context);
    if (mode == "reset")
    {
        var reset = await seeder.ResetWorkingDataAsync(CancellationToken.None);
        Console.WriteLine(
            $"stagingResetStatus={(reset.IsClean ? "clean" : "incomplete")}; " +
            $"preservedUsers={reset.PreservedAfter.Users}; preservedTariffs={reset.PreservedAfter.Tariffs}; " +
            $"preservedServices={reset.PreservedAfter.ChargeServiceSettings}; " +
            $"preservedTariffVersions={reset.PreservedAfter.ChargeServiceTariffVersions}; " +
            $"preservedIrregularPayments={reset.PreservedAfter.IrregularPayments}; " +
            $"preservedFunds={reset.PreservedAfter.Funds}; clearedRows={reset.ClearedRowCount}; " +
            $"fundBalance={reset.FundBalance}; generalPool={reset.GeneralPoolBalance}; auditEvents={reset.AuditEventCount}");
        return reset.IsClean ? 0 : 3;
    }

    var result = mode == "prepare"
        ? await seeder.PrepareAsync(CancellationToken.None)
        : await seeder.AuditAsync(CancellationToken.None);

    Console.WriteLine(
        $"showcaseStatus={(result.IsReady ? "ready" : "incomplete")}; mode={mode}; " +
        $"garages={result.GarageCount}; accruals={result.AccrualCount}; operations={result.FinancialOperationCount}; " +
        $"readings={result.MeterReadingCount}; campaigns={result.FeeCampaignCount}; suppliers={result.SupplierCount}; " +
        $"staff={result.StaffMemberCount}; " +
        $"preservedUsers={result.PreservedUserCount}; noDebt={result.HasNoDebt}; debt={result.HasDebt}; advance={result.HasAdvance}; " +
        $"newGarageClean={result.NewGarageHasNoCalculatedHistory}; campaignParticipantsLocked={result.CampaignsHaveLockedParticipants}; " +
        $"annualAccrualsUnique={result.AnnualAccrualsAreUnique}; overdueScenarioCorrect={result.OverdueScenarioIsCorrect}; " +
        $"staffScenariosComplete={result.StaffScenariosAreComplete}; supplierScenariosComplete={result.SupplierScenariosAreComplete}; " +
        $"fundBalancesReconcile={result.FundBalancesReconcile}");
    return result.IsReady ? 0 : 3;
}
catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"showcaseStatus=refused; reason={exception.Message}");
    return 2;
}
