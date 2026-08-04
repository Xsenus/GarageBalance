using GarageBalance.Api.Domain.Common;

namespace GarageBalance.Api.Application.Common;

public static class OptimisticConcurrencyGuard
{
    public static void EnsureCurrent(Guid? expectedVersion, IOptimisticConcurrencyEntity entity)
    {
        // Internal application workflows may omit the client token. HTTP update endpoints
        // reject an omitted token before invoking the application service.
        if (!expectedVersion.HasValue)
        {
            return;
        }

        if (expectedVersion.Value == Guid.Empty || expectedVersion.Value != entity.Version)
        {
            throw new OptimisticConcurrencyException(
                "The edited aggregate has changed since it was loaded.");
        }
    }
}

public sealed class OptimisticConcurrencyException(string message) : Exception(message);
