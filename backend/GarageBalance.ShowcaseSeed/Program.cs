using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.ShowcaseSeed;
using Microsoft.EntityFrameworkCore;

try
{
    var mode = args.Length == 0 ? "audit" : args[0].Trim().ToLowerInvariant();
    if (mode is not "audit" and not "prepare")
    {
        throw new ArgumentException("Mode must be 'audit' or 'prepare'.");
    }

    var allowIntegrationDatabase = string.Equals(
        Environment.GetEnvironmentVariable("GARAGEBALANCE_ALLOW_SHOWCASE_TEST_DATABASE"),
        "true",
        StringComparison.OrdinalIgnoreCase);
    var connection = ShowcaseDatabaseGuard.Validate(
        Environment.GetEnvironmentVariable("GARAGEBALANCE_SHOWCASE_CONNECTION") ?? string.Empty,
        Environment.GetEnvironmentVariable("GARAGEBALANCE_SHOWCASE_CONFIRMATION") ?? string.Empty,
        allowIntegrationDatabase);
    var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
        .UseNpgsql(connection)
        .Options;
    await using var context = new GarageBalanceDbContext(options);
    await context.Database.MigrateAsync();
    var seeder = new ShowcaseDataSeeder(context);
    var result = mode == "prepare"
        ? await seeder.PrepareAsync(CancellationToken.None)
        : await seeder.AuditAsync(CancellationToken.None);

    Console.WriteLine(
        $"showcaseStatus={(result.IsReady ? "ready" : "incomplete")}; mode={mode}; " +
        $"garages={result.GarageCount}; accruals={result.AccrualCount}; operations={result.FinancialOperationCount}; " +
        $"readings={result.MeterReadingCount}; campaigns={result.FeeCampaignCount}; suppliers={result.SupplierCount}; " +
        $"preservedUsers={result.PreservedUserCount}; noDebt={result.HasNoDebt}; debt={result.HasDebt}; advance={result.HasAdvance}; " +
        $"newGarageClean={result.NewGarageHasNoCalculatedHistory}; campaignParticipantsLocked={result.CampaignsHaveLockedParticipants}; " +
        $"annualAccrualsUnique={result.AnnualAccrualsAreUnique}; overdueScenarioCorrect={result.OverdueScenarioIsCorrect}");
    return result.IsReady ? 0 : 3;
}
catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"showcaseStatus=refused; reason={exception.Message}");
    return 2;
}
