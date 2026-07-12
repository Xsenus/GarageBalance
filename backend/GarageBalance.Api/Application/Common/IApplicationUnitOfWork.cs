namespace GarageBalance.Api.Application.Common;

public interface IApplicationUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
