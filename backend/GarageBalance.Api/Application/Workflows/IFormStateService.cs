namespace GarageBalance.Api.Application.Workflows;

public interface IFormStateService
{
    Task<FormStateDto?> GetStateAsync(string scope, CancellationToken cancellationToken);

    Task<FormStateResult<FormStateDto>> UpsertStateAsync(
        string scope,
        UpsertFormStateRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
