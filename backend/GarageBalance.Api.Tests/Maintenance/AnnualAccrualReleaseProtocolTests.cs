using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Tests.Common;
using GarageBalance.StorageTool;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class AnnualAccrualReleaseProtocolTests
{
    [Fact]
    public async Task FreshExecutionJournalsBeforeReleaseAndVerifiesBeforeAnyRestart()
    {
        var host = new Host();
        await RunAsync(host);
        Assert.Equal(new[] { "deploy+", "verify-fresh", "capture", "marker+", "preparing", "drain", "activity+", "offline",
            "prepared", "executing", "release", "verify-commit", "receipt+", "marker-", "activity-", "restore",
            "permit", "participants", "complete", "deploy-" }, host.Events);
        Assert.False(host.Marker);
        Assert.False(host.Receipt);
    }

    [Theory]
    [InlineData("verify-fresh")]
    [InlineData("capture")]
    [InlineData("marker+")]
    [InlineData("preparing")]
    [InlineData("drain")]
    [InlineData("activity+")]
    [InlineData("offline")]
    [InlineData("prepared")]
    [InlineData("executing")]
    [InlineData("release")]
    [InlineData("verify-commit")]
    [InlineData("receipt+")]
    [InlineData("marker-")]
    public async Task UnknownOutcomeOrJournalFailureNeverRestartsOrRetries(string failure)
    {
        var host = new Host { Failure = failure };
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(host));
        Assert.DoesNotContain("restore", host.Events);
        Assert.DoesNotContain("permit", host.Events);
        Assert.False(host.DeployHeld);
        Assert.False(host.ActivityHeld);
        if (host.Events.Contains("preparing")) Assert.True(host.Marker);
        if (failure is "verify-fresh" or "capture" or "marker+" or "preparing" or "drain" or "activity+" or "offline" or "prepared" or "executing")
            Assert.DoesNotContain("release", host.Events);
        Assert.InRange(host.Events.Count(e => e == "release"), 0, 1);
    }

    [Theory]
    [InlineData("restore")]
    [InlineData("permit")]
    [InlineData("participants")]
    [InlineData("complete")]
    public async Task RestartFailureRetainsProvenOutcomeReceipt(string failure)
    {
        var host = new Host { Failure = failure };
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(host));
        Assert.True(host.Receipt);
        Assert.False(host.Marker);
        Assert.Equal(1, host.Events.Count(e => e == "release"));
        Assert.False(host.DeployHeld);
    }

    [Fact]
    public async Task RequiredCallbacksAndCanceledAcquireCannotExecute()
    {
        var host = new Host();
        await Assert.ThrowsAsync<ArgumentNullException>(() => AnnualAccrualReleaseProtocol.ExecuteAsync(null!, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => AnnualAccrualReleaseProtocol.ExecuteAsync(host, null!, _ => Task.CompletedTask, default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => AnnualAccrualReleaseProtocol.ExecuteAsync(host, (_, _) => Task.CompletedTask, null!, default));
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AnnualAccrualReleaseProtocol.ExecuteAsync(host, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, source.Token));
        Assert.Empty(host.Events);
    }

    [Fact]
    public async Task ProductionAuthorityHasNoConstructorAndCannotBeIssuedOnWindows()
    {
        Assert.Empty(typeof(AnnualAccrualReleasePermit).GetConstructors());
        var plan = Plan();
        await Assert.ThrowsAsync<ArgumentNullException>(() => AnnualAccrualReleasePermit.CreateAsync(null!, plan, default));
        Assert.Equal("cleanup_database_mismatch", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            StagingAnnualAccrualReleaseRunner.ExecuteAsync("Host=127.0.0.1;Database=unrelated", plan, plan.Fingerprint, null!, "test", new TestBusinessDateProvider(new(2026, 1, 1)), default))).Code);
        if (OperatingSystem.IsWindows())
        {
            const string connection = "Host=127.0.0.1;Port=1;Database=garagebalance_staging;Username=synthetic";
            Assert.Equal("cleanup_service_exclusion_unverified", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
                StagingAnnualAccrualReleaseRunner.ExecuteAsync(connection, plan, plan.Fingerprint, null!, "test", new TestBusinessDateProvider(new(2026, 1, 1)), default))).Code);
            Assert.Equal("cleanup_service_exclusion_unverified", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
                StagingAnnualAccrualReleaseRunner.RecoverAsync(connection, plan, default))).Code);
        }
    }

    [Fact]
    public void RecoveryIdentityBindsOperationPlanPhaseAndParticipantState()
    {
        var plan = Plan();
        var state = new MaintenanceRecoveryState(plan.PlanId, true, false, false, null, new(true, false, []));
        StagingAnnualAccrualReleaseRecovery.ValidateIdentity(state, null, plan);
        StagingAnnualAccrualReleaseRecovery.ValidateIdentity(state, new(plan.PlanId, "preparing", null), plan);
        StagingAnnualAccrualReleaseRecovery.ValidateIdentity(state, new(plan.PlanId, "prepared", plan.Fingerprint), plan);
        StagingAnnualAccrualReleaseRecovery.ValidateIdentity(state with { ReceiptPresent = true, ReceiptFingerprint = plan.Fingerprint, ReceiptOutcome = "committed" },
            new(plan.PlanId, "executing", plan.Fingerprint), plan);
        foreach (var invalid in new[] { state with { PlanId = Guid.NewGuid() }, state with { Participants = null },
            state with { ReceiptPresent = true, ReceiptFingerprint = new string('f', 64), ReceiptOutcome = "committed" },
            state with { ReceiptPresent = true, ReceiptFingerprint = plan.Fingerprint, ReceiptOutcome = null } })
            Assert.Throws<InvalidOperationException>(() => StagingAnnualAccrualReleaseRecovery.ValidateIdentity(invalid, null, plan));
        foreach (var progress in new[] { new MaintenancePreparationProgress(Guid.NewGuid(), "executing", plan.Fingerprint),
            new(plan.PlanId, "executing", new string('f', 64)), new(plan.PlanId, "unknown", plan.Fingerprint), new(plan.PlanId, "preparing", plan.Fingerprint) })
            Assert.Throws<InvalidOperationException>(() => StagingAnnualAccrualReleaseRecovery.ValidateIdentity(state, progress, plan));
        var differentOperation = plan with { OperationKind = "selective-financial-cleanup" };
        differentOperation = differentOperation with { Fingerprint = AnnualAccrualHoldReleaseEngine.Fingerprint(differentOperation) };
        Assert.Throws<AccountingCleanupException>(() => AnnualAccrualHoldReleaseEngine.ValidatePlan(differentOperation));
    }

    private static AnnualAccrualHoldReleasePlan Plan()
    {
        var source = new AccountingCleanupSource("synthetic", "127.0.0.1", 5432, "garagebalance_it_test", "source", new string('c', 64));
        var forecast = new AnnualAccrualForecast(1, 2026, new(2026, 1, 1), "UTC", DateTimeOffset.UtcNow, source, true,
            AnnualAccrualForecastEngine.Scope, 0, 0, 0m, [], [], true, new string('a', 64), new string('b', 64));
        var plan = new AnnualAccrualHoldReleasePlan(Guid.NewGuid(), DateTimeOffset.UtcNow, source, 2026, Guid.NewGuid(), Guid.NewGuid(),
            new string('d', 64), new string('e', 64), "", 2, AnnualAccrualHoldReleaseEngine.Operation, forecast);
        return plan with { Fingerprint = AnnualAccrualHoldReleaseEngine.Fingerprint(plan) };
    }

    private static Task RunAsync(Host host) => AnnualAccrualReleaseProtocol.ExecuteAsync(host, (phase, _) =>
    {
        Assert.True(host.DeployHeld);
        if (phase is "prepared" or "executing") Assert.True(host.ActivityHeld && host.Marker);
        host.Record(phase);
        return Task.CompletedTask;
    }, _ =>
    {
        Assert.True(host.DeployHeld && host.ActivityHeld && host.Marker);
        Assert.Equal("executing", host.Events.Last());
        host.Record("release");
        return Task.CompletedTask;
    }, default);

    private sealed class Host : IMaintenanceBarrierHost
    {
        public List<string> Events { get; } = [];
        public string? Failure { get; init; }
        public bool DeployHeld { get; private set; }
        public bool ActivityHeld { get; private set; }
        public bool Marker { get; private set; }
        public bool Receipt { get; private set; }
        public void Record(string action) { Events.Add(action); if (Failure == action) throw new InvalidOperationException(action); }
        public ValueTask<IAsyncDisposable> AcquireDeployGateAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Record("deploy+"); DeployHeld = true;
            return ValueTask.FromResult<IAsyncDisposable>(new Lease(() => { DeployHeld = false; Record("deploy-"); }));
        }
        public ValueTask<IAsyncDisposable> AcquireActivityGateAsync(CancellationToken token)
        {
            Record("activity+"); ActivityHeld = true;
            return ValueTask.FromResult<IAsyncDisposable>(new Lease(() => { ActivityHeld = false; Record("activity-"); }));
        }
        public Task CaptureServiceStateAsync(CancellationToken token) { Record("capture"); return Task.CompletedTask; }
        public Task CreateDurableMarkerAsync(CancellationToken token) { Record("marker+"); Marker = true; return Task.CompletedTask; }
        public Task StopAndDrainAsync(CancellationToken token) { Record("drain"); return Task.CompletedTask; }
        public Task VerifyOfflineAsync(CancellationToken token) { Record("offline"); return Task.CompletedTask; }
        public Task VerifyCommittedAsync(CancellationToken token) { Record("verify-commit"); return Task.CompletedTask; }
        public Task CreateRecoveryReceiptAsync(CancellationToken token) { Record("receipt+"); Receipt = true; return Task.CompletedTask; }
        public Task RemoveDurableMarkerAsync(CancellationToken token) { Assert.True(Receipt); Record("marker-"); Marker = false; return Task.CompletedTask; }
        public Task RestoreServiceAsync(CancellationToken token) { Assert.True(DeployHeld); Assert.False(ActivityHeld); Record("restore"); return Task.CompletedTask; }
        public Task PermitParticipantsAsync(CancellationToken token) { Record("permit"); return Task.CompletedTask; }
        public Task RestoreParticipantsAsync(CancellationToken token) { Record("participants"); return Task.CompletedTask; }
        public Task CompleteRecoveryAsync(CancellationToken token) { Record("complete"); Receipt = false; return Task.CompletedTask; }
        private sealed class Lease(Action release) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
        }
    }
}
