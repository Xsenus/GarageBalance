using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfAccrualRepository(GarageBalanceDbContext dbContext) : IAccrualRepository
{
    public async Task<IrregularAccrualPaymentState?> GetIrregularPaymentStateAsync(
        Guid garageId,
        Guid irregularPaymentId,
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.GarageId == garageId &&
                accrual.IrregularPaymentId == irregularPaymentId &&
                accrual.AccountingMonth == accountingMonth)
            .Select(accrual => new
            {
                IsAvailable = accrual.IrregularPayment != null &&
                    accrual.IrregularPayment.IsActive &&
                    !accrual.IrregularPayment.IsArchived,
                accrual.Amount,
                PaidAmount = dbContext.AccrualPaymentAllocations
                    .Where(allocation =>
                        allocation.IsActive &&
                        allocation.AccrualId == accrual.Id &&
                        !allocation.FinancialOperation.IsCanceled)
                    .Sum(allocation => (decimal?)allocation.Amount) ?? 0m
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new IrregularAccrualPaymentState(
                row.IsAvailable,
                row.Amount,
                row.PaidAmount,
                Math.Max(row.Amount - row.PaidAmount, 0m));
    }

    public async Task<IReadOnlyList<Accrual>> GetListAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyPeriod(QueryActiveList(), monthFrom, monthTo);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            return (await ReadCompactListAsync(Order(query), cancellationToken))
                .Where(accrual => AccrualMatchesSearch(accrual, normalizedSearch))
                .Take(limit)
                .ToList();
        }

        return await ReadCompactListAsync(
            Order(ApplySearch(query, normalizedSearch)).Take(limit),
            cancellationToken);
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
                accrual.Basis,
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
                Basis = (string?)null,
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
                Basis = row.Basis,
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
            .Where(accrual => !accrual.IsCanceled && accrual.DueDateNeedsReview);
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresDueDateReviewPageAsync(query, offset, limit, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(accrual => accrual.Garage)
            .Include(accrual => accrual.IncomeType)
            .OrderBy(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Garage.Number)
            .ThenBy(accrual => accrual.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new AccrualPageData(items, totalCount);
    }

    private async Task<AccrualPageData> GetPostgresDueDateReviewPageAsync(
        IQueryable<Accrual> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = query
            .OrderBy(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Garage.Number)
            .ThenBy(accrual => accrual.Id)
            .Skip(offset)
            .Take(limit)
            .Select(accrual => new
            {
                Category = PageCategory,
                Id = (Guid?)accrual.Id,
                GarageId = (Guid?)accrual.GarageId,
                GarageNumber = (string?)accrual.Garage.Number,
                IncomeTypeId = (Guid?)accrual.IncomeTypeId,
                IncomeTypeName = (string?)accrual.IncomeType.Name,
                AccountingMonth = (DateOnly?)accrual.AccountingMonth,
                Amount = (decimal?)accrual.Amount,
                Source = (string?)accrual.Source,
                DueDate = (DateOnly?)accrual.DueDate,
                OverdueFromDate = (DateOnly?)accrual.OverdueFromDate,
                accrual.DueDateReviewReason,
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
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = (string?)null,
                AccountingMonth = (DateOnly?)null,
                Amount = (decimal?)null,
                Source = (string?)null,
                DueDate = (DateOnly?)null,
                OverdueFromDate = (DateOnly?)null,
                DueDateReviewReason = (string?)null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenBy(row => row.AccountingMonth)
            .ThenBy(row => row.GarageNumber)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new Accrual
            {
                Id = row.Id!.Value,
                GarageId = row.GarageId!.Value,
                Garage = new Garage { Id = row.GarageId.Value, Number = row.GarageNumber! },
                IncomeTypeId = row.IncomeTypeId!.Value,
                IncomeType = new IncomeType { Id = row.IncomeTypeId.Value, Name = row.IncomeTypeName! },
                AccountingMonth = row.AccountingMonth!.Value,
                Amount = row.Amount!.Value,
                Source = row.Source!,
                DueDate = row.DueDate!.Value,
                OverdueFromDate = row.OverdueFromDate!.Value,
                DueDateNeedsReview = true,
                DueDateReviewReason = row.DueDateReviewReason
            })
            .ToList();
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

    public async Task<IReadOnlyList<OutstandingAccrualDebtData>> GetOutstandingDebtDetailsAsync(
        Guid garageId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId)
            .Select(accrual => new
            {
                AccrualId = accrual.Id,
                accrual.IncomeTypeId,
                IncomeTypeName = accrual.IncomeType.Name,
                accrual.AccountingMonth,
                accrual.DueDate,
                accrual.Amount,
                accrual.FeeCampaignId,
                accrual.IrregularPaymentId,
                PaidAmount = dbContext.AccrualPaymentAllocations
                    .Where(allocation =>
                        allocation.IsActive &&
                        allocation.AccrualId == accrual.Id &&
                        !allocation.FinancialOperation.IsCanceled)
                    .Sum(allocation => (decimal?)allocation.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new OutstandingAccrualDebtData(
                row.AccrualId,
                row.IncomeTypeId,
                row.IncomeTypeName,
                row.AccountingMonth,
                row.DueDate,
                row.Amount,
                row.PaidAmount,
                Math.Max(row.Amount - row.PaidAmount, 0m),
                Math.Max(row.PaidAmount - row.Amount, 0m),
                row.FeeCampaignId,
                row.IrregularPaymentId))
            .Where(row => row.OutstandingAmount > 0m || row.ExcessPaidAmount > 0m)
            .OrderBy(row => row.AccountingMonth)
            .ThenBy(row => row.DueDate)
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

    public Task<Guid?> GetFeeCampaignIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Accruals.AsNoTracking()
            .Where(accrual => accrual.Id == id)
            .Select(accrual => accrual.FeeCampaignId)
            .SingleOrDefaultAsync(cancellationToken);

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

    public async Task<IReadOnlyList<Accrual>> GetActiveRegularForGarageForUpdateAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken) =>
        await TrackedAggregate()
            .Include(accrual => accrual.Tariff)
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.GarageId == garageId &&
                accrual.Source == AccrualSources.Regular &&
                ((accrual.AccountingMonth >= monthFrom && accrual.AccountingMonth <= monthTo) ||
                 (accrual.AccountingYear.HasValue &&
                  accrual.AccountingYear.Value >= monthFrom.Year &&
                  accrual.AccountingYear.Value <= monthTo.Year)))
            .OrderBy(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.IncomeTypeId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Accrual>> GetActiveRegularForRecalculationAsync(
        Guid incomeTypeId,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        await TrackedAggregate()
            .Include(accrual => accrual.Tariff)
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.Source == AccrualSources.Regular &&
                accrual.IncomeTypeId == incomeTypeId &&
                accrual.AccountingMonth == accountingMonth &&
                accrual.FeeCampaignId == null &&
                accrual.IrregularPaymentId == null &&
                accrual.Basis == null)
            .OrderBy(accrual => accrual.Garage.Number)
            .ThenBy(accrual => accrual.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetActiveRegularIncomeTypeIdsAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken) =>
        await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.GarageId == garageId &&
                accrual.Source == AccrualSources.Regular &&
                ((accrual.AccountingMonth >= monthFrom && accrual.AccountingMonth <= monthTo) ||
                 (accrual.AccountingYear.HasValue &&
                  accrual.AccountingYear.Value >= monthFrom.Year &&
                  accrual.AccountingYear.Value <= monthTo.Year)))
            .Select(accrual => accrual.IncomeTypeId)
            .ToHashSetAsync(cancellationToken);

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
                ((accrual.RequiresMeterReading && accrual.CalculationMeterKind == meterKind) ||
                 (!accrual.RequiresMeterReading && accrual.Tariff != null && accrual.Tariff.CalculationBase == calculationBase)))
            .OrderBy(accrual => accrual.IncomeTypeId)
            .ThenBy(accrual => accrual.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Accrual>> GetActiveMeteredFromForUpdateAsync(
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
                accrual.AccountingMonth >= accountingMonth &&
                accrual.Source == AccrualSources.Regular &&
                ((accrual.RequiresMeterReading && accrual.CalculationMeterKind == meterKind) ||
                 (!accrual.RequiresMeterReading && accrual.Tariff != null && accrual.Tariff.CalculationBase == calculationBase)))
            .OrderBy(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.IncomeTypeId)
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
        CancellationToken cancellationToken) =>
        await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.FeeCampaignId == feeCampaignId)
            .Select(accrual => accrual.GarageId)
            .ToHashSetAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetFullyPaidFeeCampaignGarageIdsBeforeMonthAsync(
        Guid feeCampaignId,
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        var accruedByGarage = await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.FeeCampaignId == feeCampaignId &&
                accrual.AccountingMonth < accountingMonth)
            .GroupBy(accrual => accrual.GarageId)
            .Select(group => new
            {
                GarageId = group.Key,
                Amount = group.Sum(accrual => accrual.Amount)
            })
            .ToDictionaryAsync(item => item.GarageId, item => item.Amount, cancellationToken);
        if (accruedByGarage.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var paidByGarage = await dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(allocation =>
                allocation.IsActive &&
                !allocation.Accrual.IsCanceled &&
                allocation.Accrual.FeeCampaignId == feeCampaignId &&
                allocation.Accrual.AccountingMonth < accountingMonth)
            .GroupBy(allocation => allocation.Accrual.GarageId)
            .Select(group => new
            {
                GarageId = group.Key,
                Amount = group.Sum(allocation => allocation.Amount)
            })
            .ToDictionaryAsync(item => item.GarageId, item => item.Amount, cancellationToken);

        return accruedByGarage
            .Where(item => paidByGarage.GetValueOrDefault(item.Key) >= item.Value)
            .Select(item => item.Key)
            .ToHashSet();
    }

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
                (accrual.AccountingYear == accountingYear ||
                 (!accrual.AccountingYear.HasValue && accrual.AccountingMonth.Year == accountingYear)) &&
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
                (accrual.AccountingYear == accountingYear ||
                 (!accrual.AccountingYear.HasValue && accrual.AccountingMonth.Year == accountingYear)) &&
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
            ? query.AnyAsync(
                accrual => accrual.AccountingYear == accountingYear.Value ||
                    (!accrual.AccountingYear.HasValue && accrual.AccountingMonth.Year == accountingYear.Value),
                cancellationToken)
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
        CancellationToken cancellationToken) =>
        dbContext.Accruals.AsNoTracking().AnyAsync(accrual =>
            !accrual.IsCanceled &&
            (!ignoredId.HasValue || accrual.Id != ignoredId.Value) &&
            accrual.GarageId == garageId &&
            accrual.FeeCampaignId == feeCampaignId,
            cancellationToken);

    public async Task<decimal> GetTotalThroughMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken) =>
        await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth <= accountingMonth)
            .SumAsync(accrual => accrual.Amount, cancellationToken);

    public void Add(Accrual accrual) => dbContext.Accruals.Add(accrual);

    private IQueryable<Accrual> QueryActiveList() =>
        dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled);

    private static async Task<IReadOnlyList<Accrual>> ReadCompactListAsync(
        IQueryable<Accrual> query,
        CancellationToken cancellationToken)
    {
        var rows = await query
            .Select(accrual => new AccrualListRow(
                accrual.Id,
                accrual.GarageId,
                accrual.Garage.Number,
                accrual.Garage.OwnerId,
                accrual.Garage.Owner == null ? null : accrual.Garage.Owner.LastName,
                accrual.Garage.Owner == null ? null : accrual.Garage.Owner.FirstName,
                accrual.Garage.Owner == null ? null : accrual.Garage.Owner.MiddleName,
                accrual.IncomeTypeId,
                accrual.IncomeType.Name,
                accrual.IrregularPaymentId,
                accrual.IrregularPayment == null ? null : accrual.IrregularPayment.Name,
                accrual.Basis,
                accrual.FeeCampaignId,
                accrual.FeeCampaign == null ? null : accrual.FeeCampaign.Name,
                accrual.AccountingMonth,
                accrual.AccountingYear,
                accrual.DueDate,
                accrual.OverdueFromDate,
                accrual.Amount,
                accrual.Source,
                accrual.Comment,
                accrual.IsCanceled))
            .ToListAsync(cancellationToken);

        return rows.Select(CreateCompactAccrual).ToList();
    }

    private static Accrual CreateCompactAccrual(AccrualListRow row) =>
        new()
        {
            Id = row.Id,
            GarageId = row.GarageId,
            Garage = new Garage
            {
                Id = row.GarageId,
                Number = row.GarageNumber,
                OwnerId = row.OwnerId,
                Owner = row.OwnerId is null
                    ? null
                    : new Owner
                    {
                        Id = row.OwnerId.Value,
                        LastName = row.OwnerLastName ?? string.Empty,
                        FirstName = row.OwnerFirstName ?? string.Empty,
                        MiddleName = row.OwnerMiddleName
                    }
            },
            IncomeTypeId = row.IncomeTypeId,
            IncomeType = new IncomeType { Id = row.IncomeTypeId, Name = row.IncomeTypeName },
            IrregularPaymentId = row.IrregularPaymentId,
            IrregularPayment = row.IrregularPaymentId is null
                ? null
                : new IrregularPayment
                {
                    Id = row.IrregularPaymentId.Value,
                    Name = row.IrregularPaymentName ?? string.Empty
                },
            Basis = row.Basis,
            FeeCampaignId = row.FeeCampaignId,
            FeeCampaign = row.FeeCampaignId is null
                ? null
                : new FeeCampaign
                {
                    Id = row.FeeCampaignId.Value,
                    Name = row.FeeCampaignName ?? string.Empty
                },
            AccountingMonth = row.AccountingMonth,
            AccountingYear = row.AccountingYear,
            DueDate = row.DueDate,
            OverdueFromDate = row.OverdueFromDate,
            Amount = row.Amount,
            Source = row.Source,
            Comment = row.Comment,
            IsCanceled = row.IsCanceled
        };

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

    private sealed record AccrualListRow(
        Guid Id,
        Guid GarageId,
        string GarageNumber,
        Guid? OwnerId,
        string? OwnerLastName,
        string? OwnerFirstName,
        string? OwnerMiddleName,
        Guid IncomeTypeId,
        string IncomeTypeName,
        Guid? IrregularPaymentId,
        string? IrregularPaymentName,
        string? Basis,
        Guid? FeeCampaignId,
        string? FeeCampaignName,
        DateOnly AccountingMonth,
        int? AccountingYear,
        DateOnly DueDate,
        DateOnly OverdueFromDate,
        decimal Amount,
        string Source,
        string? Comment,
        bool IsCanceled);

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

    private IQueryable<Accrual> ApplySearch(IQueryable<Accrual> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
        var candidates = dbContext.Accruals.AsNoTracking();
        var matchingIds = candidates
            .Where(accrual => EF.Functions.ILike(accrual.Garage.Number, pattern, @"\"))
            .Select(accrual => accrual.Id)
            .Concat(candidates
                .Where(accrual => EF.Functions.ILike(accrual.IncomeType.Name, pattern, @"\"))
                .Select(accrual => accrual.Id))
            .Concat(candidates
                .Where(accrual => accrual.IrregularPayment != null && EF.Functions.ILike(accrual.IrregularPayment.Name, pattern, @"\"))
                .Select(accrual => accrual.Id))
            .Concat(candidates
                .Where(accrual => accrual.Basis != null && EF.Functions.ILike(accrual.Basis, pattern, @"\"))
                .Select(accrual => accrual.Id))
            .Concat(candidates
                .Where(accrual => accrual.FeeCampaign != null && EF.Functions.ILike(accrual.FeeCampaign.Name, pattern, @"\"))
                .Select(accrual => accrual.Id))
            .Concat(candidates
                .Where(accrual => accrual.Comment != null && EF.Functions.ILike(accrual.Comment, pattern, @"\"))
                .Select(accrual => accrual.Id));
        return query.Where(accrual => matchingIds.Contains(accrual.Id));
    }

    private static IOrderedQueryable<Accrual> Order(IQueryable<Accrual> query) =>
        query.OrderByDescending(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Garage.Number);

    private static bool AccrualMatchesSearch(Accrual accrual, string normalizedSearch) =>
        accrual.Garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        accrual.IncomeType.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (accrual.IrregularPayment?.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (accrual.Basis?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (accrual.FeeCampaign?.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (accrual.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
