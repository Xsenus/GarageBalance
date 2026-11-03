using System.Net;

namespace GarageBalance.StorageTool;

// Fixed staging target only; no arbitrary service name or shell is accepted.
public sealed class StagingServiceLifecycle(
    Func<string[], CancellationToken, Task<(int ExitCode, string Output)>>? systemctl = null,
    Func<CancellationToken, Task>? health = null, bool allowFailedForRecovery = false,
    Func<CancellationToken, Task>? offlineProof = null)
{
    private readonly Func<string[], CancellationToken, Task<(int ExitCode, string Output)>> run = systemctl ?? DefaultSystemctl;
    private readonly StagingMaintenanceParticipants participants = new(systemctl ?? DefaultSystemctl);
    private readonly Func<CancellationToken, Task> checkHealth = health ?? CheckHealthAsync;
    private bool? wasActive;
    private bool failedBefore;
    private MaintenanceParticipantState? participantState;
    public bool ServiceWasActive => wasActive ?? throw new InvalidOperationException("maintenance_service_state_unknown");
    public MaintenanceParticipantState ParticipantState => participantState ?? throw new InvalidOperationException("maintenance_participant_state_unknown");

    private static Task<(int ExitCode, string Output)> DefaultSystemctl(string[] args, CancellationToken token) =>
        AccountingCleanupHostEnvironment.RunAsync("/usr/bin/systemctl", args, token);

    public async Task CaptureStateAsync(CancellationToken token)
    {
        if (wasActive.HasValue) throw new InvalidOperationException("maintenance_service_state_already_captured");
        var state = await run(["show", StagingOfflineProof.Service, "--property=LoadState", "--property=ActiveState", "--property=SubState", "--property=FragmentPath", "--all"], token);
        if (state.ExitCode != 0) throw new InvalidOperationException("maintenance_service_state_unknown");
        var lines = state.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();
        if (lines.Length != 4 || lines.Distinct(StringComparer.Ordinal).Count() != 4 ||
            !lines.Contains("LoadState=loaded", StringComparer.Ordinal) ||
            !lines.Contains("FragmentPath=/etc/systemd/system/garagebalance-staging.service", StringComparer.Ordinal))
            throw new InvalidOperationException("maintenance_service_state_unknown");
        var active = lines.Contains("ActiveState=active", StringComparer.Ordinal) && lines.Contains("SubState=running", StringComparer.Ordinal);
        var inactive = lines.Contains("ActiveState=inactive", StringComparer.Ordinal) && lines.Contains("SubState=dead", StringComparer.Ordinal);
        failedBefore = allowFailedForRecovery && lines.Contains("ActiveState=failed", StringComparer.Ordinal) && lines.Contains("SubState=failed", StringComparer.Ordinal);
        if (!active && !inactive && !failedBefore) throw new InvalidOperationException("maintenance_service_state_unknown");
        participantState = await participants.CaptureAsync(token);
        wasActive = active;
    }

    public async Task StopAndDrainAsync(CancellationToken token)
    {
        if (!wasActive.HasValue) throw new InvalidOperationException("maintenance_service_state_unknown");
        await participants.StopAndDrainAsync(token);
        if (failedBefore && (await run(["reset-failed", StagingOfflineProof.Service], token)).ExitCode != 0)
            throw new InvalidOperationException("maintenance_service_reset_failed");
        if (wasActive.Value && (await run(["stop", StagingOfflineProof.Service], token)).ExitCode != 0)
            throw new InvalidOperationException("maintenance_service_stop_failed");
    }

    public async Task VerifyOfflineAsync(CancellationToken token)
    {
        if (offlineProof is not null) { await offlineProof(token); return; }
        var proof = await new StagingOfflineProof().GetIdentityAsync(StagingOfflineProof.Service, null, token);
        if (!proof.HasHostPrivilege || !proof.ApplicationStopped) throw new InvalidOperationException("maintenance_service_not_offline");
    }

    public async Task RestoreAsync(CancellationToken token)
    {
        if (!wasActive.HasValue) throw new InvalidOperationException("maintenance_service_state_unknown");
        if (wasActive.Value)
        {
            if ((await run(["start", StagingOfflineProof.Service], token)).ExitCode != 0)
                throw new InvalidOperationException("maintenance_service_start_failed");
            await checkHealth(token);
        }
    }

    public Task RestoreParticipantsAsync(CancellationToken token) => participants.RestoreAsync(ParticipantState, token);

    public Task RestoreRecordedAsync(bool recordedWasActive, CancellationToken token, MaintenanceParticipantState? recordedParticipants = null)
    {
        if (!wasActive.HasValue) throw new InvalidOperationException("maintenance_service_state_unknown");
        wasActive = recordedWasActive;
        if (recordedParticipants is not null)
        {
            recordedParticipants.Validate();
            participantState = recordedParticipants;
        }
        return RestoreAsync(token);
    }

    private static async Task CheckHealthAsync(CancellationToken token)
    {
        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:3101/health/ready");
            request.Headers.Host = "sgk.blagodaty.ru";
            try
            {
                using var response = await client.SendAsync(request, token);
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        throw new InvalidOperationException("maintenance_service_unhealthy");
    }
}
