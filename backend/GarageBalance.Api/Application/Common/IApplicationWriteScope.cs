namespace GarageBalance.Api.Application.Common;

public interface IApplicationWriteScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync();
}
