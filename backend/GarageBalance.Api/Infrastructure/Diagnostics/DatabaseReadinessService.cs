using GarageBalance.Api.Application.Diagnostics;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Diagnostics;

public sealed class DatabaseReadinessService(
    GarageBalanceDbContext dbContext,
    ILogger<DatabaseReadinessService> logger) : IApplicationReadinessService
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "PostgreSQL readiness check failed.");
            return false;
        }
    }
}
