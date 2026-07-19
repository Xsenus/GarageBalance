using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfIncomeReportQuery(GarageBalanceDbContext dbContext) : IIncomeReportQuery
{
    private const int StartingBalanceDebtCategory = 1;
    private const int AccrualDebtCategory = 2;
    private const int PaymentDebtCategory = 3;
    private const int StartingBalanceTotalCategory = 4;
    private const int AccrualTotalCategory = 5;
    private const int IncomeTotalCategory = 6;
    private const string AllRows = "all";
    private const string AccrualRows = "accruals";
    private const string PaymentRows = "payments";
    private const string StartingBalanceRows = "starting_balance";

    public Task<IncomeReportQueryData> GetRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> garageIds,
        IReadOnlySet<Guid> ownerIds,
        IReadOnlySet<Guid> incomeTypeIds,
        string? search,
        int? limit,
        int offset,
        CancellationToken cancellationToken) =>
        GetRowsAsync(dateFrom, dateTo, rowMode, garageIds, ownerIds, incomeTypeIds, search, limit, offset, new ReportSort("date", false), cancellationToken);

    public async Task<IncomeReportQueryData> GetRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> garageIds,
        IReadOnlySet<Guid> ownerIds,
        IReadOnlySet<Guid> incomeTypeIds,
        string? search,
        int? limit,
        int offset,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        if (IsNpgsql())
        {
            return await GetPostgresRowsAsync(
                dateFrom,
                dateTo,
                rowMode,
                garageIds,
                ownerIds,
                incomeTypeIds,
                search,
                limit,
                offset,
                sort,
                cancellationToken);
        }

        var rows = new List<IncomeReportRowDto>();
        var accrualTotal = 0m;
        var incomeTotal = 0m;
        var rowCount = 0;
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var normalizedSearch = search?.Trim().ToLowerInvariant();
        var useClientSearch = hasSearch && !(dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false);
        var fetchLimit = useClientSearch ? null : GetFetchLimit(offset, limit);
        var aggregateQuery = dbContext.Accruals.AsNoTracking()
            .Where(_ => false)
            .Select(_ => new { Category = 0, Total = 0m, Count = 0 });
        var hasAggregateQueries = false;

        if (rowMode is AllRows or AccrualRows)
        {
            if (incomeTypeIds.Count == 0)
            {
                var startingBalanceQuery = dbContext.Garages.AsNoTracking()
                    .Include(garage => garage.Owner)
                    .Where(garage => !garage.IsArchived && garage.StartingBalance != 0);
                if (garageIds.Count > 0)
                {
                    startingBalanceQuery = startingBalanceQuery.Where(garage => garageIds.Contains(garage.Id));
                }

                if (ownerIds.Count > 0)
                {
                    startingBalanceQuery = startingBalanceQuery.Where(garage => garage.OwnerId != null && ownerIds.Contains(garage.OwnerId.Value));
                }

                if (hasSearch && !useClientSearch && !"Стартовый баланс".Contains(normalizedSearch!, StringComparison.OrdinalIgnoreCase))
                {
                    startingBalanceQuery = startingBalanceQuery.Where(garage =>
                        garage.Number.ToLower().Contains(normalizedSearch!) ||
                        (garage.Owner != null && (
                            garage.Owner.LastName.ToLower().Contains(normalizedSearch!) ||
                            garage.Owner.FirstName.ToLower().Contains(normalizedSearch!) ||
                            (garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(normalizedSearch!)) ||
                            (garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedSearch!))));
                }

                if (!useClientSearch)
                {
                    var startingBalanceAggregate = startingBalanceQuery
                        .GroupBy(_ => 1)
                        .Select(group => new
                        {
                            Category = StartingBalanceTotalCategory,
                            Total = group.Sum(garage => garage.StartingBalance),
                            Count = group.Count()
                        });
                    aggregateQuery = aggregateQuery.Concat(startingBalanceAggregate);
                    hasAggregateQueries = true;
                }

                var startingBalances = await ApplyLimit(startingBalanceQuery.OrderBy(garage => garage.Number).ThenBy(garage => garage.Id), fetchLimit)
                    .ToListAsync(cancellationToken);
                rows.AddRange(startingBalances.Select(garage => new IncomeReportRowDto(
                    StartingBalanceRows,
                    dateFrom,
                    dateFrom,
                    garage.Id,
                    garage.Number,
                    garage.OwnerId,
                    garage.Owner?.FullName,
                    Guid.Empty,
                    "Стартовый баланс",
                    garage.StartingBalance,
                    0m,
                    garage.StartingBalance,
                    null,
                    "Начальная задолженность гаража")));
            }

            var accrualsQuery = dbContext.Accruals.AsNoTracking()
                .Include(accrual => accrual.Garage)
                .ThenInclude(garage => garage.Owner)
                .Include(accrual => accrual.IncomeType)
                .Where(accrual => !accrual.IsCanceled && accrual.AccountingMonth >= dateFrom && accrual.AccountingMonth <= dateTo);
            if (garageIds.Count > 0)
            {
                accrualsQuery = accrualsQuery.Where(accrual => garageIds.Contains(accrual.GarageId));
            }

            if (ownerIds.Count > 0)
            {
                accrualsQuery = accrualsQuery.Where(accrual => accrual.Garage.OwnerId != null && ownerIds.Contains(accrual.Garage.OwnerId.Value));
            }

            if (incomeTypeIds.Count > 0)
            {
                accrualsQuery = accrualsQuery.Where(accrual => incomeTypeIds.Contains(accrual.IncomeTypeId));
            }

            if (hasSearch && !useClientSearch)
            {
                accrualsQuery = accrualsQuery.Where(accrual =>
                    accrual.Garage.Number.ToLower().Contains(normalizedSearch!) ||
                    (accrual.Garage.Owner != null && (
                        accrual.Garage.Owner.LastName.ToLower().Contains(normalizedSearch!) ||
                        accrual.Garage.Owner.FirstName.ToLower().Contains(normalizedSearch!) ||
                        (accrual.Garage.Owner.MiddleName != null && accrual.Garage.Owner.MiddleName.ToLower().Contains(normalizedSearch!)) ||
                        (accrual.Garage.Owner.LastName + " " + accrual.Garage.Owner.FirstName + " " + (accrual.Garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedSearch!))) ||
                    accrual.IncomeType.Name.ToLower().Contains(normalizedSearch!));
            }

            if (!useClientSearch)
            {
                var accrualAggregate = accrualsQuery
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Category = AccrualTotalCategory,
                        Total = group.Sum(accrual => accrual.Amount),
                        Count = group.Count()
                    });
                aggregateQuery = aggregateQuery.Concat(accrualAggregate);
                hasAggregateQueries = true;
            }

            var accruals = await ApplyLimit(
                    accrualsQuery.OrderBy(accrual => accrual.AccountingMonth).ThenBy(accrual => accrual.Garage.Number).ThenBy(accrual => accrual.Id),
                    fetchLimit)
                .ToListAsync(cancellationToken);
            rows.AddRange(accruals.Select(accrual => new IncomeReportRowDto(
                AccrualRows,
                accrual.AccountingMonth,
                accrual.AccountingMonth,
                accrual.GarageId,
                accrual.Garage.Number,
                accrual.Garage.OwnerId,
                accrual.Garage.Owner?.FullName,
                accrual.IncomeTypeId,
                accrual.IncomeType.Name,
                accrual.Amount,
                0m,
                accrual.Amount,
                null,
                accrual.Comment)));
        }

        if (rowMode is AllRows or PaymentRows)
        {
            var paymentsQuery = dbContext.FinancialOperations.AsNoTracking()
                .Include(operation => operation.Garage)
                .ThenInclude(garage => garage!.Owner)
                .Include(operation => operation.IncomeType)
                .Where(operation =>
                    !operation.IsCanceled &&
                    operation.OperationKind == FinancialOperationKinds.Income &&
                    operation.GarageId != null &&
                    operation.IncomeTypeId != null &&
                    operation.OperationDate >= dateFrom &&
                    operation.OperationDate <= dateTo);
            if (garageIds.Count > 0)
            {
                paymentsQuery = paymentsQuery.Where(operation => garageIds.Contains(operation.GarageId!.Value));
            }

            if (ownerIds.Count > 0)
            {
                paymentsQuery = paymentsQuery.Where(operation => operation.Garage!.OwnerId != null && ownerIds.Contains(operation.Garage.OwnerId.Value));
            }

            if (incomeTypeIds.Count > 0)
            {
                paymentsQuery = paymentsQuery.Where(operation => incomeTypeIds.Contains(operation.IncomeTypeId!.Value));
            }

            if (hasSearch && !useClientSearch)
            {
                paymentsQuery = paymentsQuery.Where(operation =>
                    operation.Garage!.Number.ToLower().Contains(normalizedSearch!) ||
                    (operation.Garage.Owner != null && (
                        operation.Garage.Owner.LastName.ToLower().Contains(normalizedSearch!) ||
                        operation.Garage.Owner.FirstName.ToLower().Contains(normalizedSearch!) ||
                        (operation.Garage.Owner.MiddleName != null && operation.Garage.Owner.MiddleName.ToLower().Contains(normalizedSearch!)) ||
                        (operation.Garage.Owner.LastName + " " + operation.Garage.Owner.FirstName + " " + (operation.Garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedSearch!))) ||
                    operation.IncomeType!.Name.ToLower().Contains(normalizedSearch!) ||
                    (operation.DocumentNumber != null && operation.DocumentNumber.ToLower().Contains(normalizedSearch!)));
            }

            if (!useClientSearch)
            {
                var incomeAggregate = paymentsQuery
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Category = IncomeTotalCategory,
                        Total = group.Sum(operation => operation.Amount),
                        Count = group.Count()
                    });
                aggregateQuery = aggregateQuery.Concat(incomeAggregate);
                hasAggregateQueries = true;
            }

            var payments = await ApplyLimit(
                    paymentsQuery.OrderBy(operation => operation.OperationDate).ThenBy(operation => operation.Garage!.Number).ThenBy(operation => operation.Id),
                    fetchLimit)
                .ToListAsync(cancellationToken);
            var debtAfterPayments = await CalculateDebtAfterPaymentsAsync(
                payments.Select(operation => new IncomeDebtTarget(
                    operation.Id,
                    operation.GarageId!.Value,
                    operation.AccountingMonth,
                    operation.OperationDate)).ToList(),
                cancellationToken);
            rows.AddRange(payments.Select(operation => new IncomeReportRowDto(
                PaymentRows,
                operation.OperationDate,
                operation.AccountingMonth,
                operation.GarageId!.Value,
                operation.Garage!.Number,
                operation.Garage.OwnerId,
                operation.Garage.Owner?.FullName,
                operation.IncomeTypeId!.Value,
                operation.IncomeType!.Name,
                0m,
                operation.Amount,
                -operation.Amount,
                operation.DocumentNumber,
                operation.Comment,
                operation.CreatedAtUtc,
                debtAfterPayments.GetValueOrDefault(operation.Id))));
        }

        if (!useClientSearch && hasAggregateQueries)
        {
            var aggregates = await aggregateQuery.ToListAsync(cancellationToken);
            accrualTotal = aggregates
                .Where(row => row.Category is StartingBalanceTotalCategory or AccrualTotalCategory)
                .Sum(row => row.Total);
            incomeTotal = aggregates
                .Where(row => row.Category == IncomeTotalCategory)
                .Sum(row => row.Total);
            rowCount = aggregates.Sum(row => row.Count);
        }
        else if (useClientSearch)
        {
            var clientSearch = search!.Trim();
            rows = rows.Where(row =>
                    row.GarageNumber.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ||
                    (row.OwnerName?.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    row.IncomeTypeName.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ||
                    (row.DocumentNumber?.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            accrualTotal = rows.Sum(row => row.AccrualAmount);
            incomeTotal = rows.Sum(row => row.IncomeAmount);
            rowCount = rows.Count;
        }

        var visibleRows = ApplyPage(
                ApplySort(rows, sort)
                    .ThenByDescending(row => row.CreatedAtUtc)
                    .ThenBy(row => row.GarageNumber, StringComparer.Ordinal)
                    .ThenBy(row => row.GarageId),
                offset,
                limit)
            .ToList();
        return new IncomeReportQueryData(accrualTotal, incomeTotal, rowCount, visibleRows);
    }

    private async Task<IncomeReportQueryData> GetPostgresRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> garageIds,
        IReadOnlySet<Guid> ownerIds,
        IReadOnlySet<Guid> incomeTypeIds,
        string? search,
        int? limit,
        int offset,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        if (rowMode == PaymentRows)
        {
            return await GetPostgresPaymentRowsAsync(
                dateFrom,
                dateTo,
                garageIds,
                ownerIds,
                incomeTypeIds,
                search,
                limit,
                offset,
                sort,
                cancellationToken);
        }

        var normalizedSearch = search?.Trim().ToLowerInvariant();
        var hasSearch = !string.IsNullOrWhiteSpace(normalizedSearch);
        IQueryable<IncomeReportProjection>? reportRows = null;
        var aggregateQuery = dbContext.Accruals.AsNoTracking()
            .Where(_ => false)
            .Select(_ => new { Category = 0, Total = 0m, Count = 0 });

        if (rowMode is AllRows or AccrualRows)
        {
            if (incomeTypeIds.Count == 0)
            {
                var startingBalances = dbContext.Garages.AsNoTracking()
                    .Where(garage => !garage.IsArchived && garage.StartingBalance != 0);
                if (garageIds.Count > 0)
                {
                    startingBalances = startingBalances.Where(garage => garageIds.Contains(garage.Id));
                }

                if (ownerIds.Count > 0)
                {
                    startingBalances = startingBalances.Where(garage => garage.OwnerId != null && ownerIds.Contains(garage.OwnerId.Value));
                }

                if (hasSearch && !"Стартовый баланс".Contains(search!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    startingBalances = startingBalances.Where(garage =>
                        garage.Number.ToLower().Contains(normalizedSearch!) ||
                        (garage.Owner != null && (
                            garage.Owner.LastName.ToLower().Contains(normalizedSearch!) ||
                            garage.Owner.FirstName.ToLower().Contains(normalizedSearch!) ||
                            (garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(normalizedSearch!)) ||
                            (garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedSearch!))));
                }

                aggregateQuery = aggregateQuery.Concat(startingBalances
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Category = StartingBalanceTotalCategory,
                        Total = group.Sum(garage => garage.StartingBalance),
                        Count = group.Count()
                    }));

                reportRows = startingBalances.Select(garage => new IncomeReportProjection
                {
                    RowType = StartingBalanceRows,
                    Date = dateFrom,
                    AccountingMonth = dateFrom,
                    GarageId = garage.Id,
                    GarageNumber = garage.Number,
                    OwnerId = garage.OwnerId,
                    OwnerName = garage.Owner == null ? null : garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty),
                    IncomeTypeId = Guid.Empty,
                    IncomeTypeName = "Стартовый баланс",
                    AccrualAmount = garage.StartingBalance,
                    IncomeAmount = 0m,
                    Debt = garage.StartingBalance,
                    DocumentNumber = null,
                    Comment = "Начальная задолженность гаража",
                    CreatedAtUtc = garage.CreatedAtUtc,
                    RowId = garage.Id,
                    PaymentOperationId = null
                });
            }

            var accruals = dbContext.Accruals.AsNoTracking()
                .Where(accrual => !accrual.IsCanceled && accrual.AccountingMonth >= dateFrom && accrual.AccountingMonth <= dateTo);
            if (garageIds.Count > 0)
            {
                accruals = accruals.Where(accrual => garageIds.Contains(accrual.GarageId));
            }

            if (ownerIds.Count > 0)
            {
                accruals = accruals.Where(accrual => accrual.Garage.OwnerId != null && ownerIds.Contains(accrual.Garage.OwnerId.Value));
            }

            if (incomeTypeIds.Count > 0)
            {
                accruals = accruals.Where(accrual => incomeTypeIds.Contains(accrual.IncomeTypeId));
            }

            if (hasSearch)
            {
                accruals = accruals.Where(accrual =>
                    accrual.Garage.Number.ToLower().Contains(normalizedSearch!) ||
                    (accrual.Garage.Owner != null && (
                        accrual.Garage.Owner.LastName.ToLower().Contains(normalizedSearch!) ||
                        accrual.Garage.Owner.FirstName.ToLower().Contains(normalizedSearch!) ||
                        (accrual.Garage.Owner.MiddleName != null && accrual.Garage.Owner.MiddleName.ToLower().Contains(normalizedSearch!)) ||
                        (accrual.Garage.Owner.LastName + " " + accrual.Garage.Owner.FirstName + " " + (accrual.Garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedSearch!))) ||
                    accrual.IncomeType.Name.ToLower().Contains(normalizedSearch!));
            }

            aggregateQuery = aggregateQuery.Concat(accruals
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Category = AccrualTotalCategory,
                    Total = group.Sum(accrual => accrual.Amount),
                    Count = group.Count()
                }));

            var projectedAccruals = accruals.Select(accrual => new IncomeReportProjection
            {
                RowType = AccrualRows,
                Date = accrual.AccountingMonth,
                AccountingMonth = accrual.AccountingMonth,
                GarageId = accrual.GarageId,
                GarageNumber = accrual.Garage.Number,
                OwnerId = accrual.Garage.OwnerId,
                OwnerName = accrual.Garage.Owner == null ? null : accrual.Garage.Owner.LastName + " " + accrual.Garage.Owner.FirstName + " " + (accrual.Garage.Owner.MiddleName ?? string.Empty),
                IncomeTypeId = accrual.IncomeTypeId,
                IncomeTypeName = accrual.IncomeType.Name,
                AccrualAmount = accrual.Amount,
                IncomeAmount = 0m,
                Debt = accrual.Amount,
                DocumentNumber = null,
                Comment = accrual.Comment,
                CreatedAtUtc = accrual.CreatedAtUtc,
                RowId = accrual.Id,
                PaymentOperationId = null
            });
            reportRows = reportRows == null ? projectedAccruals : reportRows.Concat(projectedAccruals);
        }

        if (rowMode is AllRows or PaymentRows)
        {
            var payments = dbContext.FinancialOperations.AsNoTracking()
                .Where(operation =>
                    !operation.IsCanceled &&
                    operation.OperationKind == FinancialOperationKinds.Income &&
                    operation.GarageId != null &&
                    operation.IncomeTypeId != null &&
                    operation.OperationDate >= dateFrom &&
                    operation.OperationDate <= dateTo);
            if (garageIds.Count > 0)
            {
                payments = payments.Where(operation => garageIds.Contains(operation.GarageId!.Value));
            }

            if (ownerIds.Count > 0)
            {
                payments = payments.Where(operation => operation.Garage!.OwnerId != null && ownerIds.Contains(operation.Garage.OwnerId.Value));
            }

            if (incomeTypeIds.Count > 0)
            {
                payments = payments.Where(operation => incomeTypeIds.Contains(operation.IncomeTypeId!.Value));
            }

            if (hasSearch)
            {
                payments = payments.Where(operation =>
                    operation.Garage!.Number.ToLower().Contains(normalizedSearch!) ||
                    (operation.Garage.Owner != null && (
                        operation.Garage.Owner.LastName.ToLower().Contains(normalizedSearch!) ||
                        operation.Garage.Owner.FirstName.ToLower().Contains(normalizedSearch!) ||
                        (operation.Garage.Owner.MiddleName != null && operation.Garage.Owner.MiddleName.ToLower().Contains(normalizedSearch!)) ||
                        (operation.Garage.Owner.LastName + " " + operation.Garage.Owner.FirstName + " " + (operation.Garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedSearch!))) ||
                    operation.IncomeType!.Name.ToLower().Contains(normalizedSearch!) ||
                    (operation.DocumentNumber != null && operation.DocumentNumber.ToLower().Contains(normalizedSearch!)));
            }

            aggregateQuery = aggregateQuery.Concat(payments
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Category = IncomeTotalCategory,
                    Total = group.Sum(operation => operation.Amount),
                    Count = group.Count()
                }));

            var projectedPayments = payments.Select(operation => new IncomeReportProjection
            {
                RowType = PaymentRows,
                Date = operation.OperationDate,
                AccountingMonth = operation.AccountingMonth,
                GarageId = operation.GarageId!.Value,
                GarageNumber = operation.Garage!.Number,
                OwnerId = operation.Garage.OwnerId,
                OwnerName = operation.Garage.Owner == null ? null : operation.Garage.Owner.LastName + " " + operation.Garage.Owner.FirstName + " " + (operation.Garage.Owner.MiddleName ?? string.Empty),
                IncomeTypeId = operation.IncomeTypeId!.Value,
                IncomeTypeName = operation.IncomeType!.Name,
                AccrualAmount = 0m,
                IncomeAmount = operation.Amount,
                Debt = -operation.Amount,
                DocumentNumber = operation.DocumentNumber,
                Comment = operation.Comment,
                CreatedAtUtc = operation.CreatedAtUtc,
                RowId = operation.Id,
                PaymentOperationId = operation.Id
            });
            reportRows = reportRows == null ? projectedPayments : reportRows.Concat(projectedPayments);
        }

        if (reportRows == null)
        {
            return new IncomeReportQueryData(0m, 0m, 0, []);
        }

        var aggregates = await aggregateQuery.ToListAsync(cancellationToken);
        var rowCount = aggregates.Sum(row => row.Count);
        if (rowCount == 0)
        {
            return new IncomeReportQueryData(0m, 0m, 0, []);
        }
        var accrualTotal = aggregates.Where(row => row.Category is StartingBalanceTotalCategory or AccrualTotalCategory).Sum(row => row.Total);
        var incomeTotal = aggregates.Where(row => row.Category == IncomeTotalCategory).Sum(row => row.Total);

        var sortableRows = reportRows.Select(row => new IncomeReportSortableProjection
        {
            RowType = row.RowType,
            Date = row.Date,
            AccountingMonth = row.AccountingMonth,
            GarageId = row.GarageId,
            GarageNumber = row.GarageNumber,
            OwnerId = row.OwnerId,
            OwnerName = row.OwnerName,
            IncomeTypeId = row.IncomeTypeId,
            IncomeTypeName = row.IncomeTypeName,
            AccrualAmount = row.AccrualAmount,
            IncomeAmount = row.IncomeAmount,
            Debt = row.Debt,
            DocumentNumber = row.DocumentNumber,
            Comment = row.Comment,
            CreatedAtUtc = row.CreatedAtUtc,
            RowId = row.RowId,
            PaymentOperationId = row.PaymentOperationId
        });
        var ordered = ApplyPostgresSort(sortableRows, sort)
            .ThenByDescending(row => row.CreatedAtUtc)
            .ThenBy(row => row.GarageNumber)
            .ThenBy(row => row.RowId);
        IQueryable<IncomeReportSortableProjection> page = offset > 0 ? ordered.Skip(offset) : ordered;
        if (limit is > 0)
        {
            page = page.Take(limit.Value);
        }

        var projectedRows = await page.ToListAsync(cancellationToken);
        var visiblePaymentTargets = projectedRows
            .Where(row => row.PaymentOperationId.HasValue)
            .Select(row => new IncomeDebtTarget(
                row.PaymentOperationId!.Value,
                row.GarageId,
                row.AccountingMonth,
                row.Date))
            .ToList();
        var debtAfterPayments = await CalculateDebtAfterPaymentsAsync(visiblePaymentTargets, cancellationToken);
        var rows = projectedRows.Select(row => new IncomeReportRowDto(
                row.RowType,
                row.Date,
                row.AccountingMonth,
                row.GarageId,
                row.GarageNumber,
                row.OwnerId,
                row.OwnerName,
                row.IncomeTypeId,
                row.IncomeTypeName,
                row.AccrualAmount,
                row.IncomeAmount,
                row.Debt,
                row.DocumentNumber,
                row.Comment,
                row.CreatedAtUtc,
                row.PaymentOperationId.HasValue ? debtAfterPayments.GetValueOrDefault(row.PaymentOperationId.Value) : null))
            .ToList();
        return new IncomeReportQueryData(accrualTotal, incomeTotal, rowCount, rows);
    }

    private async Task<IncomeReportQueryData> GetPostgresPaymentRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        IReadOnlySet<Guid> garageIds,
        IReadOnlySet<Guid> ownerIds,
        IReadOnlySet<Guid> incomeTypeIds,
        string? search,
        int? limit,
        int offset,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var sortColumn = sort.Field switch
        {
            "accountingMonth" => "accounting_month",
            "garageNumber" => "garage_number",
            "ownerName" => "owner_name",
            "incomeTypeName" => "income_type_name",
            "accrualAmount" => "accrual_amount",
            "incomeAmount" => "income_amount",
            "debt" => "debt",
            "documentNumber" => "document_number",
            _ => "operation_date"
        };
        var direction = sort.Descending ? "DESC" : "ASC";
        var garageClause = garageIds.Count > 0 ? "AND operation.\"GarageId\" = ANY(@garage_ids)" : string.Empty;
        var ownerClause = ownerIds.Count > 0 ? "AND garage.\"OwnerId\" = ANY(@owner_ids)" : string.Empty;
        var incomeTypeClause = incomeTypeIds.Count > 0 ? "AND operation.\"IncomeTypeId\" = ANY(@income_type_ids)" : string.Empty;
        var searchClause = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : """
              AND (STRPOS(LOWER(garage."Number"), @search) > 0
                   OR STRPOS(LOWER(owner."LastName"), @search) > 0
                   OR STRPOS(LOWER(owner."FirstName"), @search) > 0
                   OR STRPOS(LOWER(owner."MiddleName"), @search) > 0
                   OR STRPOS(LOWER(owner."LastName" || ' ' || owner."FirstName" || ' ' || COALESCE(owner."MiddleName", '')), @search) > 0
                   OR STRPOS(LOWER(income_type."Name"), @search) > 0
                   OR STRPOS(LOWER(operation."DocumentNumber"), @search) > 0)
              """;
        var limitClause = limit is > 0 ? "LIMIT @limit" : string.Empty;
        var sql = $$"""
            WITH filtered_rows AS (
                SELECT operation."Id" AS id,
                       operation."OperationDate" AS operation_date,
                       operation."AccountingMonth" AS accounting_month,
                       operation."GarageId" AS garage_id,
                       garage."Number" AS garage_number,
                       garage."OwnerId" AS owner_id,
                       CASE WHEN owner."Id" IS NULL THEN NULL
                            ELSE owner."LastName" || ' ' || owner."FirstName" || ' ' || COALESCE(owner."MiddleName", '') END AS owner_name,
                       operation."IncomeTypeId" AS income_type_id,
                       income_type."Name" AS income_type_name,
                       0::numeric AS accrual_amount,
                       operation."Amount" AS income_amount,
                       -operation."Amount" AS debt,
                       operation."DocumentNumber" AS document_number,
                       operation."Comment" AS comment,
                       operation."CreatedAtUtc" AS created_at_utc
                FROM financial_operations operation
                INNER JOIN garages garage ON garage."Id" = operation."GarageId"
                LEFT JOIN owners owner ON owner."Id" = garage."OwnerId"
                INNER JOIN income_types income_type ON income_type."Id" = operation."IncomeTypeId"
                WHERE operation."IsCanceled" = FALSE
                  AND operation."OperationKind" = 'income'
                  AND operation."GarageId" IS NOT NULL
                  AND operation."IncomeTypeId" IS NOT NULL
                  AND operation."OperationDate" >= @date_from::date
                  AND operation."OperationDate" <= @date_to::date
                  {{garageClause}}
                  {{ownerClause}}
                  {{incomeTypeClause}}
                  {{searchClause}}
            ), page_rows AS (
                SELECT filtered_rows.*,
                       ROW_NUMBER() OVER (
                           ORDER BY {{sortColumn}} {{direction}}, created_at_utc DESC, garage_number, id)::int AS row_order
                FROM filtered_rows
                ORDER BY {{sortColumn}} {{direction}}, created_at_utc DESC, garage_number, id
                OFFSET @offset
                {{limitClause}}
            )
            SELECT {{PageCategory}} AS "Category", row_order AS "RowOrder", id AS "Id",
                   operation_date AS "OperationDate", accounting_month AS "AccountingMonth",
                   garage_id AS "GarageId", garage_number AS "GarageNumber", owner_id AS "OwnerId",
                   owner_name AS "OwnerName", income_type_id AS "IncomeTypeId", income_type_name AS "IncomeTypeName",
                   accrual_amount AS "AccrualAmount", income_amount AS "IncomeAmount", debt AS "Debt",
                   document_number AS "DocumentNumber", comment AS "Comment", created_at_utc AS "CreatedAtUtc",
                   0::numeric AS "IncomeTotal", 0 AS "RowCount"
            FROM page_rows
            UNION ALL
            SELECT {{TotalsCategory}}, 0, NULL::uuid, NULL::date, NULL::date, NULL::uuid, NULL::text,
                   NULL::uuid, NULL::text, NULL::uuid, NULL::text, 0::numeric, 0::numeric, 0::numeric,
                   NULL::text, NULL::text, NULL::timestamptz, COALESCE(SUM(income_amount), 0), COUNT(*)::int
            FROM filtered_rows
            ORDER BY "Category", "RowOrder"
            """;
        var parameters = new List<object>
        {
            new NpgsqlParameter<DateOnly>("date_from", dateFrom),
            new NpgsqlParameter<DateOnly>("date_to", dateTo),
            new NpgsqlParameter<int>("offset", offset)
        };
        if (garageIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("garage_ids", garageIds.ToArray()));
        }
        if (ownerIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("owner_ids", ownerIds.ToArray()));
        }
        if (incomeTypeIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("income_type_ids", incomeTypeIds.ToArray()));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add(new NpgsqlParameter<string>("search", search.Trim().ToLowerInvariant()));
        }
        if (limit is > 0)
        {
            parameters.Add(new NpgsqlParameter<int>("limit", limit.Value));
        }

        var combinedRows = await dbContext.Database
            .SqlQueryRaw<IncomePaymentCombinedQueryRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        var totals = combinedRows.Single(row => row.Category == TotalsCategory);
        var pageRows = combinedRows.Where(row => row.Category == PageCategory).ToList();
        var targets = pageRows.Select(row => new IncomeDebtTarget(
            row.Id!.Value,
            row.GarageId!.Value,
            row.AccountingMonth!.Value,
            row.OperationDate!.Value)).ToList();
        var debtAfterPayments = await CalculateDebtAfterPaymentsAsync(targets, cancellationToken);
        var rows = pageRows.Select(row => new IncomeReportRowDto(
            PaymentRows,
            row.OperationDate!.Value,
            row.AccountingMonth!.Value,
            row.GarageId!.Value,
            row.GarageNumber!,
            row.OwnerId,
            row.OwnerName,
            row.IncomeTypeId!.Value,
            row.IncomeTypeName!,
            row.AccrualAmount,
            row.IncomeAmount,
            row.Debt,
            row.DocumentNumber,
            row.Comment,
            row.CreatedAtUtc!.Value,
            debtAfterPayments.GetValueOrDefault(row.Id!.Value)))
            .ToList();
        return new IncomeReportQueryData(0m, totals.IncomeTotal, totals.RowCount, rows);
    }

    private static IOrderedQueryable<IncomeReportSortableProjection> ApplyPostgresSort(IQueryable<IncomeReportSortableProjection> rows, ReportSort sort) =>
        sort.Field switch
        {
            "accountingMonth" => sort.Descending ? rows.OrderByDescending(row => row.AccountingMonth) : rows.OrderBy(row => row.AccountingMonth),
            "garageNumber" => sort.Descending ? rows.OrderByDescending(row => row.GarageNumber) : rows.OrderBy(row => row.GarageNumber),
            "ownerName" => sort.Descending ? rows.OrderByDescending(row => row.OwnerName) : rows.OrderBy(row => row.OwnerName),
            "incomeTypeName" => sort.Descending ? rows.OrderByDescending(row => row.IncomeTypeName) : rows.OrderBy(row => row.IncomeTypeName),
            "accrualAmount" => sort.Descending ? rows.OrderByDescending(row => row.AccrualAmount) : rows.OrderBy(row => row.AccrualAmount),
            "incomeAmount" => sort.Descending ? rows.OrderByDescending(row => row.IncomeAmount) : rows.OrderBy(row => row.IncomeAmount),
            "debt" => sort.Descending ? rows.OrderByDescending(row => row.Debt) : rows.OrderBy(row => row.Debt),
            "documentNumber" => sort.Descending ? rows.OrderByDescending(row => row.DocumentNumber) : rows.OrderBy(row => row.DocumentNumber),
            _ => sort.Descending ? rows.OrderByDescending(row => row.Date) : rows.OrderBy(row => row.Date)
        };

    private async Task<IReadOnlyDictionary<Guid, decimal>> CalculateDebtAfterPaymentsAsync(
        IReadOnlyList<IncomeDebtTarget> targets,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var garageIds = targets.Select(target => target.GarageId).Distinct().ToArray();
        var targetAccountingMonths = targets.ToDictionary(target => target.OperationId, target => target.AccountingMonth);
        var maxOperationDate = targets.Max(target => target.OperationDate);
        var maxAccountingMonth = targets.Max(target => target.AccountingMonth);
        var startingBalanceQuery = dbContext.Garages.AsNoTracking()
            .Where(garage => garageIds.Contains(garage.Id))
            .Select(garage => new
            {
                Category = StartingBalanceDebtCategory,
                GarageId = garage.Id,
                OperationId = (Guid?)null,
                AccountingMonth = (DateOnly?)null,
                OperationDate = (DateOnly?)null,
                CreatedAtUtc = (DateTimeOffset?)null,
                Amount = garage.StartingBalance
            });
        var accrualQuery = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && garageIds.Contains(accrual.GarageId) && accrual.AccountingMonth <= maxAccountingMonth)
            .GroupBy(accrual => new { accrual.GarageId, accrual.AccountingMonth })
            .Select(group => new
            {
                Category = AccrualDebtCategory,
                GarageId = group.Key.GarageId,
                OperationId = (Guid?)null,
                AccountingMonth = (DateOnly?)group.Key.AccountingMonth,
                OperationDate = (DateOnly?)null,
                CreatedAtUtc = (DateTimeOffset?)null,
                Amount = group.Sum(accrual => accrual.Amount)
            });
        var paymentQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId != null &&
                garageIds.Contains(operation.GarageId.Value) &&
                operation.OperationDate <= maxOperationDate)
            .Select(operation => new
            {
                Category = PaymentDebtCategory,
                GarageId = operation.GarageId!.Value,
                OperationId = (Guid?)operation.Id,
                AccountingMonth = (DateOnly?)null,
                OperationDate = (DateOnly?)operation.OperationDate,
                CreatedAtUtc = (DateTimeOffset?)operation.CreatedAtUtc,
                operation.Amount
            });

        var debtRows = await startingBalanceQuery
            .Concat(accrualQuery)
            .Concat(paymentQuery)
            .ToListAsync(cancellationToken);
        var startingBalances = debtRows
            .Where(row => row.Category == StartingBalanceDebtCategory)
            .ToDictionary(row => row.GarageId, row => row.Amount);
        var accrualsByGarage = debtRows
            .Where(row => row.Category == AccrualDebtCategory)
            .Select(row => new IncomeDebtAccrualRow(row.GarageId, row.AccountingMonth!.Value, row.Amount))
            .GroupBy(row => row.GarageId)
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.AccountingMonth).ToArray());
        var relatedPayments = debtRows
            .Where(row => row.Category == PaymentDebtCategory)
            .Select(row => new IncomeDebtPaymentRow(
                row.OperationId!.Value,
                row.GarageId,
                row.OperationDate!.Value,
                row.CreatedAtUtc!.Value,
                row.Amount))
            .ToList();

        var result = new Dictionary<Guid, decimal>();
        foreach (var garageGroup in relatedPayments.GroupBy(payment => payment.GarageId).OrderBy(group => group.Key))
        {
            var paidTotal = 0m;
            var startingBalance = startingBalances.GetValueOrDefault(garageGroup.Key);
            var garageAccruals = accrualsByGarage.GetValueOrDefault(garageGroup.Key) ?? [];
            foreach (var payment in garageGroup.OrderBy(payment => payment.OperationDate).ThenBy(payment => payment.CreatedAtUtc).ThenBy(payment => payment.OperationId))
            {
                paidTotal += payment.Amount;
                if (!targetAccountingMonths.TryGetValue(payment.OperationId, out var accountingMonth))
                {
                    continue;
                }

                var accrualTotal = garageAccruals.Where(accrual => accrual.AccountingMonth <= accountingMonth).Sum(accrual => accrual.Amount);
                result[payment.OperationId] = MoneyMath.RoundMoney(startingBalance + accrualTotal - paidTotal);
            }
        }

        return result;
    }

    private static IQueryable<T> ApplyLimit<T>(IQueryable<T> query, int? limit) =>
        limit is > 0 ? query.Take(limit.Value) : query;

    private static IEnumerable<T> ApplyPage<T>(IEnumerable<T> rows, int offset, int? limit)
    {
        var page = offset > 0 ? rows.Skip(offset) : rows;
        return limit is > 0 ? page.Take(limit.Value) : page;
    }

    private static int? GetFetchLimit(int offset, int? limit) =>
        limit is > 0 ? (int)Math.Min((long)offset + limit.Value, int.MaxValue) : null;

    private bool IsNpgsql() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static IOrderedEnumerable<IncomeReportRowDto> ApplySort(IEnumerable<IncomeReportRowDto> rows, ReportSort sort) =>
        sort.Field switch
        {
            "accountingMonth" => sort.Descending ? rows.OrderByDescending(row => row.AccountingMonth) : rows.OrderBy(row => row.AccountingMonth),
            "garageNumber" => sort.Descending ? rows.OrderByDescending(row => row.GarageNumber, StringComparer.Ordinal) : rows.OrderBy(row => row.GarageNumber, StringComparer.Ordinal),
            "ownerName" => sort.Descending ? rows.OrderByDescending(row => row.OwnerName, StringComparer.Ordinal) : rows.OrderBy(row => row.OwnerName, StringComparer.Ordinal),
            "incomeTypeName" => sort.Descending ? rows.OrderByDescending(row => row.IncomeTypeName, StringComparer.Ordinal) : rows.OrderBy(row => row.IncomeTypeName, StringComparer.Ordinal),
            "accrualAmount" => sort.Descending ? rows.OrderByDescending(row => row.AccrualAmount) : rows.OrderBy(row => row.AccrualAmount),
            "incomeAmount" => sort.Descending ? rows.OrderByDescending(row => row.IncomeAmount) : rows.OrderBy(row => row.IncomeAmount),
            "debt" => sort.Descending ? rows.OrderByDescending(row => row.Debt) : rows.OrderBy(row => row.Debt),
            "documentNumber" => sort.Descending ? rows.OrderByDescending(row => row.DocumentNumber, StringComparer.Ordinal) : rows.OrderBy(row => row.DocumentNumber, StringComparer.Ordinal),
            _ => sort.Descending ? rows.OrderByDescending(row => row.Date) : rows.OrderBy(row => row.Date)
        };

    private readonly record struct IncomeDebtAccrualRow(Guid GarageId, DateOnly AccountingMonth, decimal Amount);
    private readonly record struct IncomeDebtPaymentRow(Guid OperationId, Guid GarageId, DateOnly OperationDate, DateTimeOffset CreatedAtUtc, decimal Amount);
    private readonly record struct IncomeDebtTarget(Guid OperationId, Guid GarageId, DateOnly AccountingMonth, DateOnly OperationDate);

    private sealed record IncomePaymentCombinedQueryRow(
        int Category,
        int RowOrder,
        Guid? Id,
        DateOnly? OperationDate,
        DateOnly? AccountingMonth,
        Guid? GarageId,
        string? GarageNumber,
        Guid? OwnerId,
        string? OwnerName,
        Guid? IncomeTypeId,
        string? IncomeTypeName,
        decimal AccrualAmount,
        decimal IncomeAmount,
        decimal Debt,
        string? DocumentNumber,
        string? Comment,
        DateTimeOffset? CreatedAtUtc,
        decimal IncomeTotal,
        int RowCount);

    private sealed class IncomeReportProjection : IncomeReportSortableProjection
    {
    }

    private class IncomeReportSortableProjection
    {
        public string RowType { get; init; } = string.Empty;
        public DateOnly Date { get; init; }
        public DateOnly AccountingMonth { get; init; }
        public Guid GarageId { get; init; }
        public string GarageNumber { get; init; } = string.Empty;
        public Guid? OwnerId { get; init; }
        public string? OwnerName { get; init; }
        public Guid IncomeTypeId { get; init; }
        public string IncomeTypeName { get; init; } = string.Empty;
        public decimal AccrualAmount { get; init; }
        public decimal IncomeAmount { get; init; }
        public decimal Debt { get; init; }
        public string? DocumentNumber { get; init; }
        public string? Comment { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public Guid RowId { get; init; }
        public Guid? PaymentOperationId { get; init; }
    }
}
