using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Reports;

public sealed class ReportService(GarageBalanceDbContext dbContext) : IReportService
{
    private const string IncomeReportAllRows = "all";
    private const string IncomeReportAccrualRows = "accruals";
    private const string IncomeReportPaymentRows = "payments";
    private const string ExpenseReportAllRows = "all";
    private const string ExpenseReportAccrualRows = "accruals";
    private const string ExpenseReportPaymentRows = "payments";

    public async Task<ReportResult<ConsolidatedReportDto>> GetConsolidatedReportAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken)
    {
        var periodFrom = NormalizeMonth(request.MonthFrom ?? DateOnly.FromDateTime(DateTime.Today));
        var periodTo = NormalizeMonth(request.MonthTo ?? periodFrom);
        if (periodTo < periodFrom)
        {
            return ReportResult<ConsolidatedReportDto>.Failure("period_invalid", "Дата окончания отчета не может быть раньше даты начала.");
        }

        var operations = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.AccountingMonth >= periodFrom && operation.AccountingMonth <= periodTo);
        var accruals = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.AccountingMonth >= periodFrom && accrual.AccountingMonth <= periodTo);
        var meterReadings = dbContext.MeterReadings.AsNoTracking()
            .Where(reading => !reading.IsCanceled && reading.AccountingMonth >= periodFrom && reading.AccountingMonth <= periodTo);

        var incomeByMonth = await operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Income)
            .GroupBy(operation => operation.AccountingMonth)
            .Select(group => new AmountCountByMonth(group.Key, group.Sum(item => item.Amount), group.Count()))
            .ToListAsync(cancellationToken);
        var expenseByMonth = await operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Expense)
            .GroupBy(operation => operation.AccountingMonth)
            .Select(group => new AmountCountByMonth(group.Key, group.Sum(item => item.Amount), group.Count()))
            .ToListAsync(cancellationToken);
        var accrualByMonth = await accruals
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new AmountCountByMonth(group.Key, group.Sum(item => item.Amount), group.Count()))
            .ToListAsync(cancellationToken);
        var readingsByMonth = await meterReadings
            .GroupBy(reading => reading.AccountingMonth)
            .Select(group => new CountByMonth(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        var months = EnumerateMonths(periodFrom, periodTo).ToList();
        var monthlyRows = months
            .Select(month =>
            {
                var income = incomeByMonth.SingleOrDefault(row => row.Month == month);
                var expense = expenseByMonth.SingleOrDefault(row => row.Month == month);
                var accrual = accrualByMonth.SingleOrDefault(row => row.Month == month);
                var readings = readingsByMonth.SingleOrDefault(row => row.Month == month);
                return new MonthlyReportRowDto(
                    month,
                    income.Amount,
                    expense.Amount,
                    accrual.Amount,
                    income.Amount - expense.Amount,
                    accrual.Amount - income.Amount,
                    income.Count + expense.Count,
                    accrual.Count,
                    readings.Count);
            })
            .ToList();

        var garageRows = await BuildGarageRowsAsync(request.Search, periodFrom, periodTo, cancellationToken);
        var report = new ConsolidatedReportDto(
            periodFrom,
            periodTo,
            monthlyRows.Sum(row => row.IncomeTotal),
            monthlyRows.Sum(row => row.ExpenseTotal),
            monthlyRows.Sum(row => row.AccrualTotal),
            monthlyRows.Sum(row => row.Balance),
            monthlyRows.Sum(row => row.Debt),
            monthlyRows.Sum(row => row.OperationCount),
            monthlyRows.Sum(row => row.AccrualCount),
            monthlyRows.Sum(row => row.MeterReadingCount),
            monthlyRows,
            garageRows);

        return ReportResult<ConsolidatedReportDto>.Success(report);
    }

    public async Task<ReportResult<IncomeReportDto>> GetIncomeReportAsync(IncomeReportRequest request, CancellationToken cancellationToken)
    {
        var (dateFrom, dateTo) = NormalizeDateRange(request.DateFrom, request.DateTo);
        if (dateTo < dateFrom)
        {
            return ReportResult<IncomeReportDto>.Failure("period_invalid", "Дата окончания отчета не может быть раньше даты начала.");
        }

        var rowMode = string.IsNullOrWhiteSpace(request.RowMode)
            ? IncomeReportAllRows
            : request.RowMode.Trim().ToLowerInvariant();
        if (rowMode is not (IncomeReportAllRows or IncomeReportAccrualRows or IncomeReportPaymentRows))
        {
            return ReportResult<IncomeReportDto>.Failure("row_mode_invalid", "Режим строк отчета по поступлениям неизвестен.");
        }

        var garageIds = request.GarageIds.ToHashSet();
        var ownerIds = request.OwnerIds.ToHashSet();
        var incomeTypeIds = request.IncomeTypeIds.ToHashSet();
        var rows = new List<IncomeReportRowDto>();

        if (rowMode is IncomeReportAllRows or IncomeReportAccrualRows)
        {
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

            var accrualRows = await accrualsQuery
                .OrderBy(accrual => accrual.AccountingMonth)
                .ThenBy(accrual => accrual.Garage.Number)
                .ToListAsync(cancellationToken);

            rows.AddRange(accrualRows.Select(accrual => new IncomeReportRowDto(
                IncomeReportAccrualRows,
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

        if (rowMode is IncomeReportAllRows or IncomeReportPaymentRows)
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
                paymentsQuery = paymentsQuery.Where(operation => operation.GarageId != null && garageIds.Contains(operation.GarageId.Value));
            }

            if (ownerIds.Count > 0)
            {
                paymentsQuery = paymentsQuery.Where(operation => operation.Garage != null && operation.Garage.OwnerId != null && ownerIds.Contains(operation.Garage.OwnerId.Value));
            }

            if (incomeTypeIds.Count > 0)
            {
                paymentsQuery = paymentsQuery.Where(operation => operation.IncomeTypeId != null && incomeTypeIds.Contains(operation.IncomeTypeId.Value));
            }

            var paymentRows = await paymentsQuery
                .OrderBy(operation => operation.OperationDate)
                .ThenBy(operation => operation.Garage!.Number)
                .ToListAsync(cancellationToken);

            rows.AddRange(paymentRows.Select(operation => new IncomeReportRowDto(
                IncomeReportPaymentRows,
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
                operation.Comment)));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var normalizedSearch = request.Search.Trim();
            rows = rows
                .Where(row =>
                    row.GarageNumber.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    (row.OwnerName?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    row.IncomeTypeName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    (row.DocumentNumber?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        rows = rows
            .OrderBy(row => row.Date)
            .ThenBy(row => row.GarageNumber)
            .ThenBy(row => row.RowType)
            .ToList();

        var report = new IncomeReportDto(
            dateFrom,
            dateTo,
            rows.Sum(row => row.AccrualAmount),
            rows.Sum(row => row.IncomeAmount),
            rows.Sum(row => row.Debt),
            rows.Count,
            rows);

        return ReportResult<IncomeReportDto>.Success(report);
    }

    public async Task<ReportResult<ExpenseReportDto>> GetExpenseReportAsync(ExpenseReportRequest request, CancellationToken cancellationToken)
    {
        var (dateFrom, dateTo) = NormalizeDateRange(request.DateFrom, request.DateTo);
        if (dateTo < dateFrom)
        {
            return ReportResult<ExpenseReportDto>.Failure("period_invalid", "Дата окончания отчета не может быть раньше даты начала.");
        }

        var rowMode = string.IsNullOrWhiteSpace(request.RowMode)
            ? ExpenseReportAllRows
            : request.RowMode.Trim().ToLowerInvariant();
        if (rowMode is not (ExpenseReportAllRows or ExpenseReportAccrualRows or ExpenseReportPaymentRows))
        {
            return ReportResult<ExpenseReportDto>.Failure("row_mode_invalid", "Режим строк отчета по выплатам неизвестен.");
        }

        var supplierIds = request.SupplierIds.ToHashSet();
        var expenseTypeIds = request.ExpenseTypeIds.ToHashSet();
        var rows = new List<ExpenseReportRowDto>();

        if (rowMode is ExpenseReportAllRows or ExpenseReportPaymentRows)
        {
            var paymentsQuery = dbContext.FinancialOperations.AsNoTracking()
                .Include(operation => operation.Supplier)
                .Include(operation => operation.ExpenseType)
                .Where(operation =>
                    !operation.IsCanceled &&
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    operation.SupplierId != null &&
                    operation.ExpenseTypeId != null &&
                    operation.OperationDate >= dateFrom &&
                    operation.OperationDate <= dateTo);

            if (supplierIds.Count > 0)
            {
                paymentsQuery = paymentsQuery.Where(operation => operation.SupplierId != null && supplierIds.Contains(operation.SupplierId.Value));
            }

            if (expenseTypeIds.Count > 0)
            {
                paymentsQuery = paymentsQuery.Where(operation => operation.ExpenseTypeId != null && expenseTypeIds.Contains(operation.ExpenseTypeId.Value));
            }

            var paymentRows = await paymentsQuery
                .OrderBy(operation => operation.OperationDate)
                .ThenBy(operation => operation.Supplier!.Name)
                .ToListAsync(cancellationToken);

            rows.AddRange(paymentRows.Select(operation => new ExpenseReportRowDto(
                ExpenseReportPaymentRows,
                operation.OperationDate,
                operation.AccountingMonth,
                operation.SupplierId!.Value,
                operation.Supplier!.Name,
                operation.ExpenseTypeId!.Value,
                operation.ExpenseType!.Name,
                0m,
                operation.Amount,
                -operation.Amount,
                operation.DocumentNumber,
                operation.Comment)));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var normalizedSearch = request.Search.Trim();
            rows = rows
                .Where(row =>
                    row.SupplierName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    row.ExpenseTypeName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    (row.DocumentNumber?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        rows = rows
            .OrderBy(row => row.Date)
            .ThenBy(row => row.SupplierName)
            .ThenBy(row => row.RowType)
            .ToList();

        var report = new ExpenseReportDto(
            dateFrom,
            dateTo,
            rows.Sum(row => row.AccrualAmount),
            rows.Sum(row => row.ExpenseAmount),
            rows.Sum(row => row.Difference),
            rows.Count,
            rows);

        return ReportResult<ExpenseReportDto>.Success(report);
    }

    private async Task<IReadOnlyList<GarageReportRowDto>> BuildGarageRowsAsync(string? search, DateOnly periodFrom, DateOnly periodTo, CancellationToken cancellationToken)
    {
        var garagesQuery = dbContext.Garages.AsNoTracking()
            .Include(garage => garage.Owner)
            .Where(garage => !garage.IsArchived);

        var garages = await garagesQuery
            .OrderBy(garage => garage.Number)
            .ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim();
            garages = garages
                .Where(garage =>
                    garage.Number.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    (garage.Owner?.FullName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        var garageIds = garages.Select(garage => garage.Id).ToList();

        var incomeByGarage = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId != null &&
                garageIds.Contains(operation.GarageId.Value) &&
                operation.AccountingMonth >= periodFrom &&
                operation.AccountingMonth <= periodTo)
            .GroupBy(operation => operation.GarageId!.Value)
            .Select(group => new AmountByGarage(group.Key, group.Sum(item => item.Amount)))
            .ToListAsync(cancellationToken);
        var accrualByGarage = await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                garageIds.Contains(accrual.GarageId) &&
                accrual.AccountingMonth >= periodFrom &&
                accrual.AccountingMonth <= periodTo)
            .GroupBy(accrual => accrual.GarageId)
            .Select(group => new AmountByGarage(group.Key, group.Sum(item => item.Amount)))
            .ToListAsync(cancellationToken);
        var readingsByGarage = await dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                garageIds.Contains(reading.GarageId) &&
                reading.AccountingMonth >= periodFrom &&
                reading.AccountingMonth <= periodTo)
            .GroupBy(reading => reading.GarageId)
            .Select(group => new CountByGarage(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        return garages
            .Select(garage =>
            {
                var income = incomeByGarage.SingleOrDefault(row => row.GarageId == garage.Id).Amount;
                var accrual = accrualByGarage.SingleOrDefault(row => row.GarageId == garage.Id).Amount;
                var readings = readingsByGarage.SingleOrDefault(row => row.GarageId == garage.Id).Count;
                return new GarageReportRowDto(garage.Id, garage.Number, garage.Owner?.FullName, income, accrual, accrual - income, readings);
            })
            .Where(row => row.IncomeTotal != 0 || row.AccrualTotal != 0 || row.MeterReadingCount != 0)
            .ToList();
    }

    private static IEnumerable<DateOnly> EnumerateMonths(DateOnly periodFrom, DateOnly periodTo)
    {
        for (var month = periodFrom; month <= periodTo; month = month.AddMonths(1))
        {
            yield return month;
        }
    }

    private static DateOnly NormalizeMonth(DateOnly value)
    {
        return new DateOnly(value.Year, value.Month, 1);
    }

    private static (DateOnly DateFrom, DateOnly DateTo) NormalizeDateRange(DateOnly? dateFrom, DateOnly? dateTo)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = dateFrom ?? new DateOnly(today.Year, today.Month, 1);
        var end = dateTo ?? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        return (start, end);
    }

    private readonly record struct AmountCountByMonth(DateOnly Month, decimal Amount, int Count);
    private readonly record struct CountByMonth(DateOnly Month, int Count);
    private readonly record struct AmountByGarage(Guid GarageId, decimal Amount);
    private readonly record struct CountByGarage(Guid GarageId, int Count);
}
