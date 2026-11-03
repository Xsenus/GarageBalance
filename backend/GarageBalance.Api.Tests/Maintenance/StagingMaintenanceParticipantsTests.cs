using System.Text;
using GarageBalance.StorageTool;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class StagingMaintenanceParticipantsTests
{
    private const string Delta = "garagebalance-storage-tool@delta-sync.service";
    private const string Resume = "garagebalance-storage-tool@resume.service";

    [Theory]
    [InlineData("waiting")]
    [InlineData("elapsed")]
    [InlineData("running")]
    public async Task ActiveTimersRemainRecordedAcrossCaptureDrainAndRestore(string substate)
    {
        var systemd = new FakeSystemd { ActiveTimerSubstate = substate };
        systemd.Jobs.Add(Delta);
        var participants = new StagingMaintenanceParticipants(systemd.Run);
        var recorded = await participants.CaptureAsync(default);
        Assert.True(recorded.HealthTimerWasActive && recorded.StorageTimerWasActive);
        Assert.Equal(new[] { Delta }, recorded.ActiveStorageUnits);
        await participants.StopAndDrainAsync(default);
        await participants.VerifyDrainedAsync(default);
        Assert.Empty(systemd.Jobs);
        Assert.All(systemd.Timers.Values, active => Assert.False(active));
        await participants.RestoreAsync(recorded, default);
        Assert.All(systemd.Timers.Values, active => Assert.True(active));
        Assert.Equal(new[] { Delta }, systemd.Jobs);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ParticipantStateRoundTripsExactTimerAndWorkerState(bool health, bool storage)
    {
        var state = new MaintenanceParticipantState(health, storage, [Delta, Resume]);
        var decoded = MaintenanceParticipantState.Decode(state.Encode());
        Assert.Equal(health, decoded.HealthTimerWasActive);
        Assert.Equal(storage, decoded.StorageTimerWasActive);
        Assert.Equal(state.ActiveStorageUnits, decoded.ActiveStorageUnits);
        Assert.Equal(state.Encode(), decoded.Encode());
        Assert.Empty(MaintenanceParticipantState.Decode(new MaintenanceParticipantState(false, false, []).Encode()).ActiveStorageUnits);
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("e30=")]
    [InlineData("bnVsbA==")]
    public void MalformedOrIncompleteStateFailsClosed(string encoded) =>
        Assert.Equal("maintenance_participant_state_invalid", Assert.Throws<InvalidOperationException>(() => MaintenanceParticipantState.Decode(encoded)).Message);

    [Fact]
    public void DuplicateUnknownOrNoncanonicalStateFailsClosed()
    {
        foreach (var units in new[] { new[] { "unrelated.service" }, new[] { Delta, Delta }, new[] { Resume, Delta } })
            Assert.Throws<InvalidOperationException>(() => new MaintenanceParticipantState(true, true, units).Encode());
        Assert.Throws<InvalidOperationException>(() => new MaintenanceParticipantState(true, true, null!).Encode());
        foreach (var json in new[]
        {
            "{\"HealthTimerWasActive\":true,\"HealthTimerWasActive\":false,\"StorageTimerWasActive\":false,\"ActiveStorageUnits\":[]}",
            "{\"HealthTimerWasActive\":false,\"StorageTimerWasActive\":false,\"ActiveStorageUnits\":[],\"Extra\":true}",
            "{ \"HealthTimerWasActive\":false,\"StorageTimerWasActive\":false,\"ActiveStorageUnits\":[]}"
        })
            Assert.Throws<InvalidOperationException>(() => MaintenanceParticipantState.Decode(Convert.ToBase64String(Encoding.UTF8.GetBytes(json))));
        Assert.Throws<InvalidOperationException>(() => MaintenanceParticipantState.Decode(new string('a', 2049)));
    }

    [Fact]
    public async Task CapturesWithoutChangingAnythingAndDrainsJobsStartedAfterCapture()
    {
        var systemd = new FakeSystemd();
        systemd.Jobs.Add(Delta);
        var participants = new StagingMaintenanceParticipants(systemd.Run);
        var recorded = await participants.CaptureAsync(default);
        Assert.True(recorded.HealthTimerWasActive && recorded.StorageTimerWasActive);
        Assert.Equal(new[] { Delta }, recorded.ActiveStorageUnits);
        Assert.All(systemd.Calls, call => Assert.Contains(call[0], new[] { "show", "list-units" }));
        systemd.Jobs.Add(Resume);
        systemd.Calls.Clear();
        await participants.StopAndDrainAsync(default);
        Assert.Equal(new[] { "stop", "garagebalance-healthcheck.timer", "garagebalance-storage-sync.timer" }, systemd.Calls[0]);
        Assert.Equal(new[] { "stop", "garagebalance-healthcheck.service" }, systemd.Calls[1]);
        Assert.Contains(systemd.Calls, call => call.SequenceEqual(new[] { "stop", Delta, Resume }));
        Assert.Empty(systemd.Jobs);
        Assert.All(systemd.Timers.Values, value => Assert.False(value));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RestoresOnlyRecordedTimersAndPreviouslyActiveWorkers(bool health, bool storage)
    {
        var systemd = new FakeSystemd();
        foreach (var timer in systemd.Timers.Keys) systemd.Timers[timer] = false;
        var participants = new StagingMaintenanceParticipants(systemd.Run);
        await participants.RestoreAsync(new(health, storage, [Delta]), default);
        Assert.Equal(new[] { "start", "--no-block", Delta }, systemd.Calls[0]);
        Assert.Contains(Delta, systemd.Jobs);
        Assert.Equal(health, systemd.Timers["garagebalance-healthcheck.timer"]);
        Assert.Equal(storage, systemd.Timers["garagebalance-storage-sync.timer"]);
        Assert.Equal(1 + (health ? 1 : 0) + (storage ? 1 : 0), systemd.Calls.Count(call => call[0] == "start"));
    }

    [Theory]
    [InlineData("unrelated.service loaded active running")]
    [InlineData("garagebalance-storage-tool@delta-sync.service loaded deactivating stop")]
    [InlineData("garagebalance-storage-tool@delta-sync.service not-found inactive dead")]
    [InlineData("garagebalance-storage-tool@delta-sync.service loaded")]
    [InlineData("garagebalance-storage-tool@delta-sync.service loaded active running\ngaragebalance-storage-tool@delta-sync.service loaded active running")]
    public async Task UnknownOrAmbiguousUnitsRejectCaptureBeforeStopping(string listing)
    {
        var systemd = new FakeSystemd { Listing = listing };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new StagingMaintenanceParticipants(systemd.Run).CaptureAsync(default));
        Assert.Equal("maintenance_participant_state_unknown", error.Message);
        Assert.DoesNotContain(systemd.Calls, call => call[0] == "stop");
    }

    [Fact]
    public async Task TimerAndListCommandFailuresCannotBeTreatedAsInactive()
    {
        foreach (var failed in new[] { "show", "list-units" })
        {
            var systemd = new FakeSystemd { FailingCommand = failed };
            await Assert.ThrowsAsync<InvalidOperationException>(() => new StagingMaintenanceParticipants(systemd.Run).CaptureAsync(default));
        }
        var invalidTimer = new FakeSystemd { TimerOutput = "LoadState=loaded\nActiveState=failed\nSubState=failed\nFragmentPath=/etc/systemd/system/garagebalance-healthcheck.timer\n" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new StagingMaintenanceParticipants(invalidTimer.Run).CaptureAsync(default));
        var wrongPath = new FakeSystemd { TimerOutput = "LoadState=loaded\nActiveState=active\nSubState=waiting\nFragmentPath=/wrong.timer\n" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new StagingMaintenanceParticipants(wrongPath.Run).CaptureAsync(default));
    }

    [Fact]
    public async Task StopPermissionFailureOrUndrainedWorkerCannotPass()
    {
        var denied = new FakeSystemd { FailingCommand = "stop" };
        Assert.Equal("maintenance_participant_command_failed", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new StagingMaintenanceParticipants(denied.Run).StopAndDrainAsync(default))).Message);
        var busy = new FakeSystemd { KeepJobsRunning = true };
        busy.Jobs.Add(Delta);
        Assert.Equal("maintenance_participant_not_drained", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new StagingMaintenanceParticipants(busy.Run).StopAndDrainAsync(default))).Message);
    }

    [Fact]
    public async Task RestoreFailureOrWrongFinalStateRetainsFailure()
    {
        var denied = new FakeSystemd { FailingCommand = "start" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new StagingMaintenanceParticipants(denied.Run).RestoreAsync(new(true, true, []), default));
        var inactive = new FakeSystemd { IgnoreTimerStarts = true };
        foreach (var timer in inactive.Timers.Keys) inactive.Timers[timer] = false;
        Assert.Equal("maintenance_participant_restore_failed", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new StagingMaintenanceParticipants(inactive.Run).RestoreAsync(new(true, true, []), default))).Message);
    }

    [Fact]
    public async Task CancellationPropagatesWithoutAnyCommands()
    {
        var systemd = new FakeSystemd();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var participants = new StagingMaintenanceParticipants(systemd.Run);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => participants.CaptureAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => participants.StopAndDrainAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => participants.RestoreAsync(new(true, true, []), cancellation.Token));
        Assert.Empty(systemd.Calls);
    }

    private sealed class FakeSystemd
    {
        public Dictionary<string, bool> Timers { get; } = new(StringComparer.Ordinal)
        {
            ["garagebalance-healthcheck.timer"] = true,
            ["garagebalance-storage-sync.timer"] = true
        };
        public List<string> Jobs { get; } = [];
        public List<string[]> Calls { get; } = [];
        public string? FailingCommand { get; init; }
        public string? Listing { get; init; }
        public string? TimerOutput { get; init; }
        public string ActiveTimerSubstate { get; init; } = "waiting";
        public bool KeepJobsRunning { get; init; }
        public bool IgnoreTimerStarts { get; init; }

        public Task<(int, string)> Run(string[] args, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls.Add(args);
            if (args[0] == FailingCommand) return Task.FromResult((1, ""));
            if (args[0] == "show")
            {
                var active = Timers[args[1]];
                return Task.FromResult((0, TimerOutput ?? $"LoadState=loaded\nActiveState={(active ? "active" : "inactive")}\nSubState={(active ? ActiveTimerSubstate : "dead")}\nFragmentPath=/etc/systemd/system/{args[1]}\n"));
            }
            if (args[0] == "list-units")
                return Task.FromResult((0, Listing ?? string.Join('\n', Jobs.Select(unit => unit + " loaded active running Job"))));
            foreach (var unit in args.Skip(1).Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)))
            {
                if (Timers.ContainsKey(unit))
                {
                    if (args[0] != "start" || !IgnoreTimerStarts) Timers[unit] = args[0] == "start";
                }
                else if (MaintenanceParticipantState.IsStorageUnit(unit))
                {
                    if (args[0] == "start") Jobs.Add(unit);
                    else if (!KeepJobsRunning) Jobs.Remove(unit);
                }
            }
            return Task.FromResult((0, ""));
        }
    }
}
