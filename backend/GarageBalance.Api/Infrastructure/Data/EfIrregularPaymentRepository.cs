using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfIrregularPaymentRepository(GarageBalanceDbContext dbContext) : IIrregularPaymentRepository
{
    public async Task<IReadOnlyList<IrregularPayment>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.IrregularPayments.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
        if (normalizedSearch is not null)
        {
            query = query.Where(item => item.Name.ToLower().Contains(normalizedSearch));
        }

        return await query
            .OrderBy(item => item.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<IrregularPayment?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.IrregularPayments.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);

    public Task<IrregularPayment?> FindArchivedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.IrregularPayments.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken) =>
        dbContext.IrregularPayments.AsNoTracking().AnyAsync(
            item => !item.IsArchived && item.Name == name && (!ignoredId.HasValue || item.Id != ignoredId.Value),
            cancellationToken);

    public void Add(IrregularPayment payment) => dbContext.IrregularPayments.Add(payment);
}
