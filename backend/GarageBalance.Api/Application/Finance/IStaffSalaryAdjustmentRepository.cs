using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public interface IStaffSalaryAdjustmentRepository
{
    Task<IAsyncDisposable> AcquireMonthlyLockAsync(
        Guid staffMemberId,
        DateOnly accountingMonth,
        CancellationToken cancellationToken);

    Task<StaffSalaryAdjustmentTotals> GetTotalsAsync(
        Guid staffMemberId,
        DateOnly accountingMonth,
        Guid? excludedAdjustmentId,
        CancellationToken cancellationToken);

    Task<StaffSalaryAdjustment?> FindForUpdateAsync(Guid adjustmentId, CancellationToken cancellationToken);

    Task ReloadAsync(StaffSalaryAdjustment adjustment, CancellationToken cancellationToken);

    void Add(StaffSalaryAdjustment adjustment);
}

public sealed record StaffSalaryAdjustmentTotals(decimal BonusAmount, decimal PenaltyAmount);
