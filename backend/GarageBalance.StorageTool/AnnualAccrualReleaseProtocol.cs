namespace GarageBalance.StorageTool;

// Orchestration only: callbacks cannot manufacture the production release permit.
// A fresh marker is mandatory. In particular a prepared cleanup cannot become a release.
public static class AnnualAccrualReleaseProtocol
{
    public static async Task ExecuteAsync(IMaintenanceBarrierHost host,
        Func<string, CancellationToken, Task> transition,
        Func<CancellationToken, Task> release, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(release);
        await using var deploy = await host.AcquireDeployGateAsync(token);
        await transition("verify-fresh", token);
        await host.CaptureServiceStateAsync(token);
        await host.CreateDurableMarkerAsync(token);
        await transition("preparing", token);
        await host.StopAndDrainAsync(token);
        await using (var activity = await host.AcquireActivityGateAsync(token))
        {
            await host.VerifyOfflineAsync(token);
            await transition("prepared", token);
            await transition("executing", token);
            await release(token);
            await host.VerifyCommittedAsync(token);
            await host.CreateRecoveryReceiptAsync(token);
            await host.RemoveDurableMarkerAsync(token);
        }
        await host.RestoreServiceAsync(token);
        await host.PermitParticipantsAsync(token);
        await host.RestoreParticipantsAsync(token);
        await host.CompleteRecoveryAsync(token);
    }
}
