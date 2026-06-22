namespace GarageBalance.Api.Application.Finance;

public interface IFinanceService
{
    Task<IReadOnlyList<FinancialOperationDto>> GetOperationsAsync(FinancialOperationListRequest request, CancellationToken cancellationToken);

    Task<FinanceSummaryDto> GetSummaryAsync(FinancialOperationListRequest request, CancellationToken cancellationToken);

    Task<FinanceResult<FinancialOperationDto>> CreateIncomeAsync(CreateIncomeOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<FinancialOperationDto>> CreateExpenseAsync(CreateExpenseOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken);
}
