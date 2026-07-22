namespace GarageBalance.Api.Application.Finance;

public interface IFinancialReportPeriodQuery
{
    Task<FinancialReportPeriodData?> GetAsync(
        Guid? garageId,
        Guid? supplierId,
        Guid? staffMemberId,
        CancellationToken cancellationToken);
}

public sealed record FinancialReportPeriodData(
    DateOnly? AccrualMonthFrom,
    DateOnly? OperationMonthFrom,
    DateOnly? AccrualMonthTo,
    DateOnly? OperationMonthTo);
