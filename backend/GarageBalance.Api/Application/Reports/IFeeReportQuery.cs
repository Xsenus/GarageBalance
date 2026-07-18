using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Reports;

public interface IFeeReportQuery
{
    Task<IReadOnlyList<FeeCampaign>> GetActiveCampaignsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<IncomeType>> GetActiveIncomeTypesAsync(CancellationToken cancellationToken);
    Task<FeeReportQueryData> GetFeeDataAsync(IReadOnlyList<Guid> incomeTypeIds, CancellationToken cancellationToken);
    Task<FeeReportQueryData> GetFeeCampaignDataAsync(IReadOnlyList<Guid> feeCampaignIds, CancellationToken cancellationToken);
    Task<FeeReportPageQueryData> GetFeeReportPageAsync(
        IReadOnlyList<Guid> feeEntryIds,
        bool useFeeCampaigns,
        ReportSort sort,
        int offset,
        int? limit,
        CancellationToken cancellationToken);
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

public sealed record FeeReportPageQueryData(
    IReadOnlyDictionary<Guid, decimal> AccrualTotals,
    IReadOnlyDictionary<Guid, decimal> CollectedTotals,
    IReadOnlyList<FeeReportGarageQueryRow> GarageRows,
    IReadOnlyList<FeeReportGarageQueryRow> DebtorRows,
    int GarageRowCount,
    decimal DebtTotal);

public sealed record FeeReportGarageQueryRow(
    Guid GarageId,
    string GarageNumber,
    string? OwnerLastName,
    string? OwnerFirstName,
    string? OwnerMiddleName,
    Guid FeeEntryId,
    string FeeName,
    decimal Accrued,
    decimal Paid,
    DateOnly? LastPaymentDate,
    decimal Debt);
