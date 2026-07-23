using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfStaffSalaryAdjustmentRepository(GarageBalanceDbContext dbContext)
    : IStaffSalaryAdjustmentRepository
{
    public async Task<StaffSalaryAdjustmentTotals> GetTotalsAsync(
        Guid staffMemberId,
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        var totals = await dbContext.StaffSalaryAdjustments
            .AsNoTracking()
            .Where(adjustment =>
                adjustment.StaffMemberId == staffMemberId &&
                adjustment.AccountingMonth == accountingMonth)
            .GroupBy(_ => 1)
            .Select(group => new StaffSalaryAdjustmentTotals(
                group.Sum(adjustment =>
                    adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Bonus
                        ? adjustment.Amount
                        : 0m),
                group.Sum(adjustment =>
                    adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Penalty
                        ? adjustment.Amount
                        : 0m)))
            .SingleOrDefaultAsync(cancellationToken);

        return totals ?? new StaffSalaryAdjustmentTotals(0m, 0m);
    }

    public void Add(StaffSalaryAdjustment adjustment)
    {
        dbContext.StaffSalaryAdjustments.Add(adjustment);
    }
}
