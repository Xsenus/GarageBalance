using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageReportQuery(GarageBalanceDbContext dbContext) : IGarageReportQuery
{
    private const string StartingBalanceName = "Стартовый баланс";
    private const string GroupedIncomeTypeName = "ИТОГО";

    public Task<GarageReportQueryData> GetRowsAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        string? search,
        bool groupAccruals,
        int offset,
        int limit,
        CancellationToken cancellationToken) =>
        GetRowsAsync(periodFrom, periodTo, search, groupAccruals, offset, limit, new ReportSort("accountingMonth", true), cancellationToken);

    public async Task<GarageReportQueryData> GetRowsAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        string? search,
        bool groupAccruals,
        int offset,
        int limit,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        var dateTo = periodTo.AddMonths(1).AddDays(-1);
        var garages = dbContext.Garages.AsNoTracking().Where(garage => !garage.IsArchived);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            if (IsNpgsql())
            {
                var normalizedServerSearch = normalizedSearch.ToLowerInvariant();
                garages = garages.Where(garage =>
                    garage.Number.ToLower().Contains(normalizedServerSearch) ||
                    (garage.Owner != null && (
                        garage.Owner.LastName.ToLower().Contains(normalizedServerSearch) ||
                        garage.Owner.FirstName.ToLower().Contains(normalizedServerSearch) ||
                        (garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(normalizedServerSearch)) ||
                        (garage.Owner.LastName + " " + garage.Owner.FirstName).ToLower().Contains(normalizedServerSearch) ||
                        (garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedServerSearch))));
            }
            else
            {
                var matchingGarageIds = (await garages.Include(garage => garage.Owner).ToListAsync(cancellationToken))
                    .Where(garage =>
                        garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                        (garage.Owner?.FullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Select(garage => garage.Id)
                    .ToList();
                garages = garages.Where(garage => matchingGarageIds.Contains(garage.Id));
            }
        }

        var garageIds = garages.Select(garage => garage.Id);
        var startingBalances = garages
            .Where(garage => garage.StartingBalance != 0)
            .Select(garage => new
            {
                AccountingMonth = periodFrom,
                GarageId = garage.Id,
                GarageNumber = garage.Number,
                OwnerLastName = garage.Owner == null ? null : garage.Owner.LastName,
                OwnerFirstName = garage.Owner == null ? null : garage.Owner.FirstName,
                OwnerMiddleName = garage.Owner == null ? null : garage.Owner.MiddleName,
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = StartingBalanceName,
                AccrualAmount = garage.StartingBalance,
                IncomeAmount = 0m
            });
        var accruals = dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                garageIds.Contains(accrual.GarageId) &&
                accrual.AccountingMonth >= periodFrom &&
                accrual.AccountingMonth <= periodTo)
            .Select(accrual => new
            {
                accrual.AccountingMonth,
                GarageId = accrual.GarageId,
                GarageNumber = accrual.Garage.Number,
                OwnerLastName = accrual.Garage.Owner == null ? null : accrual.Garage.Owner.LastName,
                OwnerFirstName = accrual.Garage.Owner == null ? null : accrual.Garage.Owner.FirstName,
                OwnerMiddleName = accrual.Garage.Owner == null ? null : accrual.Garage.Owner.MiddleName,
                IncomeTypeId = (Guid?)accrual.IncomeTypeId,
                IncomeTypeName = accrual.IncomeType.Name,
                AccrualAmount = accrual.Amount,
                IncomeAmount = 0m
            });
        var payments = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId != null &&
                operation.IncomeTypeId != null &&
                garageIds.Contains(operation.GarageId.Value) &&
                operation.OperationDate >= periodFrom &&
                operation.OperationDate <= dateTo)
            .Select(operation => new
            {
                operation.AccountingMonth,
                GarageId = operation.GarageId!.Value,
                GarageNumber = operation.Garage!.Number,
                OwnerLastName = operation.Garage.Owner == null ? null : operation.Garage.Owner.LastName,
                OwnerFirstName = operation.Garage.Owner == null ? null : operation.Garage.Owner.FirstName,
                OwnerMiddleName = operation.Garage.Owner == null ? null : operation.Garage.Owner.MiddleName,
                IncomeTypeId = operation.IncomeTypeId,
                IncomeTypeName = operation.IncomeType!.Name,
                AccrualAmount = 0m,
                IncomeAmount = operation.Amount
            });

        var sourceRows = startingBalances.Concat(accruals).Concat(payments);
        var groupedRows = groupAccruals
            ? sourceRows
                .GroupBy(row => new
                {
                    row.AccountingMonth,
                    row.GarageId,
                    row.GarageNumber,
                    row.OwnerLastName,
                    row.OwnerFirstName,
                    row.OwnerMiddleName
                })
                .Select(group => new
                {
                    group.Key.AccountingMonth,
                    group.Key.GarageId,
                    group.Key.GarageNumber,
                    group.Key.OwnerLastName,
                    group.Key.OwnerFirstName,
                    group.Key.OwnerMiddleName,
                    IncomeTypeId = (Guid?)null,
                    IncomeTypeName = GroupedIncomeTypeName,
                    AccrualAmount = group.Sum(row => row.AccrualAmount),
                    IncomeAmount = group.Sum(row => row.IncomeAmount)
                })
            : sourceRows
                .GroupBy(row => new
                {
                    row.AccountingMonth,
                    row.GarageId,
                    row.GarageNumber,
                    row.OwnerLastName,
                    row.OwnerFirstName,
                    row.OwnerMiddleName,
                    row.IncomeTypeId,
                    row.IncomeTypeName
                })
                .Select(group => new
                {
                    group.Key.AccountingMonth,
                    group.Key.GarageId,
                    group.Key.GarageNumber,
                    group.Key.OwnerLastName,
                    group.Key.OwnerFirstName,
                    group.Key.OwnerMiddleName,
                    group.Key.IncomeTypeId,
                    group.Key.IncomeTypeName,
                    AccrualAmount = group.Sum(row => row.AccrualAmount),
                    IncomeAmount = group.Sum(row => row.IncomeAmount)
                });

        var summary = await groupedRows
            .GroupBy(_ => 1)
            .Select(group => new
            {
                AccrualTotal = group.Sum(row => row.AccrualAmount),
                IncomeTotal = group.Sum(row => row.IncomeAmount),
                RowCount = group.Count()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (summary is null)
        {
            return new GarageReportQueryData(0m, 0m, 0, []);
        }

        var orderedRows = sort.Field switch
        {
            "garageNumber" => sort.Descending ? groupedRows.OrderByDescending(row => row.GarageNumber) : groupedRows.OrderBy(row => row.GarageNumber),
            "ownerName" => sort.Descending
                ? groupedRows.OrderByDescending(row => (row.OwnerLastName ?? string.Empty) + " " + (row.OwnerFirstName ?? string.Empty) + " " + (row.OwnerMiddleName ?? string.Empty))
                : groupedRows.OrderBy(row => (row.OwnerLastName ?? string.Empty) + " " + (row.OwnerFirstName ?? string.Empty) + " " + (row.OwnerMiddleName ?? string.Empty)),
            "incomeTypeName" => sort.Descending ? groupedRows.OrderByDescending(row => row.IncomeTypeName) : groupedRows.OrderBy(row => row.IncomeTypeName),
            "accrualAmount" => sort.Descending ? groupedRows.OrderByDescending(row => row.AccrualAmount) : groupedRows.OrderBy(row => row.AccrualAmount),
            "incomeAmount" => sort.Descending ? groupedRows.OrderByDescending(row => row.IncomeAmount) : groupedRows.OrderBy(row => row.IncomeAmount),
            "difference" => sort.Descending
                ? groupedRows.OrderByDescending(row => row.AccrualAmount - row.IncomeAmount)
                : groupedRows.OrderBy(row => row.AccrualAmount - row.IncomeAmount),
            _ => sort.Descending ? groupedRows.OrderByDescending(row => row.AccountingMonth) : groupedRows.OrderBy(row => row.AccountingMonth)
        };
        var rows = await orderedRows
            .ThenBy(row => row.GarageNumber)
            .ThenBy(row => row.IncomeTypeName)
            .ThenBy(row => row.AccountingMonth)
            .ThenBy(row => row.GarageId)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return new GarageReportQueryData(
            summary.AccrualTotal,
            summary.IncomeTotal,
            summary.RowCount,
            rows.Select(row => new GarageReportQueryRow(
                    row.AccountingMonth,
                    row.GarageId,
                    row.GarageNumber,
                    row.OwnerLastName,
                    row.OwnerFirstName,
                    row.OwnerMiddleName,
                    row.IncomeTypeId,
                    row.IncomeTypeName,
                    row.AccrualAmount,
                    row.IncomeAmount))
                .ToList());
    }

    private bool IsNpgsql() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false;
}
