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

    public async Task<bool> IsUsedAsync(string name, CancellationToken cancellationToken)
    {
        var incomeTypeIds = dbContext.IncomeTypes.AsNoTracking()
            .Where(incomeType => incomeType.Name == name)
            .Select(incomeType => incomeType.Id);

        return await dbContext.Accruals.AsNoTracking()
                .AnyAsync(accrual => !accrual.IsCanceled && incomeTypeIds.Contains(accrual.IncomeTypeId), cancellationToken)
            || await dbContext.FinancialOperations.AsNoTracking()
                .AnyAsync(operation =>
                    !operation.IsCanceled &&
                    operation.IncomeTypeId.HasValue &&
                    incomeTypeIds.Contains(operation.IncomeTypeId.Value),
                    cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetUsedNamesAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var incomeTypes = dbContext.IncomeTypes.AsNoTracking()
            .Where(incomeType => names.Contains(incomeType.Name))
            .Select(incomeType => new { incomeType.Id, incomeType.Name });

        var usedByAccruals = from incomeType in incomeTypes
                             join accrual in dbContext.Accruals.AsNoTracking() on incomeType.Id equals accrual.IncomeTypeId
                             where !accrual.IsCanceled
                             select incomeType.Name;
        var usedByOperations = from incomeType in incomeTypes
                               join operation in dbContext.FinancialOperations.AsNoTracking() on incomeType.Id equals operation.IncomeTypeId
                               where !operation.IsCanceled
                               select incomeType.Name;

        return (await usedByAccruals
                .Concat(usedByOperations)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
    }

    public void Add(IrregularPayment payment) => dbContext.IrregularPayments.Add(payment);
}
