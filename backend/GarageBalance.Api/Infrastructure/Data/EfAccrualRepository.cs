using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfAccrualRepository(GarageBalanceDbContext dbContext) : IAccrualRepository
{
    public async Task<IReadOnlyList<Accrual>> GetListAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyPeriod(QueryActive(), monthFrom, monthTo);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            return (await Order(query).ToListAsync(cancellationToken))
                .Where(accrual => AccrualMatchesSearch(accrual, normalizedSearch))
                .Take(limit)
                .ToList();
        }

        return await Order(ApplySearch(query, normalizedSearch))
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<AccrualPageData> GetPageAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyPeriod(QueryActive(), monthFrom, monthTo);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            var filtered = (await Order(query).ToListAsync(cancellationToken))
                .Where(accrual => AccrualMatchesSearch(accrual, normalizedSearch))
                .ToList();
            return new AccrualPageData(filtered.Skip(offset).Take(limit).ToList(), filtered.Count);
        }

        query = ApplySearch(query, normalizedSearch);
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresPageAsync(query, offset, limit, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await Order(query)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new AccrualPageData(items, totalCount);
    }

    private async Task<AccrualPageData> GetPostgresPageAsync(
        IQueryable<Accrual> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = Order(query)
            .Skip(offset)
            .Take(limit)
            .Select(accrual => new
            {
                Category = PageCategory,
                Id = (Guid?)accrual.Id,
                GarageId = (Guid?)accrual.GarageId,
                GarageNumber = (string?)accrual.Garage.Number,
                OwnerId = accrual.Garage.OwnerId,
                OwnerLastName = accrual.Garage.Owner == null ? null : accrual.Garage.Owner.LastName,
                OwnerFirstName = accrual.Garage.Owner == null ? null : accrual.Garage.Owner.FirstName,
                OwnerMiddleName = accrual.Garage.Owner == null ? null : accrual.Garage.Owner.MiddleName,
                IncomeTypeId = (Guid?)accrual.IncomeTypeId,
                IncomeTypeName = (string?)accrual.IncomeType.Name,
                accrual.IrregularPaymentId,
                IrregularPaymentName = accrual.IrregularPayment == null ? null : accrual.IrregularPayment.Name,
                accrual.FeeCampaignId,
                FeeCampaignName = accrual.FeeCampaign == null ? null : accrual.FeeCampaign.Name,
                accrual.TariffId,
                AccountingMonth = (DateOnly?)accrual.AccountingMonth,
                accrual.AccountingYear,
                DueDate = (DateOnly?)accrual.DueDate,
                OverdueFromDate = (DateOnly?)accrual.OverdueFromDate,
                DueDateNeedsReview = (bool?)accrual.DueDateNeedsReview,
                accrual.DueDateReviewReason,
                Amount = (decimal?)accrual.Amount,
                Source = (string?)accrual.Source,
                accrual.Comment,
                IsCanceled = (bool?)accrual.IsCanceled,
                CreatedAtUtc = (DateTimeOffset?)accrual.CreatedAtUtc,
                UpdatedAtUtc = (DateTimeOffset?)accrual.UpdatedAtUtc,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new
            {
                Category = TotalsCategory,
                Id = (Guid?)null,
                GarageId = (Guid?)null,
                GarageNumber = (string?)null,
                OwnerId = (Guid?)null,
                OwnerLastName = (string?)null,
                OwnerFirstName = (string?)null,
                OwnerMiddleName = (string?)null,
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = (string?)null,
                IrregularPaymentId = (Guid?)null,
                IrregularPaymentName = (string?)null,
                FeeCampaignId = (Guid?)null,
                FeeCampaignName = (string?)null,
                TariffId = (Guid?)null,
                AccountingMonth = (DateOnly?)null,
                AccountingYear = (int?)null,
                DueDate = (DateOnly?)null,
                OverdueFromDate = (DateOnly?)null,
                DueDateNeedsReview = (bool?)null,
                DueDateReviewReason = (string?)null,
                Amount = (decimal?)null,
                Source = (string?)null,
                Comment = (string?)null,
                IsCanceled = (bool?)null,
                CreatedAtUtc = (DateTimeOffset?)null,
                UpdatedAtUtc = (DateTimeOffset?)null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenByDescending(row => row.AccountingMonth)
            .ThenBy(row => row.GarageNumber)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new Accrual
            {
                Id = row.Id!.Value,
                GarageId = row.GarageId!.Value,
                Garage = new Garage
                {
                    Id = row.GarageId.Value,
                    Number = row.GarageNumber!,
                    OwnerId = row.OwnerId,
                    Owner = row.OwnerId is null
                        ? null
                        : new Owner
                        {
                            Id = row.OwnerId.Value,
                            LastName = row.OwnerLastName!,
                            FirstName = row.OwnerFirstName!,
                            MiddleName = row.OwnerMiddleName
                        }
                },
                IncomeTypeId = row.IncomeTypeId!.Value,
                IncomeType = new IncomeType { Id = row.IncomeTypeId.Value, Name = row.IncomeTypeName! },
                IrregularPaymentId = row.IrregularPaymentId,
                IrregularPayment = row.IrregularPaymentId is null
                    ? null
                    : new IrregularPayment { Id = row.IrregularPaymentId.Value, Name = row.IrregularPaymentName! },
                FeeCampaignId = row.FeeCampaignId,
                FeeCampaign = row.FeeCampaignId is null
                    ? null
                    : new FeeCampaign { Id = row.FeeCampaignId.Value, Name = row.FeeCampaignName! },
                TariffId = row.TariffId,
                AccountingMonth = row.AccountingMonth!.Value,
                AccountingYear = row.AccountingYear,
                DueDate = row.DueDate!.Value,
                OverdueFromDate = row.OverdueFromDate!.Value,
                DueDateNeedsReview = row.DueDateNeedsReview!.Value,
                DueDateReviewReason = row.DueDateReviewReason,
                Amount = row.Amount!.Value,
                Source = row.Source!,
                Comment = row.Comment,
                IsCanceled = row.IsCanceled!.Value,
                CreatedAtUtc = row.CreatedAtUtc!.Value,
                UpdatedAtUtc = row.UpdatedAtUtc!.Value
            })
            .ToList();
        return new AccrualPageData(items, totalCount);
    }

    public async Task<AccrualPageData> GetDueDateReviewPageAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Accruals.AsNoTracking()
            .Include(accrual => accrual.Garage)
            .Include(accrual => accrual.IncomeType)
            .Where(accrual => !accrual.IsCanceled && accrual.DueDateNeedsReview);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Garage.Number)
            .ThenBy(accrual => accrual.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new AccrualPageData(items, totalCount);
    }

    public async Task<decimal> GetTotalBeforeMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken) =>
        await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth < accountingMonth)
            .SumAsync(accrual => accrual.Amount, cancellationToken);

    public async Task<IReadOnlyList<OverdueAccrualDebtData>> GetOverdueDebtDetailsAsync(
        Guid garageId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                !accrual.DueDateNeedsReview &&
                accrual.GarageId == garageId &&
                accrual.OverdueFromDate <= asOfDate)
            .Select(accrual => new
            {
                AccrualId = accrual.Id,
                accrual.IncomeTypeId,
                IncomeTypeName = accrual.IncomeType.Name,
                accrual.AccountingMonth,
                accrual.DueDate,
                accrual.OverdueFromDate,
                accrual.Amount,
                PaidAmount = dbContext.AccrualPaymentAllocations
                    .Where(allocation =>
                        allocation.IsActive &&
                        allocation.AccrualId == accrual.Id &&
                        !allocation.FinancialOperation.IsCanceled)
                    .Sum(allocation => (decimal?)allocation.Amount) ?? 0m
            });

        var rows = await query.ToListAsync(cancellationToken);
        return rows
            .Select(row => new OverdueAccrualDebtData(
                row.AccrualId,
                row.IncomeTypeId,
                row.IncomeTypeName,
                row.AccountingMonth,
                row.DueDate,
                row.OverdueFromDate,
                row.Amount,
                row.PaidAmount,
                Math.Max(row.Amount - row.PaidAmount, 0m)))
            .Where(row => row.OutstandingAmount > 0m)
            .OrderBy(row => row.OverdueFromDate)
            .ThenBy(row => row.DueDate)
            .ThenBy(row => row.AccountingMonth)
            .ThenBy(row => row.IncomeTypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.AccrualId)
            .ToList();
    }

    public async Task<IReadOnlyList<AccrualBucketData>> GetMonthlyBucketsAsync(
        Guid garageId,
        DateOnly? monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth <= monthTo);
        if (monthFrom.HasValue)
        {
            query = query.Where(accrual => accrual.AccountingMonth >= monthFrom.Value);
        }

        var rows = await query
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new { AccountingMonth = group.Key, Amount = group.Sum(accrual => accrual.Amount) })
            .OrderBy(bucket => bucket.AccountingMonth)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new AccrualBucketData(row.AccountingMonth, row.Amount)).ToList();
    }

    public Task<Accrual?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        TrackedAggregate().SingleOrDefaultAsync(accrual => accrual.Id == id, cancellationToken);

    public Task<Accrual?> FindActiveForUpdateAsync(
        Guid garageId,
        Guid incomeTypeId,
        DateOnly accountingMonth,
        string source,
        CancellationToken cancellationToken) =>
        TrackedAggregate().SingleOrDefaultAsync(accrual =>
            !accrual.IsCanceled &&
            accrual.GarageId == garageId &&
            accrual.IncomeTypeId == incomeTypeId &&
            accrual.AccountingMonth == accountingMonth &&
            accrual.Source == source,
            cancellationToken);

    public async Task<IReadOnlyList<Accrual>> GetActiveMeteredForUpdateAsync(
        Guid garageId,
        DateOnly accountingMonth,
        string meterKind,
        CancellationToken cancellationToken)
    {
        var calculationBase = meterKind switch
        {
            MeterKinds.Water => TariffCalculationBases.MeterWater,
            MeterKinds.Electricity => TariffCalculationBases.MeterElectricity,
            _ => null
        };
        if (calculationBase is null)
        {
            return [];
        }

        return await TrackedAggregate()
            .Include(accrual => accrual.Tariff)
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.GarageId == garageId &&
                accrual.AccountingMonth == accountingMonth &&
                accrual.Source == AccrualSources.Regular &&
                accrual.Tariff != null &&
                accrual.Tariff.CalculationBase == calculationBase)
            .OrderBy(accrual => accrual.IncomeTypeId)
            .ThenBy(accrual => accrual.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetActiveGarageIdsAsync(
        Guid incomeTypeId,
        DateOnly accountingMonth,
        string source,
        CancellationToken cancellationToken) =>
        await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.IncomeTypeId == incomeTypeId &&
                accrual.AccountingMonth == accountingMonth &&
                accrual.Source == source)
            .Select(accrual => accrual.GarageId)
            .ToHashSetAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetActiveFeeCampaignGarageIdsAsync(
        Guid feeCampaignId,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.FeeCampaignId == feeCampaignId &&
                accrual.AccountingMonth == accountingMonth)
            .Select(accrual => accrual.GarageId)
            .ToHashSetAsync(cancellationToken);

    public Task<int> CountActiveForGenerationAsync(
        Guid incomeTypeId,
        DateOnly accountingMonth,
        string source,
        CancellationToken cancellationToken) =>
        dbContext.Accruals.AsNoTracking().CountAsync(accrual =>
            !accrual.IsCanceled &&
            !accrual.Garage.IsArchived &&
            accrual.IncomeTypeId == incomeTypeId &&
            accrual.AccountingMonth == accountingMonth &&
            accrual.Source == source,
            cancellationToken);

    public Task<int> CountActiveAnnualRegularForGenerationAsync(
        Guid incomeTypeId,
        int accountingYear,
        CancellationToken cancellationToken) =>
        dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                !accrual.Garage.IsArchived &&
                accrual.IncomeTypeId == incomeTypeId &&
                accrual.AccountingYear == accountingYear &&
                accrual.Source == AccrualSources.Regular)
            .Select(accrual => accrual.GarageId)
            .Distinct()
            .CountAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetActiveAnnualRegularGarageIdsAsync(
        Guid incomeTypeId,
        int accountingYear,
        CancellationToken cancellationToken) =>
        await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.IncomeTypeId == incomeTypeId &&
                accrual.AccountingYear == accountingYear &&
                accrual.Source == AccrualSources.Regular)
            .Select(accrual => accrual.GarageId)
            .ToHashSetAsync(cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(
        Guid? ignoredId,
        Guid garageId,
        Guid incomeTypeId,
        DateOnly accountingMonth,
        int? accountingYear,
        string source,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Accruals.AsNoTracking().Where(accrual =>
            !accrual.IsCanceled &&
            (!ignoredId.HasValue || accrual.Id != ignoredId.Value) &&
            accrual.GarageId == garageId &&
            accrual.IncomeTypeId == incomeTypeId &&
            accrual.Source == source);
        return source == AccrualSources.Regular && accountingYear.HasValue
            ? query.AnyAsync(accrual => accrual.AccountingYear == accountingYear.Value, cancellationToken)
            : query.AnyAsync(accrual => accrual.AccountingMonth == accountingMonth, cancellationToken);
    }

    public Task<bool> ActiveIrregularDuplicateExistsAsync(
        Guid? ignoredId,
        Guid garageId,
        Guid irregularPaymentId,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        dbContext.Accruals.AsNoTracking().AnyAsync(accrual =>
            !accrual.IsCanceled &&
            (!ignoredId.HasValue || accrual.Id != ignoredId.Value) &&
            accrual.GarageId == garageId &&
            accrual.IrregularPaymentId == irregularPaymentId &&
            accrual.AccountingMonth == accountingMonth,
            cancellationToken);

    public Task<bool> ActiveFeeCampaignDuplicateExistsAsync(
        Guid? ignoredId,
        Guid garageId,
        Guid feeCampaignId,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        dbContext.Accruals.AsNoTracking().AnyAsync(accrual =>
            !accrual.IsCanceled &&
            (!ignoredId.HasValue || accrual.Id != ignoredId.Value) &&
            accrual.GarageId == garageId &&
            accrual.FeeCampaignId == feeCampaignId &&
            accrual.AccountingMonth == accountingMonth,
            cancellationToken);

    public async Task<decimal> GetTotalThroughMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken) =>
        await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth <= accountingMonth)
            .SumAsync(accrual => accrual.Amount, cancellationToken);

    public void Add(Accrual accrual) => dbContext.Accruals.Add(accrual);

    private IQueryable<Accrual> QueryActive() =>
        dbContext.Accruals.AsNoTracking()
            .Include(accrual => accrual.Garage)
            .ThenInclude(garage => garage.Owner)
            .Include(accrual => accrual.IncomeType)
            .Include(accrual => accrual.IrregularPayment)
            .Include(accrual => accrual.FeeCampaign)
            .Where(accrual => !accrual.IsCanceled);

    private IQueryable<Accrual> TrackedAggregate() =>
        dbContext.Accruals
            .Include(accrual => accrual.Garage)
            .ThenInclude(garage => garage.Owner)
            .Include(accrual => accrual.IncomeType)
            .Include(accrual => accrual.IrregularPayment)
            .Include(accrual => accrual.FeeCampaign);

    private static IQueryable<Accrual> ApplyPeriod(IQueryable<Accrual> query, DateOnly? monthFrom, DateOnly? monthTo)
    {
        if (monthFrom.HasValue)
        {
            query = query.Where(accrual => accrual.AccountingMonth >= monthFrom.Value);
        }

        if (monthTo.HasValue)
        {
            query = query.Where(accrual => accrual.AccountingMonth <= monthTo.Value);
        }

        return query;
    }

    private static IQueryable<Accrual> ApplySearch(IQueryable<Accrual> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        return query.Where(accrual =>
            accrual.Garage.Number.ToLower().Contains(normalizedSearch) ||
            accrual.IncomeType.Name.ToLower().Contains(normalizedSearch) ||
            (accrual.IrregularPayment != null && accrual.IrregularPayment.Name.ToLower().Contains(normalizedSearch)) ||
            (accrual.FeeCampaign != null && accrual.FeeCampaign.Name.ToLower().Contains(normalizedSearch)) ||
            (accrual.Comment != null && accrual.Comment.ToLower().Contains(normalizedSearch)));
    }

    private static IOrderedQueryable<Accrual> Order(IQueryable<Accrual> query) =>
        query.OrderByDescending(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Garage.Number);

    private static bool AccrualMatchesSearch(Accrual accrual, string normalizedSearch) =>
        accrual.Garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        accrual.IncomeType.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (accrual.IrregularPayment?.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (accrual.FeeCampaign?.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (accrual.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
