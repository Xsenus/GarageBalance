using GarageBalance.StorageTool;
using Microsoft.Extensions.Configuration;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class MaintenanceBarrierCoordinatorTests
{
    [Theory]
    [InlineData("selective-financial-cleanup")]
    [InlineData("selective-financial-recover")]
    public async Task StagingEntrypointRemainsBlockedBeforeOpeningPlanOrConnecting(string command)
    {
        var arguments = new List<string> { command, "--execute", "--plan", Path.Combine(Path.GetTempPath(), "unavailable-plan.json"),
            "--confirm", new string('a', 64), "--expected-database", "garagebalance_staging", "--service", StagingOfflineProof.Service };
        if (command == "selective-financial-cleanup") arguments.AddRange(["--backup-receipt", Path.Combine(Path.GetTempPath(), "unavailable-receipt.json"), "--reason", "Synthetic approval"]);
        var result = await AccountingCleanupCommands.RunAsync(arguments.ToArray(), new ConfigurationBuilder().Build(), default);
        Assert.Equal(2, result);
    }

    [Fact]
    public void LinuxHostFailsClosedOnOtherPlatforms()
    {
        if (OperatingSystem.IsLinux()) return;
        var error = Assert.Throws<InvalidOperationException>(() => new LinuxMaintenanceBarrierHost(Guid.NewGuid(), new string('a', 64), _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, () => true));
        Assert.Equal("maintenance_host_unavailable", error.Message);
    }

    [Fact]
    public async Task SuccessfulOperationKeepsBothGatesUntilMarkerRemoval()
    {
        var host = new FakeHost();
        await MaintenanceBarrierCoordinator.ExecuteAsync(host, _ =>
        {
            Assert.True(host.DeployHeld && host.ActivityHeld && host.Marker);
            host.Events.Add("commit");
            return Task.CompletedTask;
        }, default);
        Assert.Equal(new[] { "deploy+", "capture", "marker+", "drain", "activity+", "offline", "commit", "verified", "receipt+", "marker-", "activity-", "restore", "permit", "participants", "receipt-", "deploy-" }, host.Events);
        Assert.False(host.Marker);
    }

    [Theory]
    [InlineData("activity+")]
    [InlineData("drain")]
    [InlineData("offline")]
    [InlineData("commit")]
    [InlineData("verified")]
    [InlineData("receipt+")]
    [InlineData("marker-")]
    public async Task FailureKeepsMarkerAndReleasesAcquiredGates(string failure)
    {
        var host = new FakeHost { Failure = failure };
        await Assert.ThrowsAsync<InvalidOperationException>(() => MaintenanceBarrierCoordinator.ExecuteAsync(host, _ =>
        {
            host.Record("commit");
            return Task.CompletedTask;
        }, default));
        Assert.True(host.Marker);
        Assert.False(host.DeployHeld);
        Assert.False(host.ActivityHeld);
    }

    [Fact]
    public async Task PreparedExecutionAdoptsOriginalMarkerAndRecordsExecutingBeforeAnyOperation()
    {
        var host = new FakeHost();
        await host.CreateDurableMarkerAsync(default);
        host.Events.Clear();
        await MaintenanceBarrierCoordinator.ExecutePreparedAsync(host, _ =>
        {
            Assert.True(host.DeployHeld && host.Marker);
            host.Record("adopt");
            return Task.CompletedTask;
        }, _ =>
        {
            Assert.True(host.DeployHeld && host.ActivityHeld && host.Marker);
            host.Record("executing");
            return Task.CompletedTask;
        }, _ => { host.Record("commit"); return Task.CompletedTask; }, default);
        Assert.Equal(new[] { "deploy+", "adopt", "drain", "activity+", "offline", "executing", "commit", "verified", "receipt+", "marker-", "activity-", "restore", "permit", "participants", "receipt-", "deploy-" }, host.Events);
    }

    [Theory]
    [InlineData("adopt")]
    [InlineData("executing")]
    public async Task PreparedJournalFailureNeverCallsOperation(string failure)
    {
        var host = new FakeHost { Failure = failure };
        await host.CreateDurableMarkerAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => MaintenanceBarrierCoordinator.ExecutePreparedAsync(host,
            _ => { host.Record("adopt"); return Task.CompletedTask; },
            _ => { host.Record("executing"); return Task.CompletedTask; },
            _ => throw new Exception("Must not execute"), default));
        Assert.True(host.Marker);
        Assert.False(host.DeployHeld || host.ActivityHeld);
        Assert.DoesNotContain("commit", host.Events);
    }

    [Fact]
    public async Task PreparedExecutionRejectsMissingRequiredCallbacks()
    {
        var host = new FakeHost();
        await Assert.ThrowsAsync<ArgumentNullException>(() => MaintenanceBarrierCoordinator.ExecutePreparedAsync(host, null!, _ => Task.CompletedTask, _ => Task.CompletedTask, default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => MaintenanceBarrierCoordinator.ExecutePreparedAsync(host, _ => Task.CompletedTask, null!, _ => Task.CompletedTask, default));
        Assert.Empty(host.Events);
    }

    [Theory]
    [InlineData("restore")]
    [InlineData("permit")]
    [InlineData("participants")]
    [InlineData("receipt-")]
    public async Task RestartFailureLeavesRecoveryReceipt(string failure)
    {
        var host = new FakeHost { Failure = failure };
        await Assert.ThrowsAsync<InvalidOperationException>(() => MaintenanceBarrierCoordinator.ExecuteAsync(host, _ => Task.CompletedTask, default));
        Assert.True(host.Receipt);
        Assert.False(host.Marker);
        Assert.False(host.DeployHeld);
        Assert.False(host.ActivityHeld);
    }

    private sealed class FakeHost : IMaintenanceBarrierHost
    {
        public List<string> Events { get; } = [];
        public string? Failure { get; init; }
        public bool Marker { get; private set; }
        public bool DeployHeld { get; private set; }
        public bool ActivityHeld { get; private set; }
        public bool Receipt { get; private set; }
        public void Record(string entry) { Events.Add(entry); if (Failure == entry) throw new InvalidOperationException(entry); }
        public ValueTask<IAsyncDisposable> AcquireDeployGateAsync(CancellationToken token)
        {
            Record("deploy+"); DeployHeld = true;
            return ValueTask.FromResult<IAsyncDisposable>(new Lease(() => { DeployHeld = false; Events.Add("deploy-"); }));
        }
        public Task CreateDurableMarkerAsync(CancellationToken token) { Record("marker+"); Marker = true; return Task.CompletedTask; }
        public Task CaptureServiceStateAsync(CancellationToken token) { Record("capture"); return Task.CompletedTask; }
        public Task StopAndDrainAsync(CancellationToken token) { Record("drain"); return Task.CompletedTask; }
        public ValueTask<IAsyncDisposable> AcquireActivityGateAsync(CancellationToken token)
        {
            Record("activity+"); ActivityHeld = true;
            return ValueTask.FromResult<IAsyncDisposable>(new Lease(() => { ActivityHeld = false; Events.Add("activity-"); }));
        }
        public Task VerifyOfflineAsync(CancellationToken token) { Record("offline"); return Task.CompletedTask; }
        public Task VerifyCommittedAsync(CancellationToken token) { Record("verified"); return Task.CompletedTask; }
        public Task CreateRecoveryReceiptAsync(CancellationToken token) { Record("receipt+"); Receipt = true; return Task.CompletedTask; }
        public Task RemoveDurableMarkerAsync(CancellationToken token) { Record("marker-"); Marker = false; return Task.CompletedTask; }
        public Task RestoreServiceAsync(CancellationToken token) { Assert.True(DeployHeld); Assert.False(ActivityHeld); Record("restore"); return Task.CompletedTask; }
        public Task PermitParticipantsAsync(CancellationToken token) { Assert.True(Receipt); Assert.False(Marker); Record("permit"); return Task.CompletedTask; }
        public Task RestoreParticipantsAsync(CancellationToken token) { Assert.True(Receipt); Assert.False(Marker); Record("participants"); return Task.CompletedTask; }
        public Task CompleteRecoveryAsync(CancellationToken token) { Record("receipt-"); Receipt = false; return Task.CompletedTask; }
        private sealed class Lease(Action release) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
        }
    }
}
