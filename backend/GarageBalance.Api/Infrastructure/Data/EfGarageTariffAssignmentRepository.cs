using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageTariffAssignmentRepository(GarageBalanceDbContext context,
    IAccrualPaymentAllocationRepository allocationRepository) : IGarageTariffAssignmentRepository
{
    public async Task<GarageTariffAssignmentPage> GetPageAsync(Guid serviceId, Guid? garageId, bool includeArchived,
        int offset, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (limit > QueryLimits.MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
        var query = context.GarageTariffAssignments.AsNoTracking()
            .Where(item => item.ChargeServiceSettingId == serviceId && (!garageId.HasValue || item.GarageId == garageId)
                && (includeArchived || !item.IsArchived));
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.Include(item => item.Garage).Include(item => item.Tariff)
            .OrderBy(item => item.Garage.Number.Length).ThenBy(item => item.Garage.Number).ThenByDescending(item => item.EffectiveFrom).ThenBy(item => item.Id)
            .Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return new(rows, count);
    }

    public Task<Guid?> FindGarageIdAsync(Guid serviceId, Guid assignmentId, CancellationToken cancellationToken) =>
        context.GarageTariffAssignments.AsNoTracking().Where(item => item.Id == assignmentId && item.ChargeServiceSettingId == serviceId)
            .Select(item => (Guid?)item.GarageId).SingleOrDefaultAsync(cancellationToken);

    public Task<GarageTariffAssignment?> FindForUpdateAsync(Guid serviceId, Guid assignmentId, CancellationToken cancellationToken) =>
        context.GarageTariffAssignments.Include(item => item.Garage).Include(item => item.Tariff)
            .SingleOrDefaultAsync(item => item.Id == assignmentId && item.ChargeServiceSettingId == serviceId, cancellationToken);

    public Task<bool> HasOverlapAsync(Guid serviceId, IReadOnlyCollection<Guid> garageIds, DateOnly from, DateOnly? to,
        Guid? ignoredId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (to < from) throw new ArgumentException("Конец периода не может предшествовать началу.", nameof(to));
        if (garageIds.Count > 100) throw new ArgumentOutOfRangeException(nameof(garageIds));
        if (garageIds.Count == 0) return Task.FromResult(false);
        return context.GarageTariffAssignments.AsNoTracking().AnyAsync(item => item.ChargeServiceSettingId == serviceId
            && garageIds.Contains(item.GarageId) && !item.IsArchived && (!ignoredId.HasValue || item.Id != ignoredId)
            && (!to.HasValue || item.EffectiveFrom <= to) && (!item.EffectiveTo.HasValue || item.EffectiveTo >= from), cancellationToken);
    }

    public async Task<IGarageTariffWriteScope> BeginWriteAsync(IReadOnlyCollection<Guid> garageIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (garageIds.Count is < 1 or > 100 || garageIds.Contains(Guid.Empty))
            throw new ArgumentException("Выберите от 1 до 100 гаражей.", nameof(garageIds));
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Операция назначения тарифа должна владеть своей транзакцией.");
        var locks = new List<IAsyncDisposable>();
        try
        {
            foreach (var id in garageIds.Distinct().Order())
                locks.Add(await allocationRepository.AcquireGarageIncomeWorksheetLockAsync(id, cancellationToken));
            var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            return new WriteScope(transaction, locks);
        }
        catch
        {
            for (var index = locks.Count - 1; index >= 0; index--) await locks[index].DisposeAsync();
            throw;
        }
    }

    public void Add(GarageTariffAssignment assignment) => context.GarageTariffAssignments.Add(assignment);

    private sealed class WriteScope(IDbContextTransaction transaction, List<IAsyncDisposable> locks) : IGarageTariffWriteScope
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally { for (var index = locks.Count - 1; index >= 0; index--) await locks[index].DisposeAsync(); }
        }
    }
}
