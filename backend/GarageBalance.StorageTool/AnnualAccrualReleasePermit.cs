namespace GarageBalance.StorageTool;

// A distinct capability for annual release, never interchangeable with cleanup.
// No public constructor, injected proof, flag, or environment variable issues it.
public sealed class AnnualAccrualReleasePermit
{
    private readonly StagingExecutionPermit authority;
    private readonly Guid planId;
    private readonly string fingerprint;
    private readonly int year;

    private AnnualAccrualReleasePermit(StagingExecutionPermit authority, AnnualAccrualHoldReleasePlan plan)
    {
        this.authority = authority;
        planId = plan.PlanId;
        fingerprint = plan.Fingerprint;
        year = plan.Year;
    }

    public static async Task<AnnualAccrualReleasePermit> CreateAsync(LinuxMaintenanceBarrierHost host,
        AnnualAccrualHoldReleasePlan plan, CancellationToken token)
    {
        AnnualAccrualHoldReleaseEngine.ValidatePlan(plan);
        var authority = await StagingExecutionPermit.CreateAsync(host, plan.PlanId, plan.Fingerprint, token);
        return new(authority, plan);
    }

    public async Task VerifyAsync(AnnualAccrualHoldReleasePlan plan, CancellationToken token)
    {
        AnnualAccrualHoldReleaseEngine.ValidatePlan(plan);
        if (planId != plan.PlanId || fingerprint != plan.Fingerprint || year != plan.Year)
            throw new InvalidOperationException("maintenance_release_permit_plan_mismatch");
        await authority.VerifyAsync(planId, fingerprint, token);
    }
}
