using GarageBalance.Api.Application.Maintenance;
using GarageBalance.StorageTool;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class StagingMaintenancePreparationTests
{
    [Fact]
    public void LocalRecoveryRequiresPairedExactConfirmationAndOfflineStageOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), "synthetic-maintenance-plan.json");
        var fingerprint = new string('a', 64);
        string[] stage = ["selective-financial-stage", "--execute", "--output", path, "--annual-hold-year", "2026",
            "--expected-database", "garagebalance_staging", "--service", StagingOfflineProof.Service, "--reason", "synthetic test"];
        Assert.Null(AccountingCleanupCommandOptions.Parse(stage).LocalRecoveryPlanPath);
        var parsed = AccountingCleanupCommandOptions.Parse([.. stage, "--local-recovery-plan", path, "--local-recovery-confirm", fingerprint]);
        Assert.Equal(path, parsed.LocalRecoveryPlanPath);
        Assert.Equal(fingerprint, parsed.LocalRecoveryFingerprint);
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([.. stage, "--local-recovery-plan", path]));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([.. stage, "--local-recovery-confirm", fingerprint]));
        foreach (var invalid in new[] { "invalid", new string('x', 64), new string('a', 63) })
            Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([.. stage, "--local-recovery-plan", path, "--local-recovery-confirm", invalid]));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([.. stage, "--local-recovery-plan", "relative.json", "--local-recovery-confirm", fingerprint]));
        foreach (var command in new[] { "selective-financial-plan", "selective-financial-cleanup", "selective-financial-recover", "selective-financial-abort-preparation" })
            Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([command, .. stage.Skip(1), "--local-recovery-plan", path, "--local-recovery-confirm", fingerprint]));
    }

    [Fact]
    public void PreparationAbortRequiresExactRecordedIdentityAndCannotCarryDeletionOptions()
    {
        var id = Guid.NewGuid();
        string[] valid = ["selective-financial-abort-preparation", "--execute", "--operation-id", id.ToString("D"),
            "--expected-database", "garagebalance_staging", "--service", StagingOfflineProof.Service];
        Assert.Equal(id, AccountingCleanupCommandOptions.Parse(valid).PreparationOperationId);
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(valid.Where(v => v != "--execute").ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(valid.Select(v => v == id.ToString("D") ? "invalid" : v).ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(valid.Select(v => v == id.ToString("D") ? Guid.Empty.ToString("D") : v).ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(valid.Select(v => v == "garagebalance_staging" ? "other" : v).ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([.. valid, "--plan", Path.Combine(Path.GetTempPath(), "wrong-plan.json")]));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(["selective-financial-plan", "--output", Path.Combine(Path.GetTempPath(), "wrong-plan.json"), "--operation-id", id.ToString("D")]));
    }
    [Fact]
    public async Task PreparesPlanOnlyAfterDrainAndLeavesDurableOfflineState()
    {
        var host = new Host();
        var id = Guid.NewGuid();
        var persisted = false;
        var plan = await StagingMaintenancePreparation.PrepareAsync(host, id, (actualId, _) =>
        {
            Assert.Equal(id, actualId);
            Assert.True(host.DeployHeld && host.ActivityHeld && host.Marker && !host.Running);
            host.Events.Add("preview");
            return Task.FromResult(Plan(actualId));
        }, (prepared, _) =>
        {
            Assert.Equal(id, prepared.PlanId);
            Assert.True(host.DeployHeld && host.ActivityHeld && host.Marker);
            host.Events.Add("persist");
            persisted = true;
            return Task.CompletedTask;
        }, default);
        Assert.True(persisted && host.Marker && !host.Running);
        Assert.False(host.DeployHeld || host.ActivityHeld);
        Assert.Equal(id, plan.PlanId);
        Assert.Equal(new[] { "deploy+", "capture", "marker+", "drain", "activity+", "offline", "preview", "persist", "offline", "activity-", "deploy-" }, host.Events);
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("persist")]
    public async Task FailureKeepsMarkerAndCannotRestartOrDelete(string failure)
    {
        var host = new Host();
        await Assert.ThrowsAsync<InvalidOperationException>(() => StagingMaintenancePreparation.PrepareAsync(host, Guid.NewGuid(), (id, _) =>
        {
            if (failure == "preview") throw new InvalidOperationException("Synthetic preview failure");
            return Task.FromResult(Plan(id));
        }, (_, _) => throw new InvalidOperationException("Synthetic persistence failure"), default));
        Assert.True(host.Marker && !host.Running);
        Assert.False(host.DeployHeld || host.ActivityHeld);
    }

    [Fact]
    public async Task DurablePhaseTransitionsSurroundOnlyOfflinePlanPersistence()
    {
        var host = new Host();
        await StagingMaintenancePreparation.PrepareAsync(host, Guid.NewGuid(),
            (id, _) => { host.Events.Add("preview"); return Task.FromResult(Plan(id)); },
            (_, _) => { host.Events.Add("persist"); return Task.CompletedTask; }, default,
            _ => { Assert.True(host.Marker && host.DeployHeld); host.Events.Add("preparing"); return Task.CompletedTask; },
            (_, _) => { Assert.True(host.ActivityHeld); host.Events.Add("prepared"); return Task.CompletedTask; });
        Assert.Equal(new[] { "deploy+", "capture", "marker+", "preparing", "drain", "activity+", "offline", "preview", "persist", "prepared", "offline", "activity-", "deploy-" }, host.Events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task JournalFailureCannotRemoveMarkerOrRestart(bool preparingFails)
    {
        var host = new Host();
        await Assert.ThrowsAsync<IOException>(() => StagingMaintenancePreparation.PrepareAsync(host, Guid.NewGuid(),
            (id, _) => Task.FromResult(Plan(id)), (_, _) => Task.CompletedTask, default,
            _ => preparingFails ? throw new IOException("Synthetic sync failure") : Task.CompletedTask,
            (_, _) => throw new IOException("Synthetic sync failure")));
        Assert.True(host.Marker);
        Assert.False(host.DeployHeld || host.ActivityHeld);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("year")]
    [InlineData("hash")]
    public async Task InvalidPlanCannotBePersisted(string fault)
    {
        var host = new Host();
        var persisted = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => StagingMaintenancePreparation.PrepareAsync(host, Guid.NewGuid(), (id, _) =>
        {
            var plan = Plan(fault == "id" ? Guid.NewGuid() : id);
            plan = fault == "year" ? plan with { HeldAnnualYear = 2027 } : plan;
            plan = fault == "hash" ? plan with { Fingerprint = new string('0', 64) } : plan;
            return Task.FromResult(plan);
        }, (_, _) => { persisted = true; return Task.CompletedTask; }, default));
        Assert.False(persisted);
        Assert.True(host.Marker);
    }

    [Fact]
    public async Task InvalidArgumentsAndCancellationFailBeforeAnyStateChange()
    {
        var host = new Host();
        await Assert.ThrowsAsync<ArgumentException>(() => StagingMaintenancePreparation.PrepareAsync(host, Guid.Empty,
            (id, _) => Task.FromResult(Plan(id)), (_, _) => Task.CompletedTask, default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => StagingMaintenancePreparation.PrepareAsync(host, Guid.NewGuid(), null!, (_, _) => Task.CompletedTask, default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StagingMaintenancePreparation.PrepareAsync(host, Guid.NewGuid(),
            (id, _) => Task.FromResult(Plan(id)), (_, _) => Task.CompletedTask, cancellation.Token));
        Assert.Empty(host.Events);
        Assert.False(host.Marker);
        Assert.True(host.Running);
    }

    [Fact]
    public void StageOptionsRequireExplicitOfflineConsentAndExactTarget()
    {
        string[] valid = ["selective-financial-stage", "--execute", "--output", Path.Combine(Path.GetTempPath(), "synthetic-stage.json"),
            "--expected-database", "garagebalance_staging", "--service", StagingOfflineProof.Service, "--annual-hold-year", "2026", "--reason", "Synthetic authorized preparation"];
        var options = AccountingCleanupCommandOptions.Parse(valid);
        Assert.True(options.Execute);
        Assert.Equal(2026, options.AnnualHoldYear);
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(valid.Where(value => value != "--execute").ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(valid.Select(value => value == "2026" ? "2027" : value).ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(valid.Select(value => value == "garagebalance_staging" ? "other_database" : value).ToArray()));
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse([.. valid, "--plan", Path.Combine(Path.GetTempPath(), "unrelated.json")]));
    }

    private static SelectiveFinancialCleanupPlan Plan(Guid id)
    {
        var plan = new SelectiveFinancialCleanupPlan(2, id, DateTimeOffset.UtcNow,
            new("synthetic-host", "synthetic", 5432, "garagebalance_it_preparation", "synthetic-identity", new string('a', 64)),
            [], [], [], new Dictionary<string, string>(), "", 2026);
        return plan with { Fingerprint = SelectiveFinancialCleanupEngine.Fingerprint(plan) };
    }

    private sealed class Host : IMaintenanceBarrierHost
    {
        public List<string> Events { get; } = [];
        public bool DeployHeld { get; private set; }
        public bool ActivityHeld { get; private set; }
        public bool Marker { get; private set; }
        public bool Running { get; private set; } = true;
        public ValueTask<IAsyncDisposable> AcquireDeployGateAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Events.Add("deploy+"); DeployHeld = true;
            return ValueTask.FromResult<IAsyncDisposable>(new Lease(() => { Events.Add("deploy-"); DeployHeld = false; }));
        }
        public ValueTask<IAsyncDisposable> AcquireActivityGateAsync(CancellationToken token)
        {
            Assert.True(Marker && !Running);
            Events.Add("activity+"); ActivityHeld = true;
            return ValueTask.FromResult<IAsyncDisposable>(new Lease(() => { Events.Add("activity-"); ActivityHeld = false; }));
        }
        public Task CaptureServiceStateAsync(CancellationToken token) { Events.Add("capture"); return Task.CompletedTask; }
        public Task CreateDurableMarkerAsync(CancellationToken token) { Events.Add("marker+"); Marker = true; return Task.CompletedTask; }
        public Task StopAndDrainAsync(CancellationToken token) { Assert.True(Marker); Events.Add("drain"); Running = false; return Task.CompletedTask; }
        public Task VerifyOfflineAsync(CancellationToken token) { Assert.True(ActivityHeld && !Running && Marker); Events.Add("offline"); return Task.CompletedTask; }
        public Task VerifyCommittedAsync(CancellationToken token) => throw new InvalidOperationException("Preparation cannot commit.");
        public Task CreateRecoveryReceiptAsync(CancellationToken token) => throw new InvalidOperationException("Preparation cannot create a receipt.");
        public Task RemoveDurableMarkerAsync(CancellationToken token) => throw new InvalidOperationException("Preparation cannot remove its marker.");
        public Task RestoreServiceAsync(CancellationToken token) => throw new InvalidOperationException("Preparation must remain offline.");
        public Task PermitParticipantsAsync(CancellationToken token) => throw new InvalidOperationException("Preparation cannot permit participants.");
        public Task RestoreParticipantsAsync(CancellationToken token) => throw new InvalidOperationException("Preparation must remain offline.");
        public Task CompleteRecoveryAsync(CancellationToken token) => throw new InvalidOperationException("Preparation cannot complete recovery.");
        private sealed class Lease(Action release) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
        }
    }
}
