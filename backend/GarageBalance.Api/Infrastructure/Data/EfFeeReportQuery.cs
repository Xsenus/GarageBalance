using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFeeReportQuery(GarageBalanceDbContext dbContext) : IFeeReportQuery
{
    public async Task<IReadOnlyList<FeeCampaign>> GetActiveCampaignsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.FeeCampaigns.AsNoTracking()
            .Include(campaign => campaign.IncomeType)
            .Where(campaign => !campaign.IsArchived && !campaign.IncomeType.IsArchived)
            .OrderBy(campaign => campaign.StartsOn)
            .ThenBy(campaign => campaign.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IncomeType>> GetActiveIncomeTypesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.IncomeTypes.AsNoTracking()
            .Where(incomeType => !incomeType.IsArchived)
            .OrderBy(incomeType => incomeType.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<FeeReportQueryData> GetFeeDataAsync(
        IReadOnlyList<Guid> incomeTypeIds,
        CancellationToken cancellationToken)
    {
        var accrualTotals = await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && incomeTypeIds.Contains(accrual.IncomeTypeId))
            .GroupBy(accrual => accrual.IncomeTypeId)
            .Select(group => new { IncomeTypeId = group.Key, Amount = group.Sum(accrual => accrual.Amount) })
            .ToDictionaryAsync(row => row.IncomeTypeId, row => row.Amount, cancellationToken);
        var collectedTotals = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.IncomeTypeId.HasValue &&
                incomeTypeIds.Contains(operation.IncomeTypeId.Value))
            .GroupBy(operation => operation.IncomeTypeId!.Value)
            .Select(group => new { IncomeTypeId = group.Key, Amount = group.Sum(operation => operation.Amount) })
            .ToDictionaryAsync(row => row.IncomeTypeId, row => row.Amount, cancellationToken);
        var accrualsByGarage = await dbContext.Accruals.AsNoTracking()
            .Include(accrual => accrual.Garage)
            .ThenInclude(garage => garage.Owner)
            .Where(accrual => !accrual.IsCanceled && incomeTypeIds.Contains(accrual.IncomeTypeId))
            .GroupBy(accrual => new
            {
                accrual.GarageId,
                accrual.Garage.Number,
                OwnerLastName = accrual.Garage.Owner != null ? accrual.Garage.Owner.LastName : null,
                OwnerFirstName = accrual.Garage.Owner != null ? accrual.Garage.Owner.FirstName : null,
                OwnerMiddleName = accrual.Garage.Owner != null ? accrual.Garage.Owner.MiddleName : null,
                accrual.IncomeTypeId
            })
            .Select(group => new FeeAccrualByGarageData(
                group.Key.GarageId,
                group.Key.Number,
                group.Key.OwnerLastName,
                group.Key.OwnerFirstName,
                group.Key.OwnerMiddleName,
                group.Key.IncomeTypeId,
                group.Sum(accrual => accrual.Amount)))
            .ToListAsync(cancellationToken);
        var paymentsByGarage = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId.HasValue &&
                operation.IncomeTypeId.HasValue &&
                incomeTypeIds.Contains(operation.IncomeTypeId.Value))
            .GroupBy(operation => new { GarageId = operation.GarageId!.Value, IncomeTypeId = operation.IncomeTypeId!.Value })
            .Select(group => new FeePaymentByGarageData(
                group.Key.GarageId,
                group.Key.IncomeTypeId,
                group.Sum(operation => operation.Amount),
                group.Max(operation => (DateOnly?)operation.OperationDate)))
            .ToListAsync(cancellationToken);
        var allGarageIds = accrualsByGarage.Select(row => row.GarageId)
            .Concat(paymentsByGarage.Select(row => row.GarageId))
            .Distinct()
            .ToList();
        var garages = await dbContext.Garages.AsNoTracking()
            .Include(garage => garage.Owner)
            .Where(garage => allGarageIds.Contains(garage.Id))
            .Select(garage => new FeeGarageIdentityData(
                garage.Id,
                garage.Number,
                garage.Owner != null ? garage.Owner.LastName : null,
                garage.Owner != null ? garage.Owner.FirstName : null,
                garage.Owner != null ? garage.Owner.MiddleName : null))
            .ToListAsync(cancellationToken);

        return new FeeReportQueryData(
            accrualTotals,
            collectedTotals,
            accrualsByGarage,
            paymentsByGarage,
            garages.ToDictionary(garage => garage.GarageId));
    }
}
