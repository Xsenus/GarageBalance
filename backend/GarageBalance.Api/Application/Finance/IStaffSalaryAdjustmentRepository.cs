using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public interface IStaffSalaryAdjustmentRepository
{
    Task<StaffSalaryAdjustmentTotals> GetTotalsAsync(
        Guid staffMemberId,
        DateOnly accountingMonth,
        CancellationToken cancellationToken);

    void Add(StaffSalaryAdjustment adjustment);
}

public sealed record StaffSalaryAdjustmentTotals(decimal BonusAmount, decimal PenaltyAmount);
