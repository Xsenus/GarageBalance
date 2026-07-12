using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Reports;

public interface IFeeReportQuery
{
    Task<IReadOnlyList<FeeCampaign>> GetActiveCampaignsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<IncomeType>> GetActiveIncomeTypesAsync(CancellationToken cancellationToken);
    Task<FeeReportQueryData> GetFeeDataAsync(IReadOnlyList<Guid> incomeTypeIds, CancellationToken cancellationToken);
}

public sealed record FeeReportQueryData(
    IReadOnlyDictionary<Guid, decimal> AccrualTotals,
    IReadOnlyDictionary<Guid, decimal> CollectedTotals,
    IReadOnlyList<FeeAccrualByGarageData> AccrualsByGarage,
    IReadOnlyList<FeePaymentByGarageData> PaymentsByGarage,
    IReadOnlyDictionary<Guid, FeeGarageIdentityData> GaragesById);

public sealed record FeeAccrualByGarageData(
    Guid GarageId,
    string GarageNumber,
    string? OwnerLastName,
    string? OwnerFirstName,
    string? OwnerMiddleName,
    Guid IncomeTypeId,
    decimal Accrued);

public sealed record FeePaymentByGarageData(
    Guid GarageId,
    Guid IncomeTypeId,
    decimal Paid,
    DateOnly? LastPaymentDate);

public sealed record FeeGarageIdentityData(
    Guid GarageId,
    string GarageNumber,
    string? OwnerLastName,
    string? OwnerFirstName,
    string? OwnerMiddleName);
