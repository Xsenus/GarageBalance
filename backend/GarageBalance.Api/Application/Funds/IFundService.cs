namespace GarageBalance.Api.Application.Funds;

public interface IFundService
{
    Task<IReadOnlyList<FundDto>> GetFundsAsync(CancellationToken cancellationToken);

    Task<FundResult<FundOperationDto>> CreateOperationAsync(Guid fundId, CreateFundOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FundResult<FundOperationDto>> CancelOperationAsync(Guid operationId, CancelFundOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FundResult<FundOperationDto>> RestoreOperationAsync(Guid operationId, Guid? actorUserId, CancellationToken cancellationToken);
}
