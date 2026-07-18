using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFeeReportQuery(GarageBalanceDbContext dbContext) : IFeeReportQuery
{
    private const int AccrualCategory = 1;
    private const int PaymentCategory = 2;

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
        var accrualQuery = dbContext.Accruals.AsNoTracking()
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
            .Select(group => new
            {
                Category = AccrualCategory,
                GarageId = (Guid?)group.Key.GarageId,
                GarageNumber = (string?)group.Key.Number,
                group.Key.OwnerLastName,
                group.Key.OwnerFirstName,
                group.Key.OwnerMiddleName,
                group.Key.IncomeTypeId,
                Accrued = group.Sum(accrual => accrual.Amount),
                Paid = 0m,
                LastPaymentDate = (DateOnly?)null
            });
        var paymentQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.IncomeTypeId.HasValue &&
                incomeTypeIds.Contains(operation.IncomeTypeId.Value))
            .GroupBy(operation => new
            {
                operation.GarageId,
                GarageNumber = operation.Garage == null ? null : operation.Garage.Number,
                OwnerLastName = operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.LastName,
                OwnerFirstName = operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.FirstName,
                OwnerMiddleName = operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.MiddleName,
                IncomeTypeId = operation.IncomeTypeId!.Value
            })
            .Select(group => new
            {
                Category = PaymentCategory,
                group.Key.GarageId,
                group.Key.GarageNumber,
                group.Key.OwnerLastName,
                group.Key.OwnerFirstName,
                group.Key.OwnerMiddleName,
                group.Key.IncomeTypeId,
                Accrued = 0m,
                Paid = group.Sum(operation => operation.Amount),
                LastPaymentDate = group.Max(operation => (DateOnly?)operation.OperationDate)
            });
        var rows = await accrualQuery
            .Concat(paymentQuery)
            .ToListAsync(cancellationToken);

        var accrualsByGarage = rows
            .Where(row => row.Category == AccrualCategory)
            .Select(row => new FeeAccrualByGarageData(
                row.GarageId!.Value,
                row.GarageNumber!,
                row.OwnerLastName,
                row.OwnerFirstName,
                row.OwnerMiddleName,
                row.IncomeTypeId,
                row.Accrued))
            .ToList();
        var paymentsByGarage = rows
            .Where(row => row.Category == PaymentCategory && row.GarageId.HasValue)
            .Select(row => new FeePaymentByGarageData(
                row.GarageId!.Value,
                row.IncomeTypeId,
                row.Paid,
                row.LastPaymentDate))
            .ToList();
        var accrualTotals = accrualsByGarage
            .GroupBy(row => row.IncomeTypeId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Accrued));
        var collectedTotals = rows
            .Where(row => row.Category == PaymentCategory)
            .GroupBy(row => row.IncomeTypeId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Paid));
        var garagesById = rows
            .Where(row => row.GarageId.HasValue && row.GarageNumber != null)
            .GroupBy(row => row.GarageId!.Value)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var row = group.First();
                    return new FeeGarageIdentityData(
                        row.GarageId!.Value,
                        row.GarageNumber!,
                        row.OwnerLastName,
                        row.OwnerFirstName,
                        row.OwnerMiddleName);
                });

        return new FeeReportQueryData(
            accrualTotals,
            collectedTotals,
            accrualsByGarage,
            paymentsByGarage,
            garagesById);
    }

    public async Task<FeeReportQueryData> GetFeeCampaignDataAsync(
        IReadOnlyList<Guid> feeCampaignIds,
        CancellationToken cancellationToken)
    {
        var accrualsByGarage = await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.FeeCampaignId.HasValue &&
                feeCampaignIds.Contains(accrual.FeeCampaignId.Value))
            .GroupBy(accrual => new
            {
                accrual.GarageId,
                accrual.Garage.Number,
                OwnerLastName = accrual.Garage.Owner != null ? accrual.Garage.Owner.LastName : null,
                OwnerFirstName = accrual.Garage.Owner != null ? accrual.Garage.Owner.FirstName : null,
                OwnerMiddleName = accrual.Garage.Owner != null ? accrual.Garage.Owner.MiddleName : null,
                FeeCampaignId = accrual.FeeCampaignId!.Value
            })
            .Select(group => new FeeAccrualByGarageData(
                group.Key.GarageId,
                group.Key.Number,
                group.Key.OwnerLastName,
                group.Key.OwnerFirstName,
                group.Key.OwnerMiddleName,
                group.Key.FeeCampaignId,
                group.Sum(accrual => accrual.Amount)))
            .ToListAsync(cancellationToken);

        var paymentsByGarage = await dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(allocation =>
                allocation.IsActive &&
                !allocation.Accrual.IsCanceled &&
                allocation.Accrual.FeeCampaignId.HasValue &&
                feeCampaignIds.Contains(allocation.Accrual.FeeCampaignId.Value) &&
                !allocation.FinancialOperation.IsCanceled)
            .GroupBy(allocation => new
            {
                allocation.Accrual.GarageId,
                FeeCampaignId = allocation.Accrual.FeeCampaignId!.Value
            })
            .Select(group => new FeePaymentByGarageData(
                group.Key.GarageId,
                group.Key.FeeCampaignId,
                group.Sum(allocation => allocation.Amount),
                group.Max(allocation => (DateOnly?)allocation.FinancialOperation.OperationDate)))
            .ToListAsync(cancellationToken);

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

        return new FeeReportQueryData(
            accrualsByGarage.GroupBy(row => row.IncomeTypeId).ToDictionary(group => group.Key, group => group.Sum(row => row.Accrued)),
            paymentsByGarage.GroupBy(row => row.IncomeTypeId).ToDictionary(group => group.Key, group => group.Sum(row => row.Paid)),
            accrualsByGarage,
            paymentsByGarage,
            garagesById);
    }
}
