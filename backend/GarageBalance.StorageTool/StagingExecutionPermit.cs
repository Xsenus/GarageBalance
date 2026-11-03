namespace GarageBalance.StorageTool;

// No flags, environment variable or injected verifier can issue a permit.
// Its lifetime is bounded by this exact host's held gates and durable marker.
public sealed class StagingExecutionPermit
{
    private readonly StagingBarrierQualification qualification;
    private readonly LinuxMaintenanceBarrierHost host;
    private readonly Guid id;
    private readonly string fingerprint;
    private StagingExecutionPermit(StagingBarrierQualification qualification, LinuxMaintenanceBarrierHost host, Guid id, string fingerprint)
    { this.qualification = qualification; this.host = host; this.id = id; this.fingerprint = fingerprint; }

    public static async Task<StagingExecutionPermit> CreateAsync(LinuxMaintenanceBarrierHost host, Guid id, string fingerprint, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(host);
        var qualified = await StagingBarrierQualification.VerifyInstalledAsync(token);
        var permit = new StagingExecutionPermit(qualified, host, id, fingerprint);
        await permit.VerifyAsync(id, fingerprint, token);
        return permit;
    }

    public async Task VerifyAsync(Guid expectedId, string expectedFingerprint, CancellationToken token)
    {
        if (id != expectedId || fingerprint != expectedFingerprint) throw new InvalidOperationException("maintenance_permit_plan_mismatch");
        await qualification.RevalidateAsync(token);
        host.VerifyProductionExecutionAuthority(id, fingerprint);
        var offline = await new StagingOfflineProof().GetIdentityAsync(StagingOfflineProof.Service, null, token);
        if (!offline.HasHostPrivilege || !offline.ApplicationStopped) throw new InvalidOperationException("maintenance_service_not_offline");
        await new StagingMaintenanceParticipants((arguments, ct) => AccountingCleanupHostEnvironment.RunAsync("/usr/bin/systemctl", arguments, ct)).VerifyDrainedAsync(token);
    }
}
