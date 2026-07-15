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
        var paymentGroups = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.IncomeTypeId.HasValue &&
                incomeTypeIds.Contains(operation.IncomeTypeId.Value))
            .GroupBy(operation => new { operation.GarageId, IncomeTypeId = operation.IncomeTypeId!.Value })
            .Select(group => new
            {
                group.Key.GarageId,
                group.Key.IncomeTypeId,
                Paid = group.Sum(operation => operation.Amount),
                LastPaymentDate = group.Max(operation => (DateOnly?)operation.OperationDate)
            })
            .ToListAsync(cancellationToken);
        var paymentsByGarage = paymentGroups
            .Where(row => row.GarageId.HasValue)
            .Select(row => new FeePaymentByGarageData(
                row.GarageId!.Value,
                row.IncomeTypeId,
                row.Paid,
                row.LastPaymentDate))
            .ToList();
        var accrualTotals = accrualsByGarage
            .GroupBy(row => row.IncomeTypeId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Accrued));
        var collectedTotals = paymentGroups
            .GroupBy(row => row.IncomeTypeId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Paid));
        var garagesById = accrualsByGarage
            .GroupBy(row => row.GarageId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var row = group.First();
                    return new FeeGarageIdentityData(
                        row.GarageId,
                        row.GarageNumber,
                        row.OwnerLastName,
                        row.OwnerFirstName,
                        row.OwnerMiddleName);
                });
        var missingGarageIds = paymentsByGarage
            .Select(row => row.GarageId)
            .Where(garageId => !garagesById.ContainsKey(garageId))
            .Distinct()
            .ToList();
        if (missingGarageIds.Count > 0)
        {
            var paymentOnlyGarages = await dbContext.Garages.AsNoTracking()
                .Where(garage => missingGarageIds.Contains(garage.Id))
                .Select(garage => new FeeGarageIdentityData(
                    garage.Id,
                    garage.Number,
                    garage.Owner != null ? garage.Owner.LastName : null,
                    garage.Owner != null ? garage.Owner.FirstName : null,
                    garage.Owner != null ? garage.Owner.MiddleName : null))
                .ToListAsync(cancellationToken);
            foreach (var garage in paymentOnlyGarages)
            {
                garagesById[garage.GarageId] = garage;
            }
        }

        return new FeeReportQueryData(
            accrualTotals,
            collectedTotals,
            accrualsByGarage,
            paymentsByGarage,
            garagesById);
    }
}
