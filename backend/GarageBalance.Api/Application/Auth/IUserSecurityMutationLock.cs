namespace GarageBalance.Api.Application.Auth;

public interface IUserSecurityMutationLock
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken);
}
