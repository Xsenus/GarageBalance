using GarageBalance.Api.Application.Common;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfApplicationUnitOfWork(GarageBalanceDbContext dbContext) : IApplicationUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
