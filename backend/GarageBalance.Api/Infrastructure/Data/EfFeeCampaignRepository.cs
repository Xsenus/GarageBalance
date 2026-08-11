using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;
using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFeeCampaignRepository(GarageBalanceDbContext dbContext) : IFeeCampaignRepository
{
    public async Task<IReadOnlyList<FeeCampaign>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = WithDetails(dbContext.FeeCampaigns.AsNoTracking())
            .Where(item => includeArchived || !item.IsArchived);
        if (normalizedSearch is not null)
        {
            query = query.Where(item =>
                item.Name.ToLower().Contains(normalizedSearch) ||
                (item.Goal != null && item.Goal.ToLower().Contains(normalizedSearch)));
        }

        return await query
            .OrderBy(item => item.IsArchived)
            .ThenByDescending(item => item.StartsOn)
            .ThenBy(item => item.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<FeeCampaign?> FindActiveWithDetailsAsync(Guid id, CancellationToken cancellationToken) =>
        WithDetails(dbContext.FeeCampaigns)
            .SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);

    public async Task<IReadOnlyList<FeeCampaign>> GetActiveAccrualCandidatesAsync(
        DateOnly accountingMonth,
        int limit,
        CancellationToken cancellationToken)
    {
        var monthEnd = accountingMonth.AddMonths(1).AddDays(-1);
        return await dbContext.FeeCampaigns
            .AsNoTracking()
            .Where(item =>
                !item.IsArchived &&
                item.ClosedAtUtc == null &&
                item.StartsOn <= monthEnd &&
                (!item.EndsOn.HasValue || item.EndsOn.Value >= accountingMonth))
            .OrderBy(item => item.StartsOn)
            .ThenBy(item => item.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<FeeCampaign?> FindActiveForAccrualGenerationAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.FeeCampaigns
            .Include(item => item.IncomeType)
            .Include(item => item.ParticipantGarages)
                .ThenInclude(item => item.Garage)
                    .ThenInclude(garage => garage.Owner)
            .SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);

    public Task<FeeCampaign?> FindArchivedWithDetailsAsync(Guid id, CancellationToken cancellationToken) =>
        WithDetails(dbContext.FeeCampaigns)
            .SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken) =>
        dbContext.FeeCampaigns.AsNoTracking().AnyAsync(
            item => !item.IsArchived && item.Name == name && (!ignoredId.HasValue || item.Id != ignoredId.Value),
            cancellationToken);

    public Task<bool> HasAccrualsAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Accruals.AsNoTracking().AnyAsync(item => item.FeeCampaignId == id, cancellationToken);

    public async Task<decimal> GetCollectedAmountAsync(Guid id, CancellationToken cancellationToken)
    {
        var tagged = await dbContext.FinancialOperations
            .AsNoTracking()
            .Where(item =>
                !item.IsCanceled &&
                item.OperationKind == GarageBalance.Api.Domain.Finance.FinancialOperationKinds.Income &&
                item.FeeCampaignId == id)
            .SumAsync(item => item.Amount, cancellationToken);
        var legacyAllocated = await dbContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item =>
                item.IsActive &&
                !item.Accrual.IsCanceled &&
                item.Accrual.FeeCampaignId == id &&
                !item.FinancialOperation.IsCanceled &&
                item.FinancialOperation.FeeCampaignId == null)
            .SumAsync(item => item.Amount, cancellationToken);
        return tagged + legacyAllocated;
    }

    public async Task<IReadOnlyList<FeeCampaignPaymentOption>> GetPaymentOptionsForGarageAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken)
    {
        var periodStart = monthFrom;
        var periodEnd = monthTo.AddMonths(1).AddDays(-1);
        var campaigns = await WithDetails(dbContext.FeeCampaigns)
            .Where(item =>
                !item.IsArchived &&
                item.StartsOn <= periodEnd &&
                (!item.EndsOn.HasValue || item.EndsOn.Value >= periodStart) &&
                (item.AppliesToAllGarages || item.ParticipantGarages.Any(link => link.GarageId == garageId)))
            .OrderBy(item => item.StartsOn)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        if (campaigns.Count == 0)
        {
            return [];
        }

        var ids = campaigns.Select(item => item.Id).ToArray();
        var accruals = await dbContext.Accruals
            .Include(item => item.FeeCampaign)
            .Where(item => !item.IsCanceled && item.GarageId == garageId && item.FeeCampaignId.HasValue && ids.Contains(item.FeeCampaignId.Value))
            .OrderBy(item => item.AccountingMonth)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var paidByAccrual = await dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(item => item.IsActive && !item.FinancialOperation.IsCanceled && item.Accrual.GarageId == garageId && item.Accrual.FeeCampaignId.HasValue && ids.Contains(item.Accrual.FeeCampaignId.Value))
            .GroupBy(item => item.AccrualId)
            .Select(group => new { Id = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.Id, item => item.Amount, cancellationToken);
        var collected = await dbContext.FinancialOperations.AsNoTracking()
            .Where(item => !item.IsCanceled && item.OperationKind == GarageBalance.Api.Domain.Finance.FinancialOperationKinds.Income && item.FeeCampaignId.HasValue && ids.Contains(item.FeeCampaignId.Value))
            .GroupBy(item => item.FeeCampaignId!.Value)
            .Select(group => new { Id = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.Id, item => item.Amount, cancellationToken);
        var legacyCollected = await dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(item => item.IsActive && !item.FinancialOperation.IsCanceled && item.FinancialOperation.FeeCampaignId == null && item.Accrual.FeeCampaignId.HasValue && ids.Contains(item.Accrual.FeeCampaignId.Value))
            .GroupBy(item => item.Accrual.FeeCampaignId!.Value)
            .Select(group => new { Id = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.Id, item => item.Amount, cancellationToken);

        return campaigns.Select(campaign =>
        {
            var accrual = accruals.FirstOrDefault(item => item.FeeCampaignId == campaign.Id);
            return new FeeCampaignPaymentOption(campaign, accrual, accrual is null ? 0m : paidByAccrual.GetValueOrDefault(accrual.Id), collected.GetValueOrDefault(campaign.Id) + legacyCollected.GetValueOrDefault(campaign.Id));
        }).ToList();
    }

    public async Task<IAsyncDisposable> AcquirePaymentLockAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return NoOpAsyncDisposable.Instance;
        }

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(id.ToByteArray(), hash);
        var key = BinaryPrimitives.ReadInt64BigEndian(hash);
        var connection = dbContext.Database.GetDbConnection();
        var close = connection.State == ConnectionState.Closed;
        if (close) await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = key;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new AdvisoryLockLease(connection, key, close);
    }

    public async Task<IReadOnlyList<GarageBalance.Api.Domain.Finance.Accrual>> GetAccrualsForSettlementAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.Accruals
            .Where(item => !item.IsCanceled && item.FeeCampaignId == id)
            .OrderBy(item => item.AccountingMonth)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetPaidAmountsByGarageAsync(Guid id, CancellationToken cancellationToken)
    {
        var tagged = await dbContext.FinancialOperations.AsNoTracking()
            .Where(item => !item.IsCanceled && item.FeeCampaignId == id && item.GarageId.HasValue)
            .GroupBy(item => item.GarageId!.Value)
            .Select(group => new { GarageId = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.GarageId, item => item.Amount, cancellationToken);
        var legacy = await dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(item => item.IsActive && !item.FinancialOperation.IsCanceled && item.FinancialOperation.FeeCampaignId == null && item.Accrual.FeeCampaignId == id)
            .GroupBy(item => item.Accrual.GarageId)
            .Select(group => new { GarageId = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.GarageId, item => item.Amount, cancellationToken);
        foreach (var item in legacy) tagged[item.Key] = tagged.GetValueOrDefault(item.Key) + item.Value;
        return tagged;
    }

    public void Add(FeeCampaign campaign) => dbContext.FeeCampaigns.Add(campaign);

    private static IQueryable<FeeCampaign> WithDetails(IQueryable<FeeCampaign> query) =>
        query
            .Include(item => item.IncomeType)
            .Include(item => item.ParticipantGarages)
                .ThenInclude(item => item.Garage);

    private sealed class AdvisoryLockLease(System.Data.Common.DbConnection connection, long key, bool close) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "key";
            parameter.Value = key;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync();
            if (close) await connection.CloseAsync();
        }
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static readonly NoOpAsyncDisposable Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
