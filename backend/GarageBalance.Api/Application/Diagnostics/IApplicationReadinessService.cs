namespace GarageBalance.Api.Application.Diagnostics;

public interface IApplicationReadinessService
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
