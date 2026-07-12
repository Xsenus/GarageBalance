using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfTariffRepository(GarageBalanceDbContext dbContext) : ITariffRepository
{
    public async Task<IReadOnlyList<Tariff>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken) =>
        await ApplyFilters(normalizedSearch, includeArchived)
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenBy(item => item.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<TariffPageData> GetPageAsync(
        string? normalizedSearch,
        bool includeArchived,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(normalizedSearch, includeArchived);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenBy(item => item.Name)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new TariffPageData(items, totalCount);
    }

    public Task<Tariff?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Tariffs.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);

    public Task<Tariff?> FindArchivedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Tariffs.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, DateOnly effectiveFrom, CancellationToken cancellationToken) =>
        dbContext.Tariffs.AsNoTracking().AnyAsync(
            item =>
                !item.IsArchived &&
                item.Name == name &&
                item.EffectiveFrom == effectiveFrom &&
                (!ignoredId.HasValue || item.Id != ignoredId.Value),
            cancellationToken);

    public Task<DateOnly?> GetEarliestRegularAccrualMonthAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.Source == AccrualSources.Regular && accrual.TariffId == id)
            .MinAsync(accrual => (DateOnly?)accrual.AccountingMonth, cancellationToken);

    public void Add(Tariff tariff) => dbContext.Tariffs.Add(tariff);

    private IQueryable<Tariff> ApplyFilters(string? normalizedSearch, bool includeArchived)
    {
        var query = dbContext.Tariffs.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
        if (normalizedSearch is not null)
        {
            query = query.Where(item =>
                item.Name.ToLower().Contains(normalizedSearch) ||
                item.CalculationBase.ToLower().Contains(normalizedSearch));
        }

        return query;
    }
}
