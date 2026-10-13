using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Tests.Common;
using GarageBalance.StorageTool;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class AccountingCleanupCommandsTests
{
    private static string PrivatePath(string suffix = "plan.json") => Path.Combine(Path.GetTempPath(), $"garagebalance-cleanup-cli-{Guid.NewGuid():N}-{suffix}");

    [Theory]
    [InlineData("archived-garages-plan")]
    [InlineData("archived-garages-cleanup")]
    public void Commands_DefaultToReadOnlyAndBindExplicitPrivateOutput(string command)
    {
        var path = PrivatePath();
        var options = AccountingCleanupCommandOptions.Parse([command, "--output", path, "--max-garages", "25"]);
        Assert.False(options.Execute);
        Assert.Equal(path, options.OutputPath);
        Assert.Equal(25, options.MaximumGarages);
        Assert.True(AccountingCleanupCommands.CanHandle(command.ToUpperInvariant()));
        Assert.False(AccountingCleanupCommands.CanHandle("delete-all"));
    }

    [Fact]
    public void Execute_RequiresExactPlanFingerprintBackupDatabaseReasonAndOneOfflineProof()
    {
        var args = ValidExecution();
        var options = AccountingCleanupCommandOptions.Parse(args);
        Assert.True(options.Execute);
        Assert.Equal("synthetic-database", options.ExpectedDatabase);
        Assert.Equal("garagebalance-staging.service", options.ServiceName);
        for (var index = 2; index < args.Length; index += 2)
            Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(args.Take(index).Concat(args.Skip(index + 2)).ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(args.Concat(["--execute"]).ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(args.Concat(["--application-port", "3101"]).ToArray()));
        var alternate = args.ToArray();
        var service = Array.IndexOf(alternate, "--service");
        alternate[service] = "--application-port";
        alternate[service + 1] = "3101";
        Assert.Equal(3101, AccountingCleanupCommandOptions.Parse(alternate).ApplicationPort);
    }

    [Theory]
    [InlineData("--max-garages", "0")]
    [InlineData("--max-garages", "501")]
    [InlineData("--max-garages", "invalid")]
    [InlineData("--unknown", "anything")]
    [InlineData("--plan", "relative.json")]
    public void Preview_RejectsInvalidExtraOptionsOrUnboundedLimit(string key, string value)
    {
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(["archived-garages-plan", "--output", PrivatePath(), key, value]));
    }

    [Theory]
    [InlineData("relative.json")]
    [InlineData("report.txt")]
    [InlineData("backup.pgdump.manifest.json")]
    public void PrivateReports_RejectRelativeWrongExtensionOrProtectedBackupManifest(string suffix)
    {
        var path = suffix == "relative.json" ? suffix : PrivatePath(suffix);
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommands.ValidatePrivateJsonPath(path));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_DeniesBeforeOpeningDatabaseOrFilesWhenHostProofIsMissing(bool privileged, bool stopped)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=forbidden.invalid;Database=synthetic-database;Username=synthetic;Password=never-print-this-fixture"
        }).Build();
        var result = await AccountingCleanupCommands.RunAsync(ValidExecution(), configuration, CancellationToken.None, new FakeHost(privileged, stopped));
        Assert.Equal(2, result);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_CliPreviewWritesOnlyPrivatePlanAndPreservesData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        await using (var context = database.CreateContext())
        {
            var garage = new Garage { Number = "synthetic-cli-archive", IsArchived = true, PeopleCount = 1, FloorCount = 1 };
            garageId = garage.Id;
            context.Add(garage);
            await context.SaveChangesAsync();
        }
        var path = PrivatePath();
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
            }).Build();
            var result = await AccountingCleanupCommands.RunAsync(["archived-garages-plan", "--output", path], configuration, CancellationToken.None);
            Assert.Equal(0, result);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(1, json.RootElement.GetProperty("totalEligible").GetInt32());
            Assert.Equal(garageId, json.RootElement.GetProperty("eligibleGarages")[0].GetProperty("garageId").GetGuid());
            var original = await File.ReadAllBytesAsync(path);
            Assert.Equal(6, await AccountingCleanupCommands.RunAsync(["archived-garages-plan", "--output", path], configuration, CancellationToken.None));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            await using var check = database.CreateContext();
            Assert.True(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(check.Garages, garage => garage.Id == garageId));
        }
        finally { File.Delete(path); }
    }

    private static string[] ValidExecution() => ["archived-garages-cleanup", "--execute", "--plan", PrivatePath(), "--confirm", new string('a', 64),
        "--backup-receipt", PrivatePath(), "--expected-database", "synthetic-database", "--reason", "Synthetic cleanup approval", "--service", "garagebalance-staging.service"];

    private sealed class FakeHost(bool privileged, bool stopped) : IAccountingCleanupHostEnvironment
    {
        public Task<AccountingCleanupExecutionIdentity> GetIdentityAsync(string? serviceName, int? applicationPort, CancellationToken cancellationToken) =>
            Task.FromResult(new AccountingCleanupExecutionIdentity(privileged, stopped, "synthetic-test"));
    }
}
