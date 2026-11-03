namespace GarageBalance.StorageTool;

// All participants use the shared activity gate. Stop timers before draining
// one-shot jobs; restore only the state recorded before the durable marker.
public sealed class StagingMaintenanceParticipants(
    Func<string[], CancellationToken, Task<(int ExitCode, string Output)>> run)
{
    private const string HealthTimer = "garagebalance-healthcheck.timer";
    private const string StorageTimer = "garagebalance-storage-sync.timer";
    private const string HealthService = "garagebalance-healthcheck.service";

    public async Task<MaintenanceParticipantState> CaptureAsync(CancellationToken token)
    {
        var health = await IsTimerActiveAsync(HealthTimer, token);
        var storage = await IsTimerActiveAsync(StorageTimer, token);
        return new(health, storage, await ActiveStorageUnitsAsync(token));
    }

    public async Task StopAndDrainAsync(CancellationToken token)
    {
        await RequireAsync(["stop", HealthTimer, StorageTimer], token);
        await RequireAsync(["stop", HealthService], token);
        // Enumerate after the marker/timer stop, including a job that started
        // between state capture and publication of the marker.
        var active = await ActiveStorageUnitsAsync(token);
        if (active.Length != 0) await RequireAsync(["stop", .. active], token);
        await VerifyDrainedAsync(token);
    }

    public async Task VerifyDrainedAsync(CancellationToken token)
    {
        if (await IsTimerActiveAsync(HealthTimer, token) || await IsTimerActiveAsync(StorageTimer, token) ||
            (await ActiveStorageUnitsAsync(token)).Length != 0)
            throw new InvalidOperationException("maintenance_participant_not_drained");
    }

    public async Task RestoreAsync(MaintenanceParticipantState state, CancellationToken token)
    {
        state.Validate();
        // --no-block avoids waiting for a potentially hours-long one-shot sync.
        // Its completion is a separate storage verification, not app readiness.
        foreach (var unit in state.ActiveStorageUnits) await RequireAsync(["start", "--no-block", unit], token);
        if (state.StorageTimerWasActive) await RequireAsync(["start", StorageTimer], token);
        if (state.HealthTimerWasActive) await RequireAsync(["start", HealthTimer], token);
        if (await IsTimerActiveAsync(HealthTimer, token) != state.HealthTimerWasActive ||
            await IsTimerActiveAsync(StorageTimer, token) != state.StorageTimerWasActive)
            throw new InvalidOperationException("maintenance_participant_restore_failed");
    }

    private async Task<bool> IsTimerActiveAsync(string timer, CancellationToken token)
    {
        var result = await run(["show", timer, "--property=LoadState", "--property=ActiveState", "--property=SubState", "--property=FragmentPath"], token);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();
        if (result.ExitCode != 0 || lines.Length != 4 || lines.Distinct(StringComparer.Ordinal).Count() != 4 ||
            !lines.Contains("LoadState=loaded", StringComparer.Ordinal) ||
            !lines.Contains($"FragmentPath=/etc/systemd/system/{timer}", StringComparer.Ordinal))
            throw new InvalidOperationException("maintenance_participant_state_unknown");
        if (lines.Contains("ActiveState=active", StringComparer.Ordinal) &&
            (lines.Contains("SubState=waiting", StringComparer.Ordinal) || lines.Contains("SubState=elapsed", StringComparer.Ordinal) ||
             lines.Contains("SubState=running", StringComparer.Ordinal))) return true;
        if (lines.Contains("ActiveState=inactive", StringComparer.Ordinal) && lines.Contains("SubState=dead", StringComparer.Ordinal)) return false;
        throw new InvalidOperationException("maintenance_participant_state_unknown");
    }

    private async Task<string[]> ActiveStorageUnitsAsync(CancellationToken token)
    {
        var result = await run(["list-units", "--all", "--type=service", "--plain", "--no-legend", "--no-pager", "garagebalance-storage-tool@*.service"], token);
        if (result.ExitCode != 0) throw new InvalidOperationException("maintenance_participant_state_unknown");
        var active = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || !MaintenanceParticipantState.IsStorageUnit(fields[0]) || !seen.Add(fields[0]) || fields[1] != "loaded")
                throw new InvalidOperationException("maintenance_participant_state_unknown");
            if (fields[2] == "active" && fields[3] is "running" or "exited" || fields[2] == "activating" && fields[3] == "start") active.Add(fields[0]);
            else if (!(fields[2] == "inactive" && fields[3] == "dead" || fields[2] == "failed" && fields[3] == "failed"))
                throw new InvalidOperationException("maintenance_participant_state_unknown");
        }
        return active.Order(StringComparer.Ordinal).ToArray();
    }

    private async Task RequireAsync(string[] args, CancellationToken token)
    {
        if ((await run(args, token)).ExitCode != 0) throw new InvalidOperationException("maintenance_participant_command_failed");
    }
}
