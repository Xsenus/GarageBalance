using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfChargeServiceSettingRepository(GarageBalanceDbContext dbContext) : IChargeServiceSettingRepository
{
    public async Task<IReadOnlyList<ChargeServiceSetting>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ChargeServiceSettings.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
        if (normalizedSearch is not null)
        {
            query = query.Where(item => item.Name.ToLower().Contains(normalizedSearch));
        }

        return await query
            .OrderBy(item => item.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularAsync(CancellationToken cancellationToken) =>
        await dbContext.ChargeServiceSettings.AsNoTracking()
            .Where(setting => !setting.IsArchived && setting.IsRegular)
            .OrderBy(setting => setting.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularMeteredAsync(
        string calculationBase,
        DateOnly accountingMonth,
        int limit,
        CancellationToken cancellationToken) =>
        await dbContext.ChargeServiceSettings
            .Include(setting => setting.IncomeType)
            .Include(setting => setting.Tariff)
            .Where(setting =>
                !setting.IsArchived &&
                setting.IsRegular &&
                setting.IsMetered &&
                setting.IncomeType != null &&
                !setting.IncomeType.IsArchived &&
                setting.Tariff != null &&
                !setting.Tariff.IsArchived &&
                setting.Tariff.CalculationBase == calculationBase &&
                setting.Tariff.EffectiveFrom <= accountingMonth)
            .OrderBy(setting => setting.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularForDueDatesAsync(
        Guid incomeTypeId,
        Guid? tariffId,
        CancellationToken cancellationToken) =>
        await dbContext.ChargeServiceSettings.AsNoTracking()
            .Where(setting =>
                !setting.IsArchived &&
                setting.IsRegular &&
                setting.IncomeTypeId == incomeTypeId &&
                (!tariffId.HasValue || setting.TariffId == tariffId.Value))
            .OrderBy(setting => setting.Name)
            .Take(2)
            .ToListAsync(cancellationToken);

    public Task<ChargeServiceSetting?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceSettings.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);

    public Task<ChargeServiceSetting?> FindArchivedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceSettings.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceSettings.AsNoTracking().AnyAsync(
            item => !item.IsArchived && item.Name == name && (!ignoredId.HasValue || item.Id != ignoredId.Value),
            cancellationToken);

    public void Add(ChargeServiceSetting setting) => dbContext.ChargeServiceSettings.Add(setting);
}
