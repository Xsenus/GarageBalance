using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Tests.Finance;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using GarageBalance.StorageTool;
using Microsoft.Extensions.Configuration;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class AnnualAccrualCommandsTests
{
    private static string PrivatePath() => Path.Combine(Path.GetTempPath(), $"garagebalance-annual-cli-{Guid.NewGuid():N}.json");

    [Theory]
    [InlineData("annual-accrual-impact-plan")]
    [InlineData("annual-accrual-release-plan")]
    public void AnnualPlans_RequireExplicitYearDatabaseAndNewPrivateOutput(string command)
    {
        var args = new[] { command, "--annual-hold-year", "2026", "--expected-database", "synthetic-database", "--output", PrivatePath() };
        var options = AccountingCleanupCommandOptions.Parse(args);
        Assert.False(options.Execute);
        Assert.Equal(2026, options.AnnualHoldYear);
        Assert.Equal("synthetic-database", options.ExpectedDatabase);
        Assert.True(AccountingCleanupCommands.IsPreview(command.ToUpperInvariant()));
        for (var index = 1; index < args.Length; index += 2)
            Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(args.Take(index).Concat(args.Skip(index + 2)).ToArray()));
        foreach (var year in new[] { "0", "1899", "9999", "10000", "invalid" })
            Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(args.Select(value => value == "2026" ? year : value).ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([.. args, "--execute"]));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([.. args, "--max-garages", "5"]));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([.. args, "--output", PrivatePath()]));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([.. args, "--application-port", "3101"]));
    }

    [Fact]
    public void AnnualRecovery_RequiresExactPlanFingerprintAndStagingTargetWithoutMutatingOptions()
    {
        var args = RecoveryArguments();
        Assert.True(AccountingCleanupCommands.CanHandle(args[0].ToUpperInvariant()));
        Assert.False(AccountingCleanupCommands.IsPreview(args[0]));
        Assert.True(AccountingCleanupCommandOptions.Parse(args).Execute);
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(args.Where(value => value != "--execute").ToArray()));
        for (var index = 2; index < args.Length; index += 2)
            Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(args.Take(index).Concat(args.Skip(index + 2)).ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(args.Select(value => value == "garagebalance_staging" ? "other" : value).ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(args.Select(value => value == "garagebalance-staging.service" ? "other.service" : value).ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(args.Select(value => value == new string('a', 64) ? new string('g', 64) : value).ToArray()));
        foreach (var extra in new[]
        {
            new[] { "--backup-receipt", PrivatePath() },
            new[] { "--reason", "Synthetic reason" },
            new[] { "--output", PrivatePath() },
            new[] { "--annual-hold-year", "2026" },
            new[] { "--operation-id", Guid.NewGuid().ToString("D") }
        }) Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([.. args, .. extra]));
    }

    [Theory]
    [InlineData("annual-accrual-release")]
    [InlineData("annual-accrual-release-recover")]
    public async Task AnnualExecution_DeniesWrongTargetBeforeReadingFilesOrOpeningConnection(string command)
    {
        var args = command == "annual-accrual-release-recover" ? RecoveryArguments() : new[]
        {
            command, "--execute", "--plan", PrivatePath(), "--confirm", new string('a', 64),
            "--backup-receipt", PrivatePath(), "--expected-database", "garagebalance_staging",
            "--reason", "Synthetic release approval", "--service", "garagebalance-staging.service"
        };
        args[Array.IndexOf(args, "--expected-database") + 1] = "unrelated-database";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=forbidden.invalid;Database=unrelated-database;Username=synthetic;Password=never-print-this-fixture"
        }).Build();
        Assert.Equal(2, await AccountingCleanupCommands.RunAsync(args, configuration, CancellationToken.None));
    }

    [Theory]
    [InlineData("annual-accrual-release")]
    [InlineData("annual-accrual-release-recover")]
    public void AnnualMutatingCommands_RefuseWithoutExecuteAndNeverFallBackToAnotherPlan(string command)
    {
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([command, "--output", PrivatePath()]));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ImpactCliWritesPrivateSnapshotWithoutMutatingOrOverwriting()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            context.Add(new ApplicationSetting { Key = ApplicationSettingsService.BusinessDateOverrideKey, DateValue = new DateOnly(2026, 11, 9) });
            await context.SaveChangesAsync();
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
            ["Finance:RegularAccrualAutomation:TimeZoneId"] = "UTC"
        }).Build();
        string auditBefore;
        string settingsBefore;
        await using (var before = database.CreateContext())
        {
            auditBefore = JsonSerializer.Serialize(await before.AuditEvents.AsNoTracking().OrderBy(a => a.Id).ToListAsync());
            settingsBefore = JsonSerializer.Serialize(await before.ApplicationSettings.AsNoTracking().OrderBy(s => s.Key).ToListAsync());
        }
        var name = new Npgsql.NpgsqlConnectionStringBuilder(database.ConnectionString).Database!;
        var output = PrivatePath();
        var incomplete = PrivatePath();
        var canceled = PrivatePath();
        var mismatch = PrivatePath();
        string[] Arguments(int year, string path, string? expected = null) => ["annual-accrual-impact-plan", "--annual-hold-year", year.ToString(),
            "--expected-database", expected ?? name, "--output", path];
        try
        {
            Assert.Equal(0, await AccountingCleanupCommands.RunAsync(Arguments(2026, output), configuration, CancellationToken.None));
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(output));
            Assert.True(json.RootElement.GetProperty("isComplete").GetBoolean());
            Assert.Equal("2026-11-09", json.RootElement.GetProperty("businessDate").GetString());
            Assert.Equal("UTC", json.RootElement.GetProperty("timeZoneId").GetString());
            Assert.Equal(0, json.RootElement.GetProperty("newAccrualAmount").GetDecimal());
            var original = await File.ReadAllBytesAsync(output);
            Assert.Equal(6, await AccountingCleanupCommands.RunAsync(Arguments(2026, output), configuration, CancellationToken.None));
            Assert.Equal(original, await File.ReadAllBytesAsync(output));
            Assert.Equal(3, await AccountingCleanupCommands.RunAsync(Arguments(2027, incomplete), configuration, CancellationToken.None));
            using var partial = JsonDocument.Parse(await File.ReadAllTextAsync(incomplete));
            Assert.False(partial.RootElement.GetProperty("isComplete").GetBoolean());
            Assert.Equal(2, await AccountingCleanupCommands.RunAsync(Arguments(2026, mismatch, "unrelated-database"), configuration, CancellationToken.None));
            Assert.False(File.Exists(mismatch));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Equal(130, await AccountingCleanupCommands.RunAsync(Arguments(2026, canceled), configuration, cancellation.Token));
            Assert.False(File.Exists(canceled));
            await using var verification = database.CreateContext();
            Assert.Empty(await verification.Accruals.ToListAsync());
            Assert.Equal(auditBefore, JsonSerializer.Serialize(await verification.AuditEvents.AsNoTracking().OrderBy(a => a.Id).ToListAsync()));
            Assert.Equal(settingsBefore, JsonSerializer.Serialize(await verification.ApplicationSettings.AsNoTracking().OrderBy(s => s.Key).ToListAsync()));
        }
        finally
        {
            foreach (var path in new[] { output, incomplete, canceled, mismatch }) File.Delete(path);
        }
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ReleasePlanRoundtripBindsForecastAndDoesNotReleaseHold()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            await AnnualAccountingStartTests.SeedAsync(context, new DateOnly(2026, 1, 1));
            context.AddRange(
                new ApplicationSetting { Key = ApplicationSettingsService.BusinessDateOverrideKey, DateValue = new DateOnly(2026, 11, 9) },
                new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2026), BooleanValue = true, IntegerValue = 2026 });
            (await context.ApplicationSettings.SingleAsync(s => s.Key == "finance.annual_accrual_guard_epoch")).JsonValue = "{\"2026\":true}";
            await context.SaveChangesAsync();
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
            ["Finance:RegularAccrualAutomation:TimeZoneId"] = "UTC"
        }).Build();
        var path = PrivatePath();
        var args = new[] { "annual-accrual-release-plan", "--annual-hold-year", "2026", "--expected-database",
            new Npgsql.NpgsqlConnectionStringBuilder(database.ConnectionString).Database!, "--output", path };
        try
        {
            Assert.Equal(0, await AccountingCleanupCommands.RunAsync(args, configuration, CancellationToken.None));
            var bytes = await File.ReadAllBytesAsync(path);
            var plan = JsonSerializer.Deserialize<AnnualAccrualHoldReleasePlan>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            AnnualAccrualHoldReleaseEngine.ValidatePlan(plan);
            Assert.Equal(2, plan.FormatVersion);
            Assert.Equal(AnnualAccrualHoldReleaseEngine.Operation, plan.OperationKind);
            Assert.Equal(2026, plan.Year);
            Assert.True(plan.Forecast!.IsComplete);
            Assert.True(plan.Forecast.HoldActive);
            Assert.Equal(1200m, plan.Forecast.NewAccrualAmount);
            Assert.Equal(AnnualAccrualHoldReleaseEngine.Fingerprint(plan), plan.Fingerprint);
            Assert.Equal(6, await AccountingCleanupCommands.RunAsync(args, configuration, CancellationToken.None));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            await using var check = database.CreateContext();
            Assert.True((await check.ApplicationSettings.SingleAsync(s => s.Key == AnnualAccrualHoldPolicy.Key(2026))).BooleanValue);
            Assert.Empty(await check.Accruals.ToListAsync());
            Assert.Empty(await check.AuditEvents.Where(a => a.Action == AnnualAccrualHoldReleaseEngine.AuditAction).ToListAsync());
        }
        finally { File.Delete(path); }
    }

    private static string[] RecoveryArguments() => ["annual-accrual-release-recover", "--execute", "--plan", PrivatePath(),
        "--confirm", new string('a', 64), "--expected-database", "garagebalance_staging", "--service", "garagebalance-staging.service"];
}

[CollectionDefinition("Annual CLI console", DisableParallelization = true)]
public sealed class AnnualCliConsoleCollection { }

[Collection("Annual CLI console")]
public sealed class AnnualAccrualCommandDiagnosticsTests
{
    [Theory]
    [InlineData("annual-accrual-release", true)]
    [InlineData("ANNUAL-ACCRUAL-RELEASE-RECOVER", true)]
    [InlineData("annual-accrual-release-plan", false)]
    [InlineData("annual-accrual-impact-plan", false)]
    public async Task ExpectedFailure_ExplainsRecoveryOnlyForMutatingAnnualCommands(string command, bool recoveryExpected)
    {
        var previous = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetError(output);
            Assert.Equal(2, await AccountingCleanupCommands.RunAsync([command],
                new ConfigurationBuilder().Build(), CancellationToken.None));
            var diagnostic = output.ToString();
            Assert.Contains("cleanup_options_invalid:", diagnostic, StringComparison.Ordinal);
            Assert.Equal(recoveryExpected, diagnostic.Contains("annual_release_recovery_required_if_started:", StringComparison.Ordinal));
            if (recoveryExpected)
            {
                Assert.Contains("annual-accrual-release-recover", diagnostic, StringComparison.Ordinal);
                Assert.Contains("maintenance marker", diagnostic, StringComparison.Ordinal);
            }
        }
        finally { Console.SetError(previous); }
    }
}
