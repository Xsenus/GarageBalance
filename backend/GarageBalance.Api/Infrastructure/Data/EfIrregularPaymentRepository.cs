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

    public Task<bool> IsUsedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Accruals.AsNoTracking()
            .AnyAsync(accrual => !accrual.IsCanceled && accrual.IrregularPaymentId == id, cancellationToken);

    public async Task<IReadOnlySet<string>> GetUsedNamesAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var usedByAccruals = from payment in dbContext.IrregularPayments.AsNoTracking()
                             join accrual in dbContext.Accruals.AsNoTracking() on payment.Id equals accrual.IrregularPaymentId
                             where names.Contains(payment.Name) && !accrual.IsCanceled
                             select payment.Name;

        return (await usedByAccruals
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
    }

    public void Add(IrregularPayment payment) => dbContext.IrregularPayments.Add(payment);
}
