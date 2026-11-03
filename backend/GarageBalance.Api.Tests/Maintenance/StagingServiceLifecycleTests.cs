using GarageBalance.StorageTool;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class StagingServiceLifecycleTests
{
    [Fact]
    public async Task FixtureOfflineProofPropagatesSuccessFailureAndCancellation()
    {
        var calls = 0;
        var lifecycle = new StagingServiceLifecycle(offlineProof: token => { token.ThrowIfCancellationRequested(); calls++; return Task.CompletedTask; });
        await lifecycle.VerifyOfflineAsync(default);
        Assert.Equal(1, calls);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lifecycle.VerifyOfflineAsync(cancellation.Token));
        var failure = new StagingServiceLifecycle(offlineProof: _ => throw new IOException("Synthetic proof failure"));
        await Assert.ThrowsAsync<IOException>(() => failure.VerifyOfflineAsync(default));
    }
    [Fact]
    public async Task RecoveryResetsFailedUnitAndRestoresRecordedRunningService()
    {
        List<string> calls = [];
        var lifecycle = new StagingServiceLifecycle(WithInactiveParticipants((args, _) =>
        {
            calls.Add(args[0]);
            return Task.FromResult((0, "LoadState=loaded\nActiveState=failed\nSubState=failed\nFragmentPath=/etc/systemd/system/garagebalance-staging.service\n"));
        }), _ => Task.CompletedTask, allowFailedForRecovery: true);
        await lifecycle.CaptureStateAsync(default);
        await lifecycle.StopAndDrainAsync(default);
        await lifecycle.RestoreRecordedAsync(true, default);
        Assert.Equal(new[] { "show", "reset-failed", "start" }, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestoresOnlyPreviouslyRunningService(bool active)
    {
        List<string> calls = [];
        var healthCalled = false;
        var lifecycle = new StagingServiceLifecycle(WithInactiveParticipants((args, _) =>
        {
            calls.Add(args[0]);
            var state = active ? "ActiveState=active\nSubState=running\n" : "ActiveState=inactive\nSubState=dead\n";
            return Task.FromResult((0, "LoadState=loaded\n" + state + "FragmentPath=/etc/systemd/system/garagebalance-staging.service\n"));
        }), _ => { healthCalled = true; return Task.CompletedTask; });
        await lifecycle.CaptureStateAsync(default);
        await lifecycle.StopAndDrainAsync(default);
        await lifecycle.RestoreAsync(default);
        Assert.Equal(active ? new[] { "show", "stop", "start" } : new[] { "show" }, calls);
        Assert.Equal(active, healthCalled);
    }

    [Fact]
    public async Task UnknownStateFailsBeforeStop()
    {
        List<string> calls = [];
        var lifecycle = new StagingServiceLifecycle((args, _) =>
        {
            calls.Add(args[0]);
            return Task.FromResult((0, "LoadState=loaded\nActiveState=activating\nSubState=start\nFragmentPath=/etc/systemd/system/garagebalance-staging.service\n"));
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.CaptureStateAsync(default));
        Assert.Equal(new[] { "show" }, calls);
    }

    private static Func<string[], CancellationToken, Task<(int, string)>> WithInactiveParticipants(
        Func<string[], CancellationToken, Task<(int, string)>> application) => (args, token) =>
    {
        if (args[0] == "list-units") return Task.FromResult((0, ""));
        if (args[0] == "show" && args[1].EndsWith(".timer", StringComparison.Ordinal))
            return Task.FromResult((0, $"LoadState=loaded\nActiveState=inactive\nSubState=dead\nFragmentPath=/etc/systemd/system/{args[1]}\n"));
        if (args[0] == "stop" && args[1] is "garagebalance-healthcheck.timer" or "garagebalance-healthcheck.service")
            return Task.FromResult((0, ""));
        return application(args, token);
    };
}
