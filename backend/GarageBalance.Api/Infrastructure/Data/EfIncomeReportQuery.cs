using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfIncomeReportQuery(GarageBalanceDbContext dbContext) : IIncomeReportQuery
{
    private const int StartingBalanceDebtCategory = 1;
    private const int AccrualDebtCategory = 2;
    private const int PaymentDebtCategory = 3;
    private const string AllRows = "all";
    private const string AccrualRows = "accruals";
    private const string PaymentRows = "payments";
    private const string StartingBalanceRows = "starting_balance";

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
        CancellationToken cancellationToken)
    {
        var rows = new List<IncomeReportRowDto>();
        var accrualTotal = 0m;
        var incomeTotal = 0m;
        var rowCount = 0;
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var normalizedSearch = search?.Trim().ToLowerInvariant();
        var useClientSearch = hasSearch && !(dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false);
        var fetchLimit = useClientSearch ? null : GetFetchLimit(offset, limit);

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
                    var totals = await startingBalanceQuery
                        .GroupBy(_ => 1)
                        .Select(group => new { Total = group.Sum(garage => garage.StartingBalance), Count = group.Count() })
                        .SingleOrDefaultAsync(cancellationToken);
                    accrualTotal += totals?.Total ?? 0m;
                    rowCount += totals?.Count ?? 0;
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
                var totals = await accrualsQuery
                    .GroupBy(_ => 1)
                    .Select(group => new { Total = group.Sum(accrual => accrual.Amount), Count = group.Count() })
                    .SingleOrDefaultAsync(cancellationToken);
                accrualTotal += totals?.Total ?? 0m;
                rowCount += totals?.Count ?? 0;
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
                var totals = await paymentsQuery
                    .GroupBy(_ => 1)
                    .Select(group => new { Total = group.Sum(operation => operation.Amount), Count = group.Count() })
                    .SingleOrDefaultAsync(cancellationToken);
                incomeTotal += totals?.Total ?? 0m;
                rowCount += totals?.Count ?? 0;
            }

            var payments = await ApplyLimit(
                    paymentsQuery.OrderBy(operation => operation.OperationDate).ThenBy(operation => operation.Garage!.Number).ThenBy(operation => operation.Id),
                    fetchLimit)
                .ToListAsync(cancellationToken);
            var debtAfterPayments = await CalculateDebtAfterPaymentsAsync(payments, cancellationToken);
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

        if (useClientSearch)
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
                rows.OrderBy(row => row.Date).ThenBy(row => row.GarageNumber).ThenBy(row => row.RowType),
                offset,
                limit)
            .ToList();
        return new IncomeReportQueryData(accrualTotal, incomeTotal, rowCount, visibleRows);
    }

    private async Task<IReadOnlyDictionary<Guid, decimal>> CalculateDebtAfterPaymentsAsync(
        IReadOnlyList<FinancialOperation> operations,
        CancellationToken cancellationToken)
    {
        if (operations.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var garageIds = operations.Select(operation => operation.GarageId!.Value).Distinct().ToArray();
        var targetAccountingMonths = operations.ToDictionary(operation => operation.Id, operation => operation.AccountingMonth);
        var maxOperationDate = operations.Max(operation => operation.OperationDate);
        var maxAccountingMonth = operations.Max(operation => operation.AccountingMonth);
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

    private readonly record struct IncomeDebtAccrualRow(Guid GarageId, DateOnly AccountingMonth, decimal Amount);
    private readonly record struct IncomeDebtPaymentRow(Guid OperationId, Guid GarageId, DateOnly OperationDate, DateTimeOffset CreatedAtUtc, decimal Amount);
}
