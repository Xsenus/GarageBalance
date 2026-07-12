using GarageBalance.Api.Domain.Workflows;

namespace GarageBalance.Api.Application.Workflows;

public interface IFormStateRepository
{
    Task<FormState?> GetAsync(string scope, CancellationToken cancellationToken);
    Task<FormState?> FindForUpdateAsync(string scope, CancellationToken cancellationToken);
    void Add(FormState state);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
