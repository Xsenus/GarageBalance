using System.Globalization;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Reports;

public sealed class ReportService(GarageBalanceDbContext dbContext) : IReportService
{
    private const string IncomeReportAllRows = "all";
    private const string IncomeReportAccrualRows = "accruals";
    private const string IncomeReportPaymentRows = "payments";
    private const string StartingBalanceRows = "starting_balance";
    private const string ExpenseReportAllRows = "all";
    private const string ExpenseReportAccrualRows = "accruals";
    private const string ExpenseReportPaymentRows = "payments";

    public async Task<ReportResult<ConsolidatedReportDto>> GetConsolidatedReportAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken)
    {
        var periodFrom = MonthPeriod.Normalize(request.MonthFrom ?? MonthPeriod.CurrentLocalMonth());
        var periodTo = MonthPeriod.Normalize(request.MonthTo ?? periodFrom);
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
        var garageStartingBalanceTotal = await dbContext.Garages.AsNoTracking()
            .Where(garage => !garage.IsArchived && garage.StartingBalance != 0)
            .SumAsync(garage => garage.StartingBalance, cancellationToken);

        var months = MonthPeriod.Enumerate(periodFrom, periodTo).ToList();
        var monthlyRows = months
            .Select(month =>
            {
                var income = incomeByMonth.SingleOrDefault(row => row.Month == month);
                var expense = expenseByMonth.SingleOrDefault(row => row.Month == month);
                var accrual = accrualByMonth.SingleOrDefault(row => row.Month == month);
                var readings = readingsByMonth.SingleOrDefault(row => row.Month == month);
                var startingBalance = month == periodFrom ? garageStartingBalanceTotal : 0m;
                return new MonthlyReportRowDto(
                    month,
                    income.Amount,
                    expense.Amount,
                    accrual.Amount + startingBalance,
                    income.Amount - expense.Amount,
                    accrual.Amount + startingBalance - income.Amount,
                    income.Count + expense.Count,
                    accrual.Count + (startingBalance != 0 ? 1 : 0),
                    readings.Count);
            })
            .ToList();

        var garageRows = await BuildGarageRowsPageAsync(request.Search, periodFrom, periodTo, request.Limit, cancellationToken);
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
            garageRows.RowCount,
            garageRows.Rows);

        return ReportResult<ConsolidatedReportDto>.Success(report);
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportConsolidatedReportXlsxAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetConsolidatedReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var content = XlsxWorkbookBuilder.Build(
            [
                new XlsxSheet(
                    "Месяцы",
                    ["Месяц", "Начислено", "Поступило", "Выплаты", "Баланс", "Долг", "Операций", "Начислений", "Показаний"],
                    report.MonthlyRows.Select(row => (IReadOnlyList<XlsxCell>)
                    [
                        XlsxCell.Text(row.AccountingMonth.ToString("yyyy-MM")),
                        XlsxCell.Number(row.AccrualTotal),
                        XlsxCell.Number(row.IncomeTotal),
                        XlsxCell.Number(row.ExpenseTotal),
                        XlsxCell.Number(row.Balance),
                        XlsxCell.Number(row.Debt),
                        XlsxCell.Number(row.OperationCount),
                        XlsxCell.Number(row.AccrualCount),
                        XlsxCell.Number(row.MeterReadingCount)
                    ]).ToArray()),
                new XlsxSheet(
                    "Гаражи",
                    ["Гараж", "Владелец", "Начислено", "Поступило", "Долг", "Показаний"],
                    report.GarageRows.Select(row => (IReadOnlyList<XlsxCell>)
                    [
                        XlsxCell.Text(row.GarageNumber),
                        XlsxCell.Text(row.OwnerName),
                        XlsxCell.Number(row.AccrualTotal),
                        XlsxCell.Number(row.IncomeTotal),
                        XlsxCell.Number(row.Debt),
                        XlsxCell.Number(row.MeterReadingCount)
                    ]).ToArray()),
                new XlsxSheet(
                    "Итоги",
                    ["Период с", "Период по", "Начислено", "Поступило", "Выплаты", "Баланс", "Долг", "Операций", "Начислений", "Показаний"],
                    [
                        [
                            XlsxCell.Text(report.PeriodFrom.ToString("yyyy-MM-dd")),
                            XlsxCell.Text(report.PeriodTo.ToString("yyyy-MM-dd")),
                            XlsxCell.Number(report.AccrualTotal),
                            XlsxCell.Number(report.IncomeTotal),
                            XlsxCell.Number(report.ExpenseTotal),
                            XlsxCell.Number(report.Balance),
                            XlsxCell.Number(report.Debt),
                            XlsxCell.Number(report.OperationCount),
                            XlsxCell.Number(report.AccrualCount),
                            XlsxCell.Number(report.MeterReadingCount)
                        ]
                    ])
            ]);

        return ReportResult<ReportExportFileDto>.Success(new ReportExportFileDto(
            BuildExportFileName("consolidated", report.PeriodFrom, report.PeriodTo, "xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content));
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportConsolidatedReportPdfAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetConsolidatedReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var lines = new List<string>
        {
            $"Period: {report.PeriodFrom:yyyy-MM-dd} - {report.PeriodTo:yyyy-MM-dd}",
            $"Accrued: {FormatAmount(report.AccrualTotal)} | Income: {FormatAmount(report.IncomeTotal)} | Expenses: {FormatAmount(report.ExpenseTotal)} | Balance: {FormatAmount(report.Balance)} | Debt: {FormatAmount(report.Debt)}",
            $"Operations: {report.OperationCount} | Accruals: {report.AccrualCount} | Meter readings: {report.MeterReadingCount}",
            string.Empty,
            "Monthly rows",
            "Month | Accrued | Income | Expenses | Balance | Debt | Operations | Accruals | Readings"
        };
        lines.AddRange(report.MonthlyRows.Select(row =>
            string.Join(" | ",
                row.AccountingMonth.ToString("yyyy-MM"),
                FormatAmount(row.AccrualTotal),
                FormatAmount(row.IncomeTotal),
                FormatAmount(row.ExpenseTotal),
                FormatAmount(row.Balance),
                FormatAmount(row.Debt),
                row.OperationCount,
                row.AccrualCount,
                row.MeterReadingCount)));
        lines.Add(string.Empty);
        lines.Add("Garage rows");
        lines.Add("Garage | Owner | Accrued | Income | Debt | Readings");
        lines.AddRange(report.GarageRows.Select(row =>
            string.Join(" | ",
                row.GarageNumber,
                row.OwnerName ?? string.Empty,
                FormatAmount(row.AccrualTotal),
                FormatAmount(row.IncomeTotal),
                FormatAmount(row.Debt),
                row.MeterReadingCount)));

        var content = PdfReportDocumentBuilder.Build("GarageBalance consolidated report", lines);
        return ReportResult<ReportExportFileDto>.Success(new ReportExportFileDto(
            BuildExportFileName("consolidated", report.PeriodFrom, report.PeriodTo, "pdf"),
            "application/pdf",
            content));
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
        if (string.IsNullOrWhiteSpace(request.Search))
        {
            return await GetIncomeReportWithoutSearchAsync(request, dateFrom, dateTo, rowMode, garageIds, ownerIds, incomeTypeIds, cancellationToken);
        }

        var rows = new List<IncomeReportRowDto>();

        if (rowMode is IncomeReportAllRows or IncomeReportAccrualRows)
        {
            if (incomeTypeIds.Count == 0)
            {
                var garagesQuery = dbContext.Garages.AsNoTracking()
                    .Include(garage => garage.Owner)
                    .Where(garage => !garage.IsArchived && garage.StartingBalance != 0);

                if (garageIds.Count > 0)
                {
                    garagesQuery = garagesQuery.Where(garage => garageIds.Contains(garage.Id));
                }

                if (ownerIds.Count > 0)
                {
                    garagesQuery = garagesQuery.Where(garage => garage.OwnerId != null && ownerIds.Contains(garage.OwnerId.Value));
                }

                var startingBalanceRows = await garagesQuery
                    .OrderBy(garage => garage.Number)
                    .ToListAsync(cancellationToken);

                rows.AddRange(startingBalanceRows.Select(garage => new IncomeReportRowDto(
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

        var rowCount = rows.Count;
        var visibleRows = ApplyRowLimit(rows, request.Limit);
        var report = new IncomeReportDto(
            dateFrom,
            dateTo,
            rows.Sum(row => row.AccrualAmount),
            rows.Sum(row => row.IncomeAmount),
            rows.Sum(row => row.Debt),
            rowCount,
            visibleRows);

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
        if (string.IsNullOrWhiteSpace(request.Search))
        {
            return await GetExpenseReportWithoutSearchAsync(request, dateFrom, dateTo, rowMode, supplierIds, expenseTypeIds, cancellationToken);
        }

        var rows = new List<ExpenseReportRowDto>();

        if (rowMode is ExpenseReportAllRows or ExpenseReportAccrualRows)
        {
            if (expenseTypeIds.Count == 0)
            {
                var suppliersQuery = dbContext.Suppliers.AsNoTracking()
                    .Where(supplier => !supplier.IsArchived && supplier.StartingBalance != 0);

                if (supplierIds.Count > 0)
                {
                    suppliersQuery = suppliersQuery.Where(supplier => supplierIds.Contains(supplier.Id));
                }

                var startingBalanceRows = await suppliersQuery
                    .OrderBy(supplier => supplier.Name)
                    .ToListAsync(cancellationToken);

                rows.AddRange(startingBalanceRows.Select(supplier => new ExpenseReportRowDto(
                    StartingBalanceRows,
                    dateFrom,
                    dateFrom,
                    supplier.Id,
                    supplier.Name,
                    Guid.Empty,
                    "Стартовый баланс",
                    supplier.StartingBalance,
                    0m,
                    supplier.StartingBalance,
                    null,
                    "Начальное обязательство перед поставщиком")));
            }

            var accrualsQuery = dbContext.SupplierAccruals.AsNoTracking()
                .Include(accrual => accrual.Supplier)
                .Include(accrual => accrual.ExpenseType)
                .Where(accrual =>
                    !accrual.IsCanceled &&
                    accrual.AccountingMonth >= dateFrom &&
                    accrual.AccountingMonth <= dateTo);

            if (supplierIds.Count > 0)
            {
                accrualsQuery = accrualsQuery.Where(accrual => supplierIds.Contains(accrual.SupplierId));
            }

            if (expenseTypeIds.Count > 0)
            {
                accrualsQuery = accrualsQuery.Where(accrual => expenseTypeIds.Contains(accrual.ExpenseTypeId));
            }

            var accrualRows = await accrualsQuery
                .OrderBy(accrual => accrual.AccountingMonth)
                .ThenBy(accrual => accrual.Supplier.Name)
                .ToListAsync(cancellationToken);

            rows.AddRange(accrualRows.Select(accrual => new ExpenseReportRowDto(
                ExpenseReportAccrualRows,
                accrual.AccountingMonth,
                accrual.AccountingMonth,
                accrual.SupplierId,
                accrual.Supplier.Name,
                accrual.ExpenseTypeId,
                accrual.ExpenseType.Name,
                accrual.Amount,
                0m,
                accrual.Amount,
                accrual.DocumentNumber,
                accrual.Comment)));
        }

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

        var rowCount = rows.Count;
        var visibleRows = ApplyRowLimit(rows, request.Limit);
        var report = new ExpenseReportDto(
            dateFrom,
            dateTo,
            rows.Sum(row => row.AccrualAmount),
            rows.Sum(row => row.ExpenseAmount),
            rows.Sum(row => row.Difference),
            rowCount,
            visibleRows);

        return ReportResult<ExpenseReportDto>.Success(report);
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportIncomeReportXlsxAsync(IncomeReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetIncomeReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var content = XlsxWorkbookBuilder.Build(
            [
                new XlsxSheet(
                    "Поступления",
                    ["Тип", "Дата", "Месяц учета", "Гараж", "Владелец", "Вид поступления", "Начислено", "Оплачено", "Разница", "Документ", "Комментарий"],
                    report.Rows.Select(row => (IReadOnlyList<XlsxCell>)
                    [
                        XlsxCell.Text(FormatIncomeRowType(row.RowType)),
                        XlsxCell.Text(row.Date.ToString("yyyy-MM-dd")),
                        XlsxCell.Text(row.AccountingMonth.ToString("yyyy-MM")),
                        XlsxCell.Text(row.GarageNumber),
                        XlsxCell.Text(row.OwnerName),
                        XlsxCell.Text(row.IncomeTypeName),
                        XlsxCell.Number(row.AccrualAmount),
                        XlsxCell.Number(row.IncomeAmount),
                        XlsxCell.Number(row.Debt),
                        XlsxCell.Text(row.DocumentNumber),
                        XlsxCell.Text(row.Comment)
                    ]).ToArray()),
                new XlsxSheet(
                    "Итоги",
                    ["Период с", "Период по", "Начислено", "Оплачено", "Разница", "Строк"],
                    [
                        [
                            XlsxCell.Text(report.DateFrom.ToString("yyyy-MM-dd")),
                            XlsxCell.Text(report.DateTo.ToString("yyyy-MM-dd")),
                            XlsxCell.Number(report.AccrualTotal),
                            XlsxCell.Number(report.IncomeTotal),
                            XlsxCell.Number(report.Debt),
                            XlsxCell.Number(report.RowCount)
                        ]
                    ])
            ]);

        return ReportResult<ReportExportFileDto>.Success(new ReportExportFileDto(
            BuildExportFileName("income", report.DateFrom, report.DateTo, "xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content));
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportIncomeReportPdfAsync(IncomeReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetIncomeReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var lines = new List<string>
        {
            $"Period: {report.DateFrom:yyyy-MM-dd} - {report.DateTo:yyyy-MM-dd}",
            $"Accrued: {FormatAmount(report.AccrualTotal)} | Paid: {FormatAmount(report.IncomeTotal)} | Difference: {FormatAmount(report.Debt)} | Rows: {report.RowCount}",
            string.Empty,
            "Type | Date | Month | Garage | Owner | Income type | Accrued | Paid | Difference | Document"
        };
        lines.AddRange(report.Rows.Select(row =>
            string.Join(" | ",
                FormatIncomeRowType(row.RowType),
                row.Date.ToString("yyyy-MM-dd"),
                row.AccountingMonth.ToString("yyyy-MM"),
                row.GarageNumber,
                row.OwnerName ?? string.Empty,
                row.IncomeTypeName,
                FormatAmount(row.AccrualAmount),
                FormatAmount(row.IncomeAmount),
                FormatAmount(row.Debt),
                row.DocumentNumber ?? string.Empty)));

        var content = PdfReportDocumentBuilder.Build("GarageBalance income report", lines);
        return ReportResult<ReportExportFileDto>.Success(new ReportExportFileDto(
            BuildExportFileName("income", report.DateFrom, report.DateTo, "pdf"),
            "application/pdf",
            content));
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportExpenseReportXlsxAsync(ExpenseReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetExpenseReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var content = XlsxWorkbookBuilder.Build(
            [
                new XlsxSheet(
                    "Выплаты",
                    ["Тип", "Дата", "Месяц учета", "Поставщик", "Вид выплаты", "Начислено", "Выплачено", "Разница", "Документ", "Комментарий"],
                    report.Rows.Select(row => (IReadOnlyList<XlsxCell>)
                    [
                        XlsxCell.Text(FormatExpenseRowType(row.RowType)),
                        XlsxCell.Text(row.Date.ToString("yyyy-MM-dd")),
                        XlsxCell.Text(row.AccountingMonth.ToString("yyyy-MM")),
                        XlsxCell.Text(row.SupplierName),
                        XlsxCell.Text(row.ExpenseTypeName),
                        XlsxCell.Number(row.AccrualAmount),
                        XlsxCell.Number(row.ExpenseAmount),
                        XlsxCell.Number(row.Difference),
                        XlsxCell.Text(row.DocumentNumber),
                        XlsxCell.Text(row.Comment)
                    ]).ToArray()),
                new XlsxSheet(
                    "Итоги",
                    ["Период с", "Период по", "Начислено", "Выплачено", "Разница", "Строк"],
                    [
                        [
                            XlsxCell.Text(report.DateFrom.ToString("yyyy-MM-dd")),
                            XlsxCell.Text(report.DateTo.ToString("yyyy-MM-dd")),
                            XlsxCell.Number(report.AccrualTotal),
                            XlsxCell.Number(report.ExpenseTotal),
                            XlsxCell.Number(report.Difference),
                            XlsxCell.Number(report.RowCount)
                        ]
                    ])
            ]);

        return ReportResult<ReportExportFileDto>.Success(new ReportExportFileDto(
            BuildExportFileName("expense", report.DateFrom, report.DateTo, "xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content));
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportExpenseReportPdfAsync(ExpenseReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetExpenseReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var lines = new List<string>
        {
            $"Period: {report.DateFrom:yyyy-MM-dd} - {report.DateTo:yyyy-MM-dd}",
            $"Accrued: {FormatAmount(report.AccrualTotal)} | Paid: {FormatAmount(report.ExpenseTotal)} | Difference: {FormatAmount(report.Difference)} | Rows: {report.RowCount}",
            string.Empty,
            "Type | Date | Month | Supplier | Expense type | Accrued | Paid | Difference | Document"
        };
        lines.AddRange(report.Rows.Select(row =>
            string.Join(" | ",
                FormatExpenseRowType(row.RowType),
                row.Date.ToString("yyyy-MM-dd"),
                row.AccountingMonth.ToString("yyyy-MM"),
                row.SupplierName,
                row.ExpenseTypeName,
                FormatAmount(row.AccrualAmount),
                FormatAmount(row.ExpenseAmount),
                FormatAmount(row.Difference),
                row.DocumentNumber ?? string.Empty)));

        var content = PdfReportDocumentBuilder.Build("GarageBalance expense report", lines);
        return ReportResult<ReportExportFileDto>.Success(new ReportExportFileDto(
            BuildExportFileName("expense", report.DateFrom, report.DateTo, "pdf"),
            "application/pdf",
            content));
    }

    private async Task<GarageReportRowsPage> BuildGarageRowsPageAsync(string? search, DateOnly periodFrom, DateOnly periodTo, int? limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return await BuildGarageRowsWithoutSearchAsync(periodFrom, periodTo, limit, cancellationToken);
        }

        var rows = await BuildGarageRowsAsync(search, periodFrom, periodTo, cancellationToken);
        return new GarageReportRowsPage(rows.Count, ApplyRowLimit(rows, limit));
    }

    private async Task<GarageReportRowsPage> BuildGarageRowsWithoutSearchAsync(DateOnly periodFrom, DateOnly periodTo, int? limit, CancellationToken cancellationToken)
    {
        var garageRowsQuery = dbContext.Garages.AsNoTracking()
            .Where(garage => !garage.IsArchived)
            .Select(garage => new
            {
                GarageId = garage.Id,
                GarageNumber = garage.Number,
                OwnerLastName = garage.Owner == null ? null : garage.Owner.LastName,
                OwnerFirstName = garage.Owner == null ? null : garage.Owner.FirstName,
                OwnerMiddleName = garage.Owner == null ? null : garage.Owner.MiddleName,
                IncomeTotal = dbContext.FinancialOperations.AsNoTracking()
                    .Where(operation =>
                        !operation.IsCanceled &&
                        operation.OperationKind == FinancialOperationKinds.Income &&
                        operation.GarageId == garage.Id &&
                        operation.AccountingMonth >= periodFrom &&
                        operation.AccountingMonth <= periodTo)
                    .Sum(operation => (decimal?)operation.Amount) ?? 0m,
                AccrualTotal = garage.StartingBalance + (dbContext.Accruals.AsNoTracking()
                    .Where(accrual =>
                        !accrual.IsCanceled &&
                        accrual.GarageId == garage.Id &&
                        accrual.AccountingMonth >= periodFrom &&
                        accrual.AccountingMonth <= periodTo)
                    .Sum(accrual => (decimal?)accrual.Amount) ?? 0m),
                MeterReadingCount = dbContext.MeterReadings.AsNoTracking()
                    .Count(reading =>
                        !reading.IsCanceled &&
                        reading.GarageId == garage.Id &&
                        reading.AccountingMonth >= periodFrom &&
                        reading.AccountingMonth <= periodTo)
            })
            .Where(row => row.IncomeTotal != 0 || row.AccrualTotal != 0 || row.MeterReadingCount != 0)
            .OrderBy(row => row.GarageNumber);

        var rowCount = await garageRowsQuery.CountAsync(cancellationToken);
        var visibleRows = await ApplyReportRowLimit(garageRowsQuery, limit)
            .ToListAsync(cancellationToken);

        return new GarageReportRowsPage(
            rowCount,
            visibleRows
                .Select(row => new GarageReportRowDto(
                    row.GarageId,
                    row.GarageNumber,
                    FormatOwnerName(row.OwnerLastName, row.OwnerFirstName, row.OwnerMiddleName),
                    row.IncomeTotal,
                    row.AccrualTotal,
                    row.AccrualTotal - row.IncomeTotal,
                    row.MeterReadingCount))
                .ToList());
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
                var accrualWithStartingBalance = accrual + garage.StartingBalance;
                return new GarageReportRowDto(garage.Id, garage.Number, garage.Owner?.FullName, income, accrualWithStartingBalance, accrualWithStartingBalance - income, readings);
            })
            .Where(row => row.IncomeTotal != 0 || row.AccrualTotal != 0 || row.MeterReadingCount != 0)
            .ToList();
    }

    private async Task<ReportResult<IncomeReportDto>> GetIncomeReportWithoutSearchAsync(
        IncomeReportRequest request,
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        HashSet<Guid> garageIds,
        HashSet<Guid> ownerIds,
        HashSet<Guid> incomeTypeIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<IncomeReportRowDto>();
        var accrualTotal = 0m;
        var incomeTotal = 0m;
        var rowCount = 0;

        if (rowMode is IncomeReportAllRows or IncomeReportAccrualRows)
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

                accrualTotal += await startingBalanceQuery.SumAsync(garage => garage.StartingBalance, cancellationToken);
                rowCount += await startingBalanceQuery.CountAsync(cancellationToken);
                var startingBalanceRows = await ApplyReportRowLimit(startingBalanceQuery.OrderBy(garage => garage.Number), request.Limit)
                    .ToListAsync(cancellationToken);

                rows.AddRange(startingBalanceRows.Select(garage => new IncomeReportRowDto(
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

            accrualTotal += await accrualsQuery.SumAsync(accrual => accrual.Amount, cancellationToken);
            rowCount += await accrualsQuery.CountAsync(cancellationToken);
            var accrualRows = await ApplyReportRowLimit(
                    accrualsQuery
                        .OrderBy(accrual => accrual.AccountingMonth)
                        .ThenBy(accrual => accrual.Garage.Number),
                    request.Limit)
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

            incomeTotal += await paymentsQuery.SumAsync(operation => operation.Amount, cancellationToken);
            rowCount += await paymentsQuery.CountAsync(cancellationToken);
            var paymentRows = await ApplyReportRowLimit(
                    paymentsQuery
                        .OrderBy(operation => operation.OperationDate)
                        .ThenBy(operation => operation.Garage!.Number),
                    request.Limit)
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

        var visibleRows = ApplyRowLimit(
            rows
                .OrderBy(row => row.Date)
                .ThenBy(row => row.GarageNumber)
                .ThenBy(row => row.RowType)
                .ToList(),
            request.Limit);

        return ReportResult<IncomeReportDto>.Success(new IncomeReportDto(
            dateFrom,
            dateTo,
            accrualTotal,
            incomeTotal,
            accrualTotal - incomeTotal,
            rowCount,
            visibleRows));
    }

    private async Task<ReportResult<ExpenseReportDto>> GetExpenseReportWithoutSearchAsync(
        ExpenseReportRequest request,
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        HashSet<Guid> supplierIds,
        HashSet<Guid> expenseTypeIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<ExpenseReportRowDto>();
        var accrualTotal = 0m;
        var expenseTotal = 0m;
        var rowCount = 0;

        if (rowMode is ExpenseReportAllRows or ExpenseReportAccrualRows)
        {
            if (expenseTypeIds.Count == 0)
            {
                var startingBalanceQuery = dbContext.Suppliers.AsNoTracking()
                    .Where(supplier => !supplier.IsArchived && supplier.StartingBalance != 0);

                if (supplierIds.Count > 0)
                {
                    startingBalanceQuery = startingBalanceQuery.Where(supplier => supplierIds.Contains(supplier.Id));
                }

                accrualTotal += await startingBalanceQuery.SumAsync(supplier => supplier.StartingBalance, cancellationToken);
                rowCount += await startingBalanceQuery.CountAsync(cancellationToken);
                var startingBalanceRows = await ApplyReportRowLimit(startingBalanceQuery.OrderBy(supplier => supplier.Name), request.Limit)
                    .ToListAsync(cancellationToken);

                rows.AddRange(startingBalanceRows.Select(supplier => new ExpenseReportRowDto(
                    StartingBalanceRows,
                    dateFrom,
                    dateFrom,
                    supplier.Id,
                    supplier.Name,
                    Guid.Empty,
                    "Стартовый баланс",
                    supplier.StartingBalance,
                    0m,
                    supplier.StartingBalance,
                    null,
                    "Начальное обязательство перед поставщиком")));
            }

            var accrualsQuery = dbContext.SupplierAccruals.AsNoTracking()
                .Include(accrual => accrual.Supplier)
                .Include(accrual => accrual.ExpenseType)
                .Where(accrual =>
                    !accrual.IsCanceled &&
                    accrual.AccountingMonth >= dateFrom &&
                    accrual.AccountingMonth <= dateTo);

            if (supplierIds.Count > 0)
            {
                accrualsQuery = accrualsQuery.Where(accrual => supplierIds.Contains(accrual.SupplierId));
            }

            if (expenseTypeIds.Count > 0)
            {
                accrualsQuery = accrualsQuery.Where(accrual => expenseTypeIds.Contains(accrual.ExpenseTypeId));
            }

            accrualTotal += await accrualsQuery.SumAsync(accrual => accrual.Amount, cancellationToken);
            rowCount += await accrualsQuery.CountAsync(cancellationToken);
            var accrualRows = await ApplyReportRowLimit(
                    accrualsQuery
                        .OrderBy(accrual => accrual.AccountingMonth)
                        .ThenBy(accrual => accrual.Supplier.Name),
                    request.Limit)
                .ToListAsync(cancellationToken);

            rows.AddRange(accrualRows.Select(accrual => new ExpenseReportRowDto(
                ExpenseReportAccrualRows,
                accrual.AccountingMonth,
                accrual.AccountingMonth,
                accrual.SupplierId,
                accrual.Supplier.Name,
                accrual.ExpenseTypeId,
                accrual.ExpenseType.Name,
                accrual.Amount,
                0m,
                accrual.Amount,
                accrual.DocumentNumber,
                accrual.Comment)));
        }

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

            expenseTotal += await paymentsQuery.SumAsync(operation => operation.Amount, cancellationToken);
            rowCount += await paymentsQuery.CountAsync(cancellationToken);
            var paymentRows = await ApplyReportRowLimit(
                    paymentsQuery
                        .OrderBy(operation => operation.OperationDate)
                        .ThenBy(operation => operation.Supplier!.Name),
                    request.Limit)
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

        var visibleRows = ApplyRowLimit(
            rows
                .OrderBy(row => row.Date)
                .ThenBy(row => row.SupplierName)
                .ThenBy(row => row.RowType)
                .ToList(),
            request.Limit);

        return ReportResult<ExpenseReportDto>.Success(new ExpenseReportDto(
            dateFrom,
            dateTo,
            accrualTotal,
            expenseTotal,
            accrualTotal - expenseTotal,
            rowCount,
            visibleRows));
    }

    private static (DateOnly DateFrom, DateOnly DateTo) NormalizeDateRange(DateOnly? dateFrom, DateOnly? dateTo)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = dateFrom ?? MonthPeriod.Normalize(today);
        var end = dateTo ?? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        return (start, end);
    }

    private static string BuildExportFileName(string reportType, DateOnly dateFrom, DateOnly dateTo, string extension)
    {
        return $"garagebalance-{reportType}-{dateFrom:yyyyMMdd}-{dateTo:yyyyMMdd}.{extension}";
    }

    private static IReadOnlyList<T> ApplyRowLimit<T>(IReadOnlyList<T> rows, int? limit)
    {
        return limit is > 0 && rows.Count > limit.Value
            ? rows.Take(NormalizeReportLimit(limit.Value)).ToList()
            : rows;
    }

    private static IQueryable<T> ApplyReportRowLimit<T>(IQueryable<T> query, int? limit)
    {
        return limit is > 0
            ? query.Take(NormalizeReportLimit(limit.Value))
            : query;
    }

    private static int NormalizeReportLimit(int limit) => Math.Clamp(limit, 1, 500);

    private static string FormatAmount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string? FormatOwnerName(string? lastName, string? firstName, string? middleName)
    {
        var fullName = string.Join(' ', new[] { lastName, firstName, middleName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(fullName) ? null : fullName;
    }

    private static string FormatIncomeRowType(string rowType)
    {
        return rowType switch
        {
            StartingBalanceRows => "Стартовый баланс",
            IncomeReportAccrualRows => "Начисление",
            IncomeReportPaymentRows => "Оплата",
            _ => rowType
        };
    }

    private static string FormatExpenseRowType(string rowType)
    {
        return rowType switch
        {
            StartingBalanceRows => "Стартовый баланс",
            ExpenseReportAccrualRows => "Начисление",
            ExpenseReportPaymentRows => "Выплата",
            _ => rowType
        };
    }

    private readonly record struct AmountCountByMonth(DateOnly Month, decimal Amount, int Count);
    private readonly record struct CountByMonth(DateOnly Month, int Count);
    private readonly record struct AmountByGarage(Guid GarageId, decimal Amount);
    private readonly record struct CountByGarage(Guid GarageId, int Count);
    private sealed record GarageReportRowsPage(int RowCount, IReadOnlyList<GarageReportRowDto> Rows);
}
