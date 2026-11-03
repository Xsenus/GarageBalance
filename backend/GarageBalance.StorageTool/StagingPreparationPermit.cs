namespace GarageBalance.StorageTool;

// A preparation permit can restore immutable backup files; its type cannot be
// supplied to financial execution. No financial SQL is eligible in preparing.
public sealed class StagingPreparationPermit
{
    private readonly StagingBarrierQualification qualification;
    private readonly LinuxMaintenanceBarrierHost host;
    private readonly Guid id;
    private StagingPreparationPermit(StagingBarrierQualification qualification, LinuxMaintenanceBarrierHost host, Guid id)
    { this.qualification = qualification; this.host = host; this.id = id; }

    public static async Task<StagingPreparationPermit> CreateAsync(LinuxMaintenanceBarrierHost host, Guid id, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(host);
        var permit = new StagingPreparationPermit(await StagingBarrierQualification.VerifyInstalledAsync(token), host, id);
        await permit.VerifyAsync(token);
        return permit;
    }

    public async Task VerifyAsync(CancellationToken token)
    {
        await qualification.RevalidateAsync(token);
        host.VerifyProductionPreparationAuthority(id);
        var offline = await new StagingOfflineProof().GetIdentityAsync(StagingOfflineProof.Service, null, token);
        if (!offline.HasHostPrivilege || !offline.ApplicationStopped) throw new InvalidOperationException("maintenance_service_not_offline");
        await new StagingMaintenanceParticipants((arguments, ct) => AccountingCleanupHostEnvironment.RunAsync("/usr/bin/systemctl", arguments, ct)).VerifyDrainedAsync(token);
    }
}
