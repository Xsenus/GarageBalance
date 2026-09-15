namespace GarageBalance.Api.Application.Dictionaries;

public interface IGarageCreationService
{
    Task<DictionaryResult<GarageDto>> CreateWithAnnualPaymentsAsync(
        CreateGarageWithAnnualPaymentsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
