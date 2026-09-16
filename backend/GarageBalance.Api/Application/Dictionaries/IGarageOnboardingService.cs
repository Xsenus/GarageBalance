namespace GarageBalance.Api.Application.Dictionaries;

public interface IGarageOnboardingService
{
    Task<DictionaryResult<GarageDto>> CreateWithAnnualPaymentsAsync(
        CreateGarageWithAnnualPaymentsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}

public interface IGarageOnboardingTransactionRunner
{
    Task<DictionaryResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<DictionaryResult<T>>> action,
        CancellationToken cancellationToken);
}
