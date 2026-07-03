namespace GarageBalance.Api.Application.Funds;

public interface IFundService
{
    Task<IReadOnlyList<FundDto>> GetFundsAsync(CancellationToken cancellationToken);

    Task<FundResult<FundOperationDto>> CreateOperationAsync(Guid fundId, CreateFundOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken);
}
