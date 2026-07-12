using GarageBalance.Api.Application.Workflows;
using GarageBalance.Api.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFormStateRepository(GarageBalanceDbContext dbContext) : IFormStateRepository
{
    public Task<FormState?> GetAsync(string scope, CancellationToken cancellationToken)
    {
        return dbContext.FormStates
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Scope == scope, cancellationToken);
    }

    public Task<FormState?> FindForUpdateAsync(string scope, CancellationToken cancellationToken)
    {
        return dbContext.FormStates
            .SingleOrDefaultAsync(item => item.Scope == scope, cancellationToken);
    }

    public void Add(FormState state)
    {
        dbContext.FormStates.Add(state);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
