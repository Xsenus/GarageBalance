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
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresPageAsync(query, offset, limit, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenBy(item => item.Name)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new TariffPageData(items, totalCount);
    }

    private async Task<TariffPageData> GetPostgresPageAsync(
        IQueryable<Tariff> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = query
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Skip(offset)
            .Take(limit)
            .Select(item => new TariffPageRow
            {
                Category = PageCategory,
                Id = item.Id,
                Name = item.Name,
                CalculationBase = item.CalculationBase,
                Rate = item.Rate,
                ElectricityFirstThreshold = item.ElectricityFirstThreshold,
                ElectricitySecondThreshold = item.ElectricitySecondThreshold,
                ElectricityFirstTierName = item.ElectricityFirstTierName,
                ElectricitySecondTierName = item.ElectricitySecondTierName,
                ElectricityThirdTierName = item.ElectricityThirdTierName,
                ElectricityFirstRate = item.ElectricityFirstRate,
                ElectricitySecondRate = item.ElectricitySecondRate,
                ElectricityThirdRate = item.ElectricityThirdRate,
                ElectricityTiersJson = item.ElectricityTiersJson,
                EffectiveFrom = item.EffectiveFrom,
                Comment = item.Comment,
                IsArchived = item.IsArchived,
                Version = item.Version,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new TariffPageRow
            {
                Category = TotalsCategory,
                Id = null,
                Name = null,
                CalculationBase = null,
                Rate = null,
                ElectricityFirstThreshold = null,
                ElectricitySecondThreshold = null,
                ElectricityFirstTierName = null,
                ElectricitySecondTierName = null,
                ElectricityThirdTierName = null,
                ElectricityFirstRate = null,
                ElectricitySecondRate = null,
                ElectricityThirdRate = null,
                ElectricityTiersJson = null,
                EffectiveFrom = null,
                Comment = null,
                IsArchived = null,
                Version = null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenByDescending(row => row.EffectiveFrom)
            .ThenBy(row => row.Name)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new Tariff
            {
                Id = row.Id!.Value,
                Name = row.Name!,
                CalculationBase = row.CalculationBase!,
                Rate = row.Rate!.Value,
                ElectricityFirstThreshold = row.ElectricityFirstThreshold,
                ElectricitySecondThreshold = row.ElectricitySecondThreshold,
                ElectricityFirstTierName = row.ElectricityFirstTierName,
                ElectricitySecondTierName = row.ElectricitySecondTierName,
                ElectricityThirdTierName = row.ElectricityThirdTierName,
                ElectricityFirstRate = row.ElectricityFirstRate,
                ElectricitySecondRate = row.ElectricitySecondRate,
                ElectricityThirdRate = row.ElectricityThirdRate,
                ElectricityTiersJson = row.ElectricityTiersJson,
                EffectiveFrom = row.EffectiveFrom!.Value,
                Comment = row.Comment,
                IsArchived = row.IsArchived!.Value,
                Version = row.Version!.Value
            })
            .ToList();
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

    public Task<bool> HasActiveServiceAssignmentsAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceSettings.AsNoTracking()
            .AnyAsync(setting => setting.TariffId == id && !setting.IsArchived, cancellationToken);

    public void Add(Tariff tariff) => dbContext.Tariffs.Add(tariff);

    private IQueryable<Tariff> ApplyFilters(string? normalizedSearch, bool includeArchived)
    {
        var query = dbContext.Tariffs.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
        if (normalizedSearch is not null)
        {
            if (dbContext.Database.IsNpgsql())
            {
                var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
                query = query.Where(item =>
                    EF.Functions.ILike(item.Name, pattern, @"\") ||
                    EF.Functions.ILike(item.CalculationBase, pattern, @"\"));
            }
            else
            {
                query = query.Where(item =>
                    item.Name.ToLower().Contains(normalizedSearch) ||
                    item.CalculationBase.ToLower().Contains(normalizedSearch));
            }
        }

        return query;
    }

    private sealed class TariffPageRow
    {
        public int Category { get; init; }
        public Guid? Id { get; init; }
        public string? Name { get; init; }
        public string? CalculationBase { get; init; }
        public decimal? Rate { get; init; }
        public decimal? ElectricityFirstThreshold { get; init; }
        public decimal? ElectricitySecondThreshold { get; init; }
        public string? ElectricityFirstTierName { get; init; }
        public string? ElectricitySecondTierName { get; init; }
        public string? ElectricityThirdTierName { get; init; }
        public decimal? ElectricityFirstRate { get; init; }
        public decimal? ElectricitySecondRate { get; init; }
        public decimal? ElectricityThirdRate { get; init; }
        public string? ElectricityTiersJson { get; init; }
        public DateOnly? EffectiveFrom { get; init; }
        public string? Comment { get; init; }
        public bool? IsArchived { get; init; }
        public Guid? Version { get; init; }
        public int TotalCount { get; init; }
    }
}
