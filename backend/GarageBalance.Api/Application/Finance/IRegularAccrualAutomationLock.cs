namespace GarageBalance.Api.Application.Finance;

public interface IRegularAccrualAutomationLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(DateOnly accountingMonth, CancellationToken cancellationToken);
}
