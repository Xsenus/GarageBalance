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
        var query = dbContext.FeeCampaigns.AsNoTracking()
            .Where(item => includeArchived || !item.IsArchived);
        if (normalizedSearch is not null)
        {
            if (dbContext.Database.IsNpgsql())
            {
                var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
                query = query.Where(item =>
                    EF.Functions.ILike(item.Name, pattern, @"\") ||
                    (item.Goal != null && EF.Functions.ILike(item.Goal, pattern, @"\")));
            }
            else
            {
                query = query.Where(item =>
                    item.Name.ToLower().Contains(normalizedSearch) ||
                    (item.Goal != null && item.Goal.ToLower().Contains(normalizedSearch)));
            }
        }

        return await ReadCompactListAsync(
            query
                .OrderBy(item => item.IsArchived)
                .ThenByDescending(item => item.StartsOn)
                .ThenBy(item => item.Name)
                .Take(limit),
            cancellationToken);
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
        return await BuildCollectedAmountsQuery([id])
            .SumAsync(item => item.Amount, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetCollectedAmountsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        return await BuildCollectedAmountsQuery(ids)
            .ToDictionaryAsync(item => item.Id, item => item.Amount, cancellationToken);
    }

    public async Task<IReadOnlyList<FeeCampaignPaymentOption>> GetPaymentOptionsForGarageAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken)
    {
        var periodStart = monthFrom;
        var periodEnd = monthTo.AddMonths(1).AddDays(-1);
        var campaigns = WithDetails(dbContext.FeeCampaigns)
            .Where(item =>
                !item.IsArchived &&
                item.StartsOn <= periodEnd &&
                (!item.EndsOn.HasValue || item.EndsOn.Value >= periodStart) &&
                (item.AppliesToAllGarages || item.ParticipantGarages.Any(link => link.GarageId == garageId)))
            .OrderBy(item => item.StartsOn)
            .ThenBy(item => item.Id);
        var rows = await campaigns
            .Select(campaign => new FeeCampaignPaymentOptionQueryRow(
                campaign,
                dbContext.Accruals
                    .Where(accrual =>
                        !accrual.IsCanceled &&
                        accrual.GarageId == garageId &&
                        accrual.FeeCampaignId == campaign.Id)
                    .OrderBy(accrual => accrual.AccountingMonth)
                    .ThenBy(accrual => accrual.Id)
                    .FirstOrDefault(),
                dbContext.AccrualPaymentAllocations
                    .Where(allocation =>
                        allocation.IsActive &&
                        !allocation.FinancialOperation.IsCanceled &&
                        allocation.AccrualId == dbContext.Accruals
                            .Where(accrual =>
                                !accrual.IsCanceled &&
                                accrual.GarageId == garageId &&
                                accrual.FeeCampaignId == campaign.Id)
                            .OrderBy(accrual => accrual.AccountingMonth)
                            .ThenBy(accrual => accrual.Id)
                            .Select(accrual => accrual.Id)
                            .FirstOrDefault())
                    .Sum(allocation => (decimal?)allocation.Amount) ?? 0m,
                (dbContext.FinancialOperations
                    .Where(operation =>
                        !operation.IsCanceled &&
                        operation.OperationKind == GarageBalance.Api.Domain.Finance.FinancialOperationKinds.Income &&
                        operation.FeeCampaignId == campaign.Id)
                    .Sum(operation => (decimal?)operation.Amount) ?? 0m) +
                (dbContext.AccrualPaymentAllocations
                    .Where(allocation =>
                        allocation.IsActive &&
                        !allocation.Accrual.IsCanceled &&
                        allocation.Accrual.FeeCampaignId == campaign.Id &&
                        !allocation.FinancialOperation.IsCanceled &&
                        allocation.FinancialOperation.FeeCampaignId == null)
                    .Sum(allocation => (decimal?)allocation.Amount) ?? 0m)))
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new FeeCampaignPaymentOption(
                row.Campaign,
                row.Accrual,
                row.PaidAmount,
                row.CollectedAmount))
            .ToList();
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
        var tagged = dbContext.FinancialOperations.AsNoTracking()
            .Where(item => !item.IsCanceled && item.FeeCampaignId == id && item.GarageId.HasValue)
            .Select(item => new FeeCampaignAmountRow
            {
                Id = item.GarageId!.Value,
                Amount = item.Amount
            });
        var legacy = dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(item => item.IsActive && !item.FinancialOperation.IsCanceled && item.FinancialOperation.FeeCampaignId == null && item.Accrual.FeeCampaignId == id)
            .Select(item => new FeeCampaignAmountRow
            {
                Id = item.Accrual.GarageId,
                Amount = item.Amount
            });
        return await tagged
            .Concat(legacy)
            .GroupBy(item => item.Id)
            .Select(group => new FeeCampaignAmountRow
            {
                Id = group.Key,
                Amount = group.Sum(item => item.Amount)
            })
            .ToDictionaryAsync(item => item.Id, item => item.Amount, cancellationToken);
    }

    public void Add(FeeCampaign campaign) => dbContext.FeeCampaigns.Add(campaign);

    private static async Task<IReadOnlyList<FeeCampaign>> ReadCompactListAsync(
        IQueryable<FeeCampaign> query,
        CancellationToken cancellationToken)
    {
        var rows = await query
            .Select(campaign => new FeeCampaignListRow(
                campaign.Id,
                campaign.Name,
                campaign.IncomeTypeId,
                campaign.IncomeType.Name,
                campaign.IncomeType.DestinationFundId,
                campaign.IncomeType.DestinationFund == null ? null : campaign.IncomeType.DestinationFund.Name,
                campaign.Goal,
                campaign.ContributionAmount,
                campaign.TargetAmount,
                campaign.StartsOn,
                campaign.EndsOn,
                campaign.AppliesToAllGarages,
                campaign.ParticipantGarages
                    .OrderBy(participant => participant.Garage.Number)
                    .Select(participant => new FeeCampaignParticipantListRow(
                        participant.GarageId,
                        participant.Garage.Number))
                    .ToList(),
                campaign.OverdueGraceDays,
                campaign.ClosedAtUtc,
                campaign.IsClosedEarly,
                campaign.ClosureComment,
                campaign.IsArchived))
            .ToListAsync(cancellationToken);

        return rows.Select(CreateCompactCampaign).ToList();
    }

    private static FeeCampaign CreateCompactCampaign(FeeCampaignListRow row)
    {
        var campaign = new FeeCampaign
        {
            Id = row.Id,
            Name = row.Name,
            IncomeTypeId = row.IncomeTypeId,
            IncomeType = new IncomeType
            {
                Id = row.IncomeTypeId,
                Name = row.IncomeTypeName,
                DestinationFundId = row.DestinationFundId,
                DestinationFund = row.DestinationFundId is null
                    ? null
                    : new GarageBalance.Api.Domain.Finance.Fund
                    {
                        Id = row.DestinationFundId.Value,
                        Name = row.DestinationFundName ?? string.Empty,
                        NormalizedName = row.DestinationFundName ?? string.Empty
                    }
            },
            Goal = row.Goal,
            ContributionAmount = row.ContributionAmount,
            TargetAmount = row.TargetAmount,
            StartsOn = row.StartsOn,
            EndsOn = row.EndsOn,
            AppliesToAllGarages = row.AppliesToAllGarages,
            ParticipantGarages = row.Participants
                .Select(participant => new FeeCampaignGarage
                {
                    GarageId = participant.GarageId,
                    Garage = new Garage
                    {
                        Id = participant.GarageId,
                        Number = participant.GarageNumber
                    }
                })
                .ToList(),
            OverdueGraceDays = row.OverdueGraceDays,
            ClosedAtUtc = row.ClosedAtUtc,
            IsClosedEarly = row.IsClosedEarly,
            ClosureComment = row.ClosureComment,
            IsArchived = row.IsArchived
        };
        foreach (var participant in campaign.ParticipantGarages)
        {
            participant.FeeCampaignId = campaign.Id;
            participant.FeeCampaign = campaign;
        }

        return campaign;
    }

    private static IQueryable<FeeCampaign> WithDetails(IQueryable<FeeCampaign> query) =>
        query
            .Include(item => item.IncomeType)
                .ThenInclude(item => item.DestinationFund)
            .Include(item => item.ParticipantGarages)
                .ThenInclude(item => item.Garage);

    private IQueryable<FeeCampaignAmountRow> BuildCollectedAmountsQuery(IReadOnlyCollection<Guid> ids)
    {
        var tagged = dbContext.FinancialOperations.AsNoTracking()
            .Where(item =>
                !item.IsCanceled &&
                item.OperationKind == GarageBalance.Api.Domain.Finance.FinancialOperationKinds.Income &&
                item.FeeCampaignId.HasValue &&
                ids.Contains(item.FeeCampaignId.Value))
            .Select(item => new FeeCampaignAmountRow
            {
                Id = item.FeeCampaignId!.Value,
                Amount = item.Amount
            });
        var legacy = dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(item =>
                item.IsActive &&
                !item.Accrual.IsCanceled &&
                item.Accrual.FeeCampaignId.HasValue &&
                ids.Contains(item.Accrual.FeeCampaignId.Value) &&
                !item.FinancialOperation.IsCanceled &&
                item.FinancialOperation.FeeCampaignId == null)
            .Select(item => new FeeCampaignAmountRow
            {
                Id = item.Accrual.FeeCampaignId!.Value,
                Amount = item.Amount
            });
        return tagged
            .Concat(legacy)
            .GroupBy(item => item.Id)
            .Select(group => new FeeCampaignAmountRow
            {
                Id = group.Key,
                Amount = group.Sum(item => item.Amount)
            });
    }

    private sealed record FeeCampaignPaymentOptionQueryRow(
        FeeCampaign Campaign,
        GarageBalance.Api.Domain.Finance.Accrual? Accrual,
        decimal PaidAmount,
        decimal CollectedAmount);

    private sealed record FeeCampaignListRow(
        Guid Id,
        string Name,
        Guid IncomeTypeId,
        string IncomeTypeName,
        Guid? DestinationFundId,
        string? DestinationFundName,
        string? Goal,
        decimal ContributionAmount,
        decimal TargetAmount,
        DateOnly StartsOn,
        DateOnly? EndsOn,
        bool AppliesToAllGarages,
        List<FeeCampaignParticipantListRow> Participants,
        int OverdueGraceDays,
        DateTimeOffset? ClosedAtUtc,
        bool IsClosedEarly,
        string? ClosureComment,
        bool IsArchived);

    private sealed record FeeCampaignParticipantListRow(Guid GarageId, string GarageNumber);

    private sealed class FeeCampaignAmountRow
    {
        public Guid Id { get; init; }
        public decimal Amount { get; init; }
    }

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
