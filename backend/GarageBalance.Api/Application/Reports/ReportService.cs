using System.Globalization;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Reports;

public sealed class ReportService(GarageBalanceDbContext dbContext, IAuditEventWriter auditEventWriter) : IReportService
{
    private const string IncomeReportAllRows = "all";
    private const string IncomeReportAccrualRows = "accruals";
    private const string IncomeReportPaymentRows = "payments";
    private const string StartingBalanceRows = "starting_balance";
    private const string ExpenseReportAllRows = "all";
    private const string ExpenseReportAccrualRows = "accruals";
    private const string ExpenseReportPaymentRows = "payments";

    public ReportService(GarageBalanceDbContext dbContext)
        : this(dbContext, new AuditEventWriter(dbContext))
    {
    }

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

        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.consolidated_generated",
            "Сводный отчет",
            "generated",
            report.PeriodFrom,
            report.PeriodTo,
            report.GarageRowCount,
            request.Search,
            new Dictionary<string, object?>
            {
                ["limit"] = request.Limit,
                ["monthlyRowCount"] = report.MonthlyRows.Count,
                ["visibleGarageRows"] = report.GarageRows.Count
            },
            cancellationToken);

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

        var file = new ReportExportFileDto(
            BuildExportFileName("consolidated", report.PeriodFrom, report.PeriodTo, "xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Сводный отчет", "xlsx", file.FileName, report.PeriodFrom, report.PeriodTo, report.GarageRowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
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
        var file = new ReportExportFileDto(
            BuildExportFileName("consolidated", report.PeriodFrom, report.PeriodTo, "pdf"),
            "application/pdf",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Сводный отчет", "pdf", file.FileName, report.PeriodFrom, report.PeriodTo, report.GarageRowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
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
                .ThenBy(operation => operation.Id)
                .ToListAsync(cancellationToken);
            var debtAfterPayments = await CalculateIncomeDebtAfterPaymentsAsync(paymentRows, cancellationToken);

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
                operation.Comment,
                operation.CreatedAtUtc,
                debtAfterPayments.GetValueOrDefault(operation.Id))));
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

        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.income_generated",
            "Отчет по поступлениям",
            "generated",
            report.DateFrom,
            report.DateTo,
            report.RowCount,
            request.Search,
            BuildIncomeReportMetadata(request, rowMode, report.Rows.Count),
            cancellationToken);

        return ReportResult<IncomeReportDto>.Success(report);
    }

    private async Task<IReadOnlyDictionary<Guid, decimal>> CalculateIncomeDebtAfterPaymentsAsync(
        IReadOnlyList<FinancialOperation> operations,
        CancellationToken cancellationToken)
    {
        if (operations.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var garageIds = operations
            .Select(operation => operation.GarageId!.Value)
            .Distinct()
            .ToArray();
        var targetAccountingMonths = operations.ToDictionary(operation => operation.Id, operation => operation.AccountingMonth);
        var maxOperationDate = operations.Max(operation => operation.OperationDate);
        var maxAccountingMonth = operations.Max(operation => operation.AccountingMonth);

        var startingBalances = await dbContext.Garages.AsNoTracking()
            .Where(garage => garageIds.Contains(garage.Id))
            .Select(garage => new { garage.Id, garage.StartingBalance })
            .ToDictionaryAsync(garage => garage.Id, garage => garage.StartingBalance, cancellationToken);

        var accrualTotals = await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                garageIds.Contains(accrual.GarageId) &&
                accrual.AccountingMonth <= maxAccountingMonth)
            .GroupBy(accrual => new { accrual.GarageId, accrual.AccountingMonth })
            .Select(group => new IncomeDebtAccrualRow(group.Key.GarageId, group.Key.AccountingMonth, group.Sum(accrual => accrual.Amount)))
            .ToListAsync(cancellationToken);
        var accrualsByGarage = accrualTotals
            .GroupBy(row => row.GarageId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.AccountingMonth).ToArray());

        var relatedPayments = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId != null &&
                garageIds.Contains(operation.GarageId.Value) &&
                operation.OperationDate <= maxOperationDate)
            .Select(operation => new IncomeDebtPaymentRow(
                operation.Id,
                operation.GarageId!.Value,
                operation.OperationDate,
                operation.CreatedAtUtc,
                operation.Amount))
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, decimal>();
        foreach (var garageGroup in relatedPayments
            .GroupBy(payment => payment.GarageId)
            .OrderBy(group => group.Key))
        {
            var paidTotal = 0m;
            var startingBalance = startingBalances.GetValueOrDefault(garageGroup.Key);
            var garageAccruals = accrualsByGarage.GetValueOrDefault(garageGroup.Key) ?? [];

            foreach (var payment in garageGroup
                .OrderBy(payment => payment.OperationDate)
                .ThenBy(payment => payment.CreatedAtUtc)
                .ThenBy(payment => payment.OperationId))
            {
                paidTotal += payment.Amount;
                if (!targetAccountingMonths.TryGetValue(payment.OperationId, out var accountingMonth))
                {
                    continue;
                }

                var accrualTotal = garageAccruals
                    .Where(accrual => accrual.AccountingMonth <= accountingMonth)
                    .Sum(accrual => accrual.Amount);
                result[payment.OperationId] = MoneyMath.RoundMoney(startingBalance + accrualTotal - paidTotal);
            }
        }

        return result;
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

        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.expense_generated",
            "Отчет по выплатам",
            "generated",
            report.DateFrom,
            report.DateTo,
            report.RowCount,
            request.Search,
            BuildExpenseReportMetadata(request, rowMode, report.Rows.Count),
            cancellationToken);

        return ReportResult<ExpenseReportDto>.Success(report);
    }

    public async Task<ReportResult<FundChangeReportDto>> GetFundChangeReportAsync(FundChangeReportRequest request, CancellationToken cancellationToken)
    {
        var (dateFrom, dateTo) = NormalizeDateRange(request.DateFrom, request.DateTo);
        if (dateTo < dateFrom)
        {
            return ReportResult<FundChangeReportDto>.Failure("period_invalid", "Дата окончания отчета не может быть раньше даты начала.");
        }

        var fromUtc = new DateTimeOffset(dateFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toExclusiveUtc = new DateTimeOffset(dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var operationsQuery = dbContext.FundOperations.AsNoTracking()
            .Include(operation => operation.Fund)
            .Where(operation =>
                !operation.IsCanceled &&
                operation.CreatedAtUtc >= fromUtc &&
                operation.CreatedAtUtc < toExclusiveUtc);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var normalizedSearch = request.Search.Trim().ToLower();
            operationsQuery = operationsQuery.Where(operation =>
                operation.Fund.Name.ToLower().Contains(normalizedSearch) ||
                operation.OperationKind.ToLower().Contains(normalizedSearch) ||
                operation.Reason.ToLower().Contains(normalizedSearch));
        }

        List<FundOperationReportRow> operations;
        int rowCount;
        decimal depositTotal;
        decimal withdrawalTotal;
        if (dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            var sqliteOperations = await dbContext.FundOperations.AsNoTracking()
                .Include(operation => operation.Fund)
                .ToListAsync(cancellationToken);
            var filteredOperations = sqliteOperations
                .Where(operation =>
                    !operation.IsCanceled &&
                    operation.CreatedAtUtc >= fromUtc &&
                    operation.CreatedAtUtc < toExclusiveUtc);
            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var normalizedSearch = request.Search.Trim();
                filteredOperations = filteredOperations.Where(operation =>
                    operation.Fund.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    operation.OperationKind.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    operation.Reason.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
            }

            var filteredList = filteredOperations.ToList();
            rowCount = filteredList.Count;
            depositTotal = filteredList
                .Where(operation => operation.OperationKind == FundOperationKinds.Deposit)
                .Sum(operation => operation.Amount);
            withdrawalTotal = filteredList
                .Where(operation => operation.OperationKind == FundOperationKinds.Withdraw)
                .Sum(operation => operation.Amount);
            operations = ApplyEnumerableReportRowLimit(
                    filteredList
                        .OrderBy(operation => operation.CreatedAtUtc)
                        .ThenBy(operation => operation.Fund.Name),
                    request.Limit)
                .Select(CreateFundOperationReportRow)
                .ToList();
        }
        else
        {
            rowCount = await operationsQuery.CountAsync(cancellationToken);
            depositTotal = await operationsQuery
                .Where(operation => operation.OperationKind == FundOperationKinds.Deposit)
                .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;
            withdrawalTotal = await operationsQuery
                .Where(operation => operation.OperationKind == FundOperationKinds.Withdraw)
                .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;
            operations = await ApplyReportRowLimit(
                    operationsQuery
                        .OrderBy(operation => operation.CreatedAtUtc)
                        .ThenBy(operation => operation.Fund.Name),
                    request.Limit)
                .Select(operation => new FundOperationReportRow(
                    operation.Id,
                    operation.FundId,
                    operation.Fund.Name,
                    operation.OperationKind,
                    operation.Amount,
                    operation.BalanceBefore,
                    operation.BalanceAfter,
                    operation.ActorUserId,
                    operation.CreatedAtUtc,
                    operation.Reason))
                .ToListAsync(cancellationToken);
        }
        var actorIds = operations
            .Where(operation => operation.ActorUserId.HasValue)
            .Select(operation => operation.ActorUserId!.Value)
            .Distinct()
            .ToList();
        var usersById = actorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Users.AsNoTracking()
                .Where(user => actorIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        var rows = operations
            .Select(operation => new FundChangeReportRowDto(
                operation.Id,
                operation.FundId,
                operation.FundName,
                DateOnly.FromDateTime(operation.CreatedAtUtc.UtcDateTime),
                operation.OperationKind,
                FormatFundOperationKind(operation.OperationKind),
                operation.Amount,
                operation.BalanceBefore,
                operation.BalanceAfter,
                operation.ActorUserId,
                operation.ActorUserId.HasValue && usersById.TryGetValue(operation.ActorUserId.Value, out var displayName)
                    ? displayName
                    : operation.ActorUserId?.ToString(),
                operation.Reason))
            .ToList();
        var report = new FundChangeReportDto(dateFrom, dateTo, depositTotal, withdrawalTotal, rowCount, rows);

        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.fund_changes_generated",
            "Отчет по изменению фондов",
            "generated",
            report.DateFrom,
            report.DateTo,
            report.RowCount,
            request.Search,
            new Dictionary<string, object?>
            {
                ["reportType"] = "fund_changes",
                ["visibleRowCount"] = report.Rows.Count,
                ["depositTotal"] = report.DepositTotal,
                ["withdrawalTotal"] = report.WithdrawalTotal,
                ["limit"] = request.Limit
            },
            cancellationToken);

        return ReportResult<FundChangeReportDto>.Success(report);
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportFundChangeReportXlsxAsync(FundChangeReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetFundChangeReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var content = XlsxWorkbookBuilder.Build(
            [
                new XlsxSheet(
                    "Изменение фондов",
                    ["Фонд", "Дата", "Операция", "Сумма", "Сумма до", "Сумма после", "Пользователь", "Комментарий"],
                    report.Rows.Select(row => (IReadOnlyList<XlsxCell>)
                    [
                        XlsxCell.Text(row.FundName),
                        XlsxCell.Text(row.Date.ToString("yyyy-MM-dd")),
                        XlsxCell.Text(row.ChangeName),
                        XlsxCell.Number(row.Amount),
                        XlsxCell.Number(row.BalanceBefore),
                        XlsxCell.Number(row.BalanceAfter),
                        XlsxCell.Text(row.ActorDisplayName),
                        XlsxCell.Text(row.Reason)
                    ]).ToArray()),
                new XlsxSheet(
                    "Итоги",
                    ["Период с", "Период по", "Пополнено", "Изъято", "Строк"],
                    [
                        [
                            XlsxCell.Text(report.DateFrom.ToString("yyyy-MM-dd")),
                            XlsxCell.Text(report.DateTo.ToString("yyyy-MM-dd")),
                            XlsxCell.Number(report.DepositTotal),
                            XlsxCell.Number(report.WithdrawalTotal),
                            XlsxCell.Number(report.RowCount)
                        ]
                    ])
            ]);

        var file = new ReportExportFileDto(
            BuildExportFileName("fund-changes", report.DateFrom, report.DateTo, "xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Отчет по изменению фондов", "xlsx", file.FileName, report.DateFrom, report.DateTo, report.RowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportFundChangeReportPdfAsync(FundChangeReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetFundChangeReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var lines = new List<string>
        {
            $"Period: {report.DateFrom:yyyy-MM-dd} - {report.DateTo:yyyy-MM-dd}",
            $"Deposits: {FormatAmount(report.DepositTotal)} | Withdrawals: {FormatAmount(report.WithdrawalTotal)} | Rows: {report.RowCount}",
            string.Empty,
            "Fund | Date | Operation | Amount | Before | After | User | Comment"
        };
        lines.AddRange(report.Rows.Select(row =>
            string.Join(" | ",
                row.FundName,
                row.Date.ToString("yyyy-MM-dd"),
                row.ChangeName,
                FormatAmount(row.Amount),
                FormatAmount(row.BalanceBefore),
                FormatAmount(row.BalanceAfter),
                row.ActorDisplayName ?? string.Empty,
                row.Reason)));

        var content = PdfReportDocumentBuilder.Build("GarageBalance fund changes report", lines);
        var file = new ReportExportFileDto(
            BuildExportFileName("fund-changes", report.DateFrom, report.DateTo, "pdf"),
            "application/pdf",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Отчет по изменению фондов", "pdf", file.FileName, report.DateFrom, report.DateTo, report.RowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
    }

    public async Task<ReportResult<CashPaymentReportDto>> GetCashPaymentReportAsync(CashPaymentReportRequest request, CancellationToken cancellationToken)
    {
        var (dateFrom, dateTo) = NormalizeDateRange(request.DateFrom, request.DateTo);
        if (dateTo < dateFrom)
        {
            return ReportResult<CashPaymentReportDto>.Failure("period_invalid", "Дата окончания отчета не может быть раньше даты начала.");
        }

        var operationsQuery = dbContext.FinancialOperations.AsNoTracking()
            .Include(operation => operation.Supplier)
            .Include(operation => operation.ExpenseType)
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.OperationDate >= dateFrom &&
                operation.OperationDate <= dateTo);

        var operations = await operationsQuery
            .OrderBy(operation => operation.OperationDate)
            .ThenBy(operation => operation.DocumentNumber)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var normalizedSearch = request.Search.Trim();
            operations = operations
                .Where(operation =>
                    (operation.Supplier?.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (operation.ExpenseType?.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (operation.DocumentNumber?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (operation.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        var rowCount = operations.Count;
        var visibleOperations = ApplyEnumerableReportRowLimit(operations, request.Limit).ToList();
        var rows = visibleOperations
            .Select(operation => new CashPaymentReportRowDto(
                operation.Id,
                operation.OperationDate,
                operation.Amount,
                !string.IsNullOrWhiteSpace(operation.DocumentNumber),
                BuildCashPaymentPurpose(operation),
                operation.Supplier?.Name,
                operation.ExpenseType?.Name,
                operation.DocumentNumber,
                operation.Comment))
            .ToList();
        var report = new CashPaymentReportDto(dateFrom, dateTo, operations.Sum(operation => operation.Amount), rowCount, rows);

        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.cash_payments_generated",
            "Отчет по оплатам из кассы",
            "generated",
            report.DateFrom,
            report.DateTo,
            report.RowCount,
            request.Search,
            new Dictionary<string, object?>
            {
                ["reportType"] = "cash_payments",
                ["visibleRowCount"] = report.Rows.Count,
                ["total"] = report.Total,
                ["limit"] = request.Limit
            },
            cancellationToken);

        return ReportResult<CashPaymentReportDto>.Success(report);
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportCashPaymentReportXlsxAsync(CashPaymentReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetCashPaymentReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var content = XlsxWorkbookBuilder.Build(
            [
                new XlsxSheet(
                    "Оплаты из кассы",
                    ["Дата", "Сумма", "Наличие чека", "Назначение", "Поставщик", "Услуга", "Документ", "Комментарий"],
                    report.Rows.Select(row => (IReadOnlyList<XlsxCell>)
                    [
                        XlsxCell.Text(row.Date.ToString("yyyy-MM-dd")),
                        XlsxCell.Number(row.Amount),
                        XlsxCell.Text(row.HasReceipt ? "Да" : "Нет"),
                        XlsxCell.Text(row.Purpose),
                        XlsxCell.Text(row.SupplierName),
                        XlsxCell.Text(row.ExpenseTypeName),
                        XlsxCell.Text(row.DocumentNumber),
                        XlsxCell.Text(row.Comment)
                    ]).ToArray()),
                new XlsxSheet(
                    "Итоги",
                    ["Период с", "Период по", "Сумма", "Строк"],
                    [
                        [
                            XlsxCell.Text(report.DateFrom.ToString("yyyy-MM-dd")),
                            XlsxCell.Text(report.DateTo.ToString("yyyy-MM-dd")),
                            XlsxCell.Number(report.Total),
                            XlsxCell.Number(report.RowCount)
                        ]
                    ])
            ]);

        var file = new ReportExportFileDto(
            BuildExportFileName("cash-payments", report.DateFrom, report.DateTo, "xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Отчет по оплатам из кассы", "xlsx", file.FileName, report.DateFrom, report.DateTo, report.RowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportCashPaymentReportPdfAsync(CashPaymentReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetCashPaymentReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var lines = new List<string>
        {
            $"Period: {report.DateFrom:yyyy-MM-dd} - {report.DateTo:yyyy-MM-dd}",
            $"Total: {FormatAmount(report.Total)} | Rows: {report.RowCount}",
            string.Empty,
            "Date | Amount | Receipt | Document | Purpose | Comment"
        };
        lines.AddRange(report.Rows.Select(row =>
            string.Join(" | ",
                row.Date.ToString("yyyy-MM-dd"),
                FormatAmount(row.Amount),
                row.HasReceipt ? "yes" : "no",
                row.DocumentNumber ?? string.Empty,
                row.Purpose,
                row.Comment ?? string.Empty)));

        var content = PdfReportDocumentBuilder.Build("GarageBalance cash payments report", lines);
        var file = new ReportExportFileDto(
            BuildExportFileName("cash-payments", report.DateFrom, report.DateTo, "pdf"),
            "application/pdf",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Отчет по оплатам из кассы", "pdf", file.FileName, report.DateFrom, report.DateTo, report.RowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
    }

    public async Task<ReportResult<BankDepositReportDto>> GetBankDepositReportAsync(BankDepositReportRequest request, CancellationToken cancellationToken)
    {
        var (dateFrom, dateTo) = NormalizeDateRange(request.DateFrom, request.DateTo);
        if (dateTo < dateFrom)
        {
            return ReportResult<BankDepositReportDto>.Failure("period_invalid", "Дата окончания отчета не может быть раньше даты начала.");
        }

        var fromUtc = new DateTimeOffset(dateFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toExclusiveUtc = new DateTimeOffset(dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var operationsQuery = dbContext.FundOperations.AsNoTracking()
            .Include(operation => operation.Fund)
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FundOperationKinds.Deposit &&
                operation.CreatedAtUtc >= fromUtc &&
                operation.CreatedAtUtc < toExclusiveUtc);

        List<FundOperation> operations;
        if (dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            operations = await dbContext.FundOperations.AsNoTracking()
                .Include(operation => operation.Fund)
                .ToListAsync(cancellationToken);
            operations = operations
                .Where(operation =>
                    !operation.IsCanceled &&
                    operation.OperationKind == FundOperationKinds.Deposit &&
                    operation.CreatedAtUtc >= fromUtc &&
                    operation.CreatedAtUtc < toExclusiveUtc)
                .ToList();
        }
        else
        {
            operations = await operationsQuery
                .OrderBy(operation => operation.CreatedAtUtc)
                .ThenBy(operation => operation.Fund.Name)
                .ToListAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var normalizedSearch = request.Search.Trim();
            operations = operations
                .Where(operation =>
                    operation.Fund.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    operation.Reason.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        operations = operations
            .OrderBy(operation => operation.CreatedAtUtc)
            .ThenBy(operation => operation.Fund.Name)
            .ToList();

        var rowCount = operations.Count;
        var rows = ApplyEnumerableReportRowLimit(operations, request.Limit)
            .Select(operation => new BankDepositReportRowDto(
                operation.Id,
                DateOnly.FromDateTime(operation.CreatedAtUtc.UtcDateTime),
                operation.Amount,
                operation.Fund.Name,
                operation.Reason))
            .ToList();
        var report = new BankDepositReportDto(dateFrom, dateTo, operations.Sum(operation => operation.Amount), rowCount, rows);

        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.bank_deposits_generated",
            "Отчет по сдаче кассы в банк",
            "generated",
            report.DateFrom,
            report.DateTo,
            report.RowCount,
            request.Search,
            new Dictionary<string, object?>
            {
                ["reportType"] = "bank_deposits",
                ["visibleRowCount"] = report.Rows.Count,
                ["total"] = report.Total,
                ["limit"] = request.Limit
            },
            cancellationToken);

        return ReportResult<BankDepositReportDto>.Success(report);
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportBankDepositReportXlsxAsync(BankDepositReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetBankDepositReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var content = XlsxWorkbookBuilder.Build(
            [
                new XlsxSheet(
                    "Сдача кассы в банк",
                    ["Дата", "Сумма", "Фонд", "Комментарий"],
                    report.Rows.Select(row => (IReadOnlyList<XlsxCell>)
                    [
                        XlsxCell.Text(row.Date.ToString("yyyy-MM-dd")),
                        XlsxCell.Number(row.Amount),
                        XlsxCell.Text(row.FundName),
                        XlsxCell.Text(row.Comment)
                    ]).ToArray()),
                new XlsxSheet(
                    "Итоги",
                    ["Период с", "Период по", "Сумма", "Строк"],
                    [
                        [
                            XlsxCell.Text(report.DateFrom.ToString("yyyy-MM-dd")),
                            XlsxCell.Text(report.DateTo.ToString("yyyy-MM-dd")),
                            XlsxCell.Number(report.Total),
                            XlsxCell.Number(report.RowCount)
                        ]
                    ])
            ]);

        var file = new ReportExportFileDto(
            BuildExportFileName("bank-deposits", report.DateFrom, report.DateTo, "xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Отчет по сдаче кассы в банк", "xlsx", file.FileName, report.DateFrom, report.DateTo, report.RowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportBankDepositReportPdfAsync(BankDepositReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetBankDepositReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var lines = new List<string>
        {
            $"Period: {report.DateFrom:yyyy-MM-dd} - {report.DateTo:yyyy-MM-dd}",
            $"Total: {FormatAmount(report.Total)} | Rows: {report.RowCount}",
            string.Empty,
            "Date | Amount | Fund | Comment"
        };
        lines.AddRange(report.Rows.Select(row =>
            string.Join(" | ",
                row.Date.ToString("yyyy-MM-dd"),
                FormatAmount(row.Amount),
                row.FundName ?? string.Empty,
                row.Comment ?? string.Empty)));

        var content = PdfReportDocumentBuilder.Build("GarageBalance bank deposits report", lines);
        var file = new ReportExportFileDto(
            BuildExportFileName("bank-deposits", report.DateFrom, report.DateTo, "pdf"),
            "application/pdf",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Отчет по сдаче кассы в банк", "pdf", file.FileName, report.DateFrom, report.DateTo, report.RowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
    }

    public async Task<ReportResult<FeeReportDto>> GetFeeReportAsync(FeeReportRequest request, CancellationToken cancellationToken)
    {
        var variation = request.Variation?.Trim();
        var campaigns = await dbContext.FeeCampaigns.AsNoTracking()
            .Include(campaign => campaign.IncomeType)
            .Where(campaign => !campaign.IsArchived && !campaign.IncomeType.IsArchived)
            .OrderBy(campaign => campaign.StartsOn)
            .ThenBy(campaign => campaign.Name)
            .ToListAsync(cancellationToken);
        var hasFeeCampaigns = campaigns.Count > 0;

        if (hasFeeCampaigns && !string.IsNullOrWhiteSpace(variation))
        {
            var normalizedVariation = variation.ToUpperInvariant();
            campaigns = campaigns
                .Where(campaign =>
                    campaign.Name.ToUpperInvariant().Contains(normalizedVariation, StringComparison.Ordinal) ||
                    (campaign.Goal != null && campaign.Goal.ToUpperInvariant().Contains(normalizedVariation, StringComparison.Ordinal)) ||
                    campaign.IncomeType.Name.ToUpperInvariant().Contains(normalizedVariation, StringComparison.Ordinal))
                .ToList();
        }

        var campaignByIncomeType = campaigns
            .GroupBy(campaign => campaign.IncomeTypeId)
            .ToDictionary(group => group.Key, group => group.First());
        var incomeTypes = hasFeeCampaigns
            ? campaigns
                .Select(campaign => campaign.IncomeType)
                .DistinctBy(incomeType => incomeType.Id)
                .OrderBy(incomeType => incomeType.Name)
                .ToList()
            : await dbContext.IncomeTypes.AsNoTracking()
                .Where(incomeType => !incomeType.IsArchived)
                .OrderBy(incomeType => incomeType.Name)
                .ToListAsync(cancellationToken);

        if (!hasFeeCampaigns && !string.IsNullOrWhiteSpace(variation))
        {
            var normalizedVariation = variation.ToUpperInvariant();
            incomeTypes = incomeTypes
                .Where(incomeType => incomeType.Name.ToUpperInvariant().Contains(normalizedVariation, StringComparison.Ordinal))
                .ToList();
        }

        var incomeTypeIds = incomeTypes.Select(incomeType => incomeType.Id).ToList();

        if (incomeTypeIds.Count == 0)
        {
            var emptyReport = new FeeReportDto(variation ?? "Все сборы", 0m, 0m, 0m, 0, [], [], []);
            await AddFeeReportAuditAsync(request, emptyReport, cancellationToken);
            return ReportResult<FeeReportDto>.Success(emptyReport);
        }

        var accrualTotals = await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && incomeTypeIds.Contains(accrual.IncomeTypeId))
            .GroupBy(accrual => accrual.IncomeTypeId)
            .Select(group => new { IncomeTypeId = group.Key, Amount = group.Sum(accrual => accrual.Amount) })
            .ToDictionaryAsync(row => row.IncomeTypeId, row => row.Amount, cancellationToken);
        var collectedTotals = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.IncomeTypeId.HasValue &&
                incomeTypeIds.Contains(operation.IncomeTypeId.Value))
            .GroupBy(operation => operation.IncomeTypeId!.Value)
            .Select(group => new { IncomeTypeId = group.Key, Amount = group.Sum(operation => operation.Amount) })
            .ToDictionaryAsync(row => row.IncomeTypeId, row => row.Amount, cancellationToken);

        var summaryRows = incomeTypes
            .Select(incomeType =>
            {
                accrualTotals.TryGetValue(incomeType.Id, out var accrued);
                collectedTotals.TryGetValue(incomeType.Id, out var collected);
                if (campaignByIncomeType.TryGetValue(incomeType.Id, out var campaign))
                {
                    return new FeeReportSummaryRowDto(
                        incomeType.Id,
                        campaign.Name,
                        string.IsNullOrWhiteSpace(campaign.Goal) ? BuildFeeGoal(campaign.Name) : campaign.Goal.Trim(),
                        campaign.TargetAmount,
                        collected);
                }

                return new FeeReportSummaryRowDto(incomeType.Id, incomeType.Name, BuildFeeGoal(incomeType.Name), accrued, collected);
            })
            .Where(row => row.FeeAmount != 0 || row.Collected != 0 || !string.IsNullOrWhiteSpace(variation))
            .ToList();

        var accrualsByGarage = await dbContext.Accruals.AsNoTracking()
            .Include(accrual => accrual.Garage)
            .ThenInclude(garage => garage.Owner)
            .Where(accrual => !accrual.IsCanceled && incomeTypeIds.Contains(accrual.IncomeTypeId))
            .GroupBy(accrual => new
            {
                accrual.GarageId,
                accrual.Garage.Number,
                OwnerLastName = accrual.Garage.Owner != null ? accrual.Garage.Owner.LastName : null,
                OwnerFirstName = accrual.Garage.Owner != null ? accrual.Garage.Owner.FirstName : null,
                OwnerMiddleName = accrual.Garage.Owner != null ? accrual.Garage.Owner.MiddleName : null,
                accrual.IncomeTypeId
            })
            .Select(group => new
            {
                group.Key.GarageId,
                GarageNumber = group.Key.Number,
                group.Key.OwnerLastName,
                group.Key.OwnerFirstName,
                group.Key.OwnerMiddleName,
                group.Key.IncomeTypeId,
                Accrued = group.Sum(accrual => accrual.Amount)
            })
            .ToListAsync(cancellationToken);
        var paymentsByGarage = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId.HasValue &&
                operation.IncomeTypeId.HasValue &&
                incomeTypeIds.Contains(operation.IncomeTypeId.Value))
            .GroupBy(operation => new { GarageId = operation.GarageId!.Value, IncomeTypeId = operation.IncomeTypeId!.Value })
            .Select(group => new
            {
                group.Key.GarageId,
                group.Key.IncomeTypeId,
                Paid = group.Sum(operation => operation.Amount),
                LastPaymentDate = group.Max(operation => (DateOnly?)operation.OperationDate)
            })
            .ToListAsync(cancellationToken);
        var allGarageIds = accrualsByGarage
            .Select(row => row.GarageId)
            .Concat(paymentsByGarage.Select(row => row.GarageId))
            .Distinct()
            .ToList();
        var garageLookup = await dbContext.Garages.AsNoTracking()
            .Include(garage => garage.Owner)
            .Where(garage => allGarageIds.Contains(garage.Id))
            .Select(garage => new
            {
                garage.Id,
                garage.Number,
                OwnerLastName = garage.Owner != null ? garage.Owner.LastName : null,
                OwnerFirstName = garage.Owner != null ? garage.Owner.FirstName : null,
                OwnerMiddleName = garage.Owner != null ? garage.Owner.MiddleName : null
            })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var accrualLookup = accrualsByGarage.ToDictionary(row => (row.GarageId, row.IncomeTypeId));
        var paymentLookup = paymentsByGarage.ToDictionary(row => (row.GarageId, row.IncomeTypeId));
        var incomeTypeNames = incomeTypes.ToDictionary(
            incomeType => incomeType.Id,
            incomeType => campaignByIncomeType.TryGetValue(incomeType.Id, out var campaign) ? campaign.Name : incomeType.Name);
        var feeGarageRows = accrualLookup.Keys
            .Concat(paymentLookup.Keys)
            .Distinct()
            .Select(key =>
            {
                accrualLookup.TryGetValue(key, out var accrual);
                paymentLookup.TryGetValue(key, out var payment);
                garageLookup.TryGetValue(key.GarageId, out var garage);
                var accrued = accrual?.Accrued ?? 0m;
                var paid = payment?.Paid ?? 0m;
                return new FeeReportGarageRowDto(
                    key.GarageId,
                    accrual?.GarageNumber ?? garage?.Number ?? string.Empty,
                    FormatOwnerName(
                        accrual?.OwnerLastName ?? garage?.OwnerLastName,
                        accrual?.OwnerFirstName ?? garage?.OwnerFirstName,
                        accrual?.OwnerMiddleName ?? garage?.OwnerMiddleName),
                    key.IncomeTypeId,
                    incomeTypeNames[key.IncomeTypeId],
                    accrued,
                    paid,
                    payment?.LastPaymentDate,
                    accrued - paid);
            })
            .OrderBy(row => row.FeeName)
            .ThenBy(row => row.GarageNumber)
            .ToList();
        var debtorRows = feeGarageRows
            .Where(row => row.Debt > 0)
            .Select(row => new FeeReportDebtorRowDto(
                row.GarageId,
                row.GarageNumber,
                row.OwnerName,
                row.IncomeTypeId,
                row.FeeName,
                row.Paid,
                row.LastPaymentDate,
                row.Debt))
            .OrderBy(row => row.FeeName)
            .ThenBy(row => row.GarageNumber)
            .ToList();

        var rowCount = summaryRows.Count + feeGarageRows.Count;
        var visibleSummaryRows = ApplyRowLimit(summaryRows, request.Limit);
        var visibleGarageRows = ApplyRowLimit(feeGarageRows, request.Limit);
        var visibleDebtorRows = ApplyRowLimit(debtorRows, request.Limit);
        var report = new FeeReportDto(
            variation ?? "Все сборы",
            summaryRows.Sum(row => row.FeeAmount),
            summaryRows.Sum(row => row.Collected),
            debtorRows.Sum(row => row.Debt),
            rowCount,
            visibleSummaryRows,
            visibleGarageRows,
            visibleDebtorRows);

        await AddFeeReportAuditAsync(request, report, cancellationToken);

        return ReportResult<FeeReportDto>.Success(report);
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportFeeReportXlsxAsync(FeeReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetFeeReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var content = XlsxWorkbookBuilder.Build(
            [
                new XlsxSheet(
                    "Сборы",
                    ["Наименование", "Цель", "Сумма сбора", "Собрано", "Задолженность"],
                    report.SummaryRows.Select(row => (IReadOnlyList<XlsxCell>)
                    [
                        XlsxCell.Text(row.Name),
                        XlsxCell.Text(row.Goal),
                        XlsxCell.Number(row.FeeAmount),
                        XlsxCell.Number(row.Collected),
                        XlsxCell.Number(Math.Max(row.FeeAmount - row.Collected, 0m))
                    ]).ToArray()),
                new XlsxSheet(
                    "Гаражи",
                    ["Сбор", "Гараж", "Владелец", "Начислено", "Оплачено", "Дата платежа", "Задолженность"],
                    report.GarageRows.Select(row => (IReadOnlyList<XlsxCell>)
                    [
                        XlsxCell.Text(row.FeeName),
                        XlsxCell.Text(row.GarageNumber),
                        XlsxCell.Text(row.OwnerName),
                        XlsxCell.Number(row.Accrued),
                        XlsxCell.Number(row.Paid),
                        XlsxCell.Text(row.LastPaymentDate?.ToString("yyyy-MM-dd") ?? string.Empty),
                        XlsxCell.Number(row.Debt)
                    ]).ToArray()),
                new XlsxSheet(
                    "Должники",
                    ["Сбор", "Гараж", "Владелец", "Оплачено", "Дата платежа", "Задолженность"],
                    report.DebtorRows.Select(row => (IReadOnlyList<XlsxCell>)
                    [
                        XlsxCell.Text(row.FeeName),
                        XlsxCell.Text(row.GarageNumber),
                        XlsxCell.Text(row.OwnerName),
                        XlsxCell.Number(row.Paid),
                        XlsxCell.Text(row.LastPaymentDate?.ToString("yyyy-MM-dd") ?? string.Empty),
                        XlsxCell.Number(row.Debt)
                    ]).ToArray()),
                new XlsxSheet(
                    "Итоги",
                    ["Вариация", "Начислено", "Собрано", "Задолженность", "Строк"],
                    [
                        [
                            XlsxCell.Text(report.Variation),
                            XlsxCell.Number(report.AccruedTotal),
                            XlsxCell.Number(report.CollectedTotal),
                            XlsxCell.Number(report.DebtTotal),
                            XlsxCell.Number(report.RowCount)
                        ]
                    ])
            ]);

        var file = new ReportExportFileDto(
            BuildSnapshotExportFileName("fees", "xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        await AddFeeReportExportAuditAsync(request, report, "xlsx", file.FileName, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportFeeReportPdfAsync(FeeReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await GetFeeReportAsync(request, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var lines = new List<string>
        {
            $"Variation: {report.Variation}",
            $"Accrued: {FormatAmount(report.AccruedTotal)} | Collected: {FormatAmount(report.CollectedTotal)} | Debt: {FormatAmount(report.DebtTotal)} | Rows: {report.RowCount}",
            string.Empty,
            "Fees",
            "Name | Goal | Accrued | Collected"
        };
        lines.AddRange(report.SummaryRows.Select(row =>
            string.Join(" | ",
                row.Name,
                row.Goal,
                FormatAmount(row.FeeAmount),
                FormatAmount(row.Collected))));
        lines.Add(string.Empty);
        lines.Add("Garages");
        lines.Add("Fee | Garage | Owner | Accrued | Paid | Payment date | Debt");
        lines.AddRange(report.GarageRows.Select(row =>
            string.Join(" | ",
                row.FeeName,
                row.GarageNumber,
                row.OwnerName ?? string.Empty,
                FormatAmount(row.Accrued),
                FormatAmount(row.Paid),
                row.LastPaymentDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                FormatAmount(row.Debt))));
        lines.Add(string.Empty);
        lines.Add("Debtors");
        lines.Add("Fee | Garage | Owner | Paid | Payment date | Debt");
        lines.AddRange(report.DebtorRows.Select(row =>
            string.Join(" | ",
                row.FeeName,
                row.GarageNumber,
                row.OwnerName ?? string.Empty,
                FormatAmount(row.Paid),
                row.LastPaymentDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                FormatAmount(row.Debt))));

        var content = PdfReportDocumentBuilder.Build("GarageBalance fees report", lines);
        var file = new ReportExportFileDto(
            BuildSnapshotExportFileName("fees", "pdf"),
            "application/pdf",
            content);
        await AddFeeReportExportAuditAsync(request, report, "pdf", file.FileName, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
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
                    ["Тип", "Дата", "Месяц учета", "Гараж", "Владелец", "Вид поступления", "Начислено", "Оплачено", "Разница", "Остаток после платежа", "Документ", "Комментарий"],
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
                        row.DebtAfterPayment.HasValue ? XlsxCell.Number(row.DebtAfterPayment.Value) : XlsxCell.Text(string.Empty),
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

        var file = new ReportExportFileDto(
            BuildExportFileName("income", report.DateFrom, report.DateTo, "xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Отчет по поступлениям", "xlsx", file.FileName, report.DateFrom, report.DateTo, report.RowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
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
            "Date | Garage | Paid | Debt after payment | Document | Income type"
        };
        lines.AddRange(report.Rows.Select(row =>
            string.Join(" | ",
                row.Date.ToString("yyyy-MM-dd"),
                row.GarageNumber,
                FormatAmount(row.IncomeAmount),
                row.DebtAfterPayment.HasValue ? FormatAmount(row.DebtAfterPayment.Value) : string.Empty,
                row.DocumentNumber ?? string.Empty,
                row.IncomeTypeName)));

        var content = PdfReportDocumentBuilder.Build("GarageBalance income report", lines);
        var file = new ReportExportFileDto(
            BuildExportFileName("income", report.DateFrom, report.DateTo, "pdf"),
            "application/pdf",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Отчет по поступлениям", "pdf", file.FileName, report.DateFrom, report.DateTo, report.RowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
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

        var file = new ReportExportFileDto(
            BuildExportFileName("expense", report.DateFrom, report.DateTo, "xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Отчет по выплатам", "xlsx", file.FileName, report.DateFrom, report.DateTo, report.RowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
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
        var file = new ReportExportFileDto(
            BuildExportFileName("expense", report.DateFrom, report.DateTo, "pdf"),
            "application/pdf",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Отчет по выплатам", "pdf", file.FileName, report.DateFrom, report.DateTo, report.RowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
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
                        .ThenBy(operation => operation.Garage!.Number)
                        .ThenBy(operation => operation.Id),
                    request.Limit)
                .ToListAsync(cancellationToken);
            var debtAfterPayments = await CalculateIncomeDebtAfterPaymentsAsync(paymentRows, cancellationToken);

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
                operation.Comment,
                operation.CreatedAtUtc,
                debtAfterPayments.GetValueOrDefault(operation.Id))));
        }

        var visibleRows = ApplyRowLimit(
            rows
                .OrderBy(row => row.Date)
                .ThenBy(row => row.GarageNumber)
                .ThenBy(row => row.RowType)
                .ToList(),
            request.Limit);

        var report = new IncomeReportDto(
            dateFrom,
            dateTo,
            accrualTotal,
            incomeTotal,
            accrualTotal - incomeTotal,
            rowCount,
            visibleRows);

        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.income_generated",
            "Отчет по поступлениям",
            "generated",
            report.DateFrom,
            report.DateTo,
            report.RowCount,
            request.Search,
            BuildIncomeReportMetadata(request, rowMode, report.Rows.Count),
            cancellationToken);

        return ReportResult<IncomeReportDto>.Success(report);
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

        var report = new ExpenseReportDto(
            dateFrom,
            dateTo,
            accrualTotal,
            expenseTotal,
            accrualTotal - expenseTotal,
            rowCount,
            visibleRows);

        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.expense_generated",
            "Отчет по выплатам",
            "generated",
            report.DateFrom,
            report.DateTo,
            report.RowCount,
            request.Search,
            BuildExpenseReportMetadata(request, rowMode, report.Rows.Count),
            cancellationToken);

        return ReportResult<ExpenseReportDto>.Success(report);
    }

    private async Task AddReportExportAuditAsync(
        Guid? actorUserId,
        string reportTitle,
        string format,
        string fileName,
        DateOnly dateFrom,
        DateOnly dateTo,
        int rowCount,
        string? search,
        CancellationToken cancellationToken)
    {
        await AddReportAuditAsync(
            actorUserId,
            $"reports.{NormalizeReportActionName(reportTitle)}_exported",
            reportTitle,
            "exported",
            dateFrom,
            dateTo,
            rowCount,
            search,
            new Dictionary<string, object?>
            {
                ["format"] = format,
                ["fileName"] = fileName
            },
            cancellationToken);
    }

    private async Task AddReportAuditAsync(
        Guid? actorUserId,
        string action,
        string reportTitle,
        string operation,
        DateOnly dateFrom,
        DateOnly dateTo,
        int rowCount,
        string? search,
        IReadOnlyDictionary<string, object?> metadata,
        CancellationToken cancellationToken)
    {
        var period = dateFrom == dateTo
            ? dateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : $"{dateFrom:yyyy-MM-dd} - {dateTo:yyyy-MM-dd}";
        var allMetadata = new Dictionary<string, object?>(metadata, StringComparer.Ordinal)
        {
            ["reportTitle"] = reportTitle,
            ["operation"] = operation,
            ["periodFrom"] = dateFrom,
            ["periodTo"] = dateTo,
            ["rowCount"] = rowCount,
            ["hasSearch"] = !string.IsNullOrWhiteSpace(search),
            ["searchLength"] = string.IsNullOrWhiteSpace(search) ? 0 : search.Trim().Length
        };

        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            action,
            "report",
            NormalizeReportActionName(reportTitle),
            operation == "exported"
                ? $"Выгружен отчет \"{reportTitle}\" за период {period}."
                : $"Сформирован отчет \"{reportTitle}\" за период {period}.",
            EntityDisplayName: reportTitle,
            RelatedAccountingMonth: dateFrom.Year == dateTo.Year && dateFrom.Month == dateTo.Month
                ? dateFrom.ToString("yyyy-MM", CultureInfo.InvariantCulture)
                : null,
            Metadata: allMetadata));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Dictionary<string, object?> BuildIncomeReportMetadata(IncomeReportRequest request, string rowMode, int visibleRowCount)
    {
        return new Dictionary<string, object?>
        {
            ["reportType"] = "income",
            ["rowMode"] = rowMode,
            ["visibleRowCount"] = visibleRowCount,
            ["garageFilterCount"] = request.GarageIds.Count,
            ["ownerFilterCount"] = request.OwnerIds.Count,
            ["incomeTypeFilterCount"] = request.IncomeTypeIds.Count,
            ["limit"] = request.Limit
        };
    }

    private static Dictionary<string, object?> BuildExpenseReportMetadata(ExpenseReportRequest request, string rowMode, int visibleRowCount)
    {
        return new Dictionary<string, object?>
        {
            ["reportType"] = "expense",
            ["rowMode"] = rowMode,
            ["visibleRowCount"] = visibleRowCount,
            ["supplierFilterCount"] = request.SupplierIds.Count,
            ["expenseTypeFilterCount"] = request.ExpenseTypeIds.Count,
            ["limit"] = request.Limit
        };
    }

    private async Task AddFeeReportAuditAsync(FeeReportRequest request, FeeReportDto report, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.fees_generated",
            "Отчет по сборам",
            "generated",
            today,
            today,
            report.RowCount,
            request.Variation,
            new Dictionary<string, object?>
            {
                ["reportType"] = "fees",
                ["visibleSummaryRowCount"] = report.SummaryRows.Count,
                ["visibleDebtorRowCount"] = report.DebtorRows.Count,
                ["accruedTotal"] = report.AccruedTotal,
                ["collectedTotal"] = report.CollectedTotal,
                ["debtTotal"] = report.DebtTotal,
                ["variation"] = report.Variation,
                ["limit"] = request.Limit
            },
            cancellationToken);
    }

    private async Task AddFeeReportExportAuditAsync(FeeReportRequest request, FeeReportDto report, string format, string fileName, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.fees_exported",
            "Отчет по сборам",
            "exported",
            today,
            today,
            report.RowCount,
            request.Variation,
            new Dictionary<string, object?>
            {
                ["reportType"] = "fees",
                ["format"] = format,
                ["fileName"] = fileName,
                ["visibleSummaryRowCount"] = report.SummaryRows.Count,
                ["visibleGarageRowCount"] = report.GarageRows.Count,
                ["visibleDebtorRowCount"] = report.DebtorRows.Count,
                ["accruedTotal"] = report.AccruedTotal,
                ["collectedTotal"] = report.CollectedTotal,
                ["debtTotal"] = report.DebtTotal,
                ["variation"] = report.Variation,
                ["limit"] = request.Limit
            },
            cancellationToken);
    }

    private static string NormalizeReportActionName(string reportTitle)
    {
        return reportTitle switch
        {
            "Сводный отчет" => "consolidated",
            "Отчет по поступлениям" => "income",
            "Отчет по выплатам" => "expense",
            "Отчет по оплатам из кассы" => "cash_payments",
            "Отчет по сдаче кассы в банк" => "bank_deposits",
            "Отчет по сборам" => "fees",
            "Отчет по изменению фондов" => "fund_changes",
            _ => "custom"
        };
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

    private static string BuildSnapshotExportFileName(string reportType, string extension)
    {
        return $"garagebalance-{reportType}.{extension}";
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

    private static IEnumerable<T> ApplyEnumerableReportRowLimit<T>(IEnumerable<T> rows, int? limit)
    {
        return limit is > 0
            ? rows.Take(NormalizeReportLimit(limit.Value))
            : rows;
    }

    private static FundOperationReportRow CreateFundOperationReportRow(FundOperation operation)
    {
        return new FundOperationReportRow(
            operation.Id,
            operation.FundId,
            operation.Fund.Name,
            operation.OperationKind,
            operation.Amount,
            operation.BalanceBefore,
            operation.BalanceAfter,
            operation.ActorUserId,
            operation.CreatedAtUtc,
            operation.Reason);
    }

    private static string BuildCashPaymentPurpose(FinancialOperation operation)
    {
        var parts = new[] { operation.ExpenseType?.Name, operation.Supplier?.Name }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var purpose = string.Join(": ", parts);
        return string.IsNullOrWhiteSpace(purpose)
            ? operation.Comment ?? "Оплата из кассы"
            : purpose;
    }

    private static string BuildFeeGoal(string name)
    {
        if (name.Contains("цел", StringComparison.OrdinalIgnoreCase))
        {
            return "Целевой сбор";
        }

        if (name.Contains("член", StringComparison.OrdinalIgnoreCase))
        {
            return "Членский взнос";
        }

        if (name.Contains("вступ", StringComparison.OrdinalIgnoreCase))
        {
            return "Вступительный взнос";
        }

        return name.Contains("сбор", StringComparison.OrdinalIgnoreCase)
            ? "Сбор"
            : "Поступление";
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

    private static string FormatFundOperationKind(string operationKind)
    {
        return operationKind switch
        {
            FundOperationKinds.Deposit => "Пополнение",
            FundOperationKinds.Withdraw => "Изъятие",
            _ => operationKind
        };
    }

    private readonly record struct AmountCountByMonth(DateOnly Month, decimal Amount, int Count);
    private readonly record struct CountByMonth(DateOnly Month, int Count);
    private readonly record struct AmountByGarage(Guid GarageId, decimal Amount);
    private readonly record struct CountByGarage(Guid GarageId, int Count);
    private readonly record struct IncomeDebtAccrualRow(Guid GarageId, DateOnly AccountingMonth, decimal Amount);
    private readonly record struct IncomeDebtPaymentRow(Guid OperationId, Guid GarageId, DateOnly OperationDate, DateTimeOffset CreatedAtUtc, decimal Amount);
    private sealed record FundOperationReportRow(
        Guid Id,
        Guid FundId,
        string FundName,
        string OperationKind,
        decimal Amount,
        decimal BalanceBefore,
        decimal BalanceAfter,
        Guid? ActorUserId,
        DateTimeOffset CreatedAtUtc,
        string Reason);
    private sealed record GarageReportRowsPage(int RowCount, IReadOnlyList<GarageReportRowDto> Rows);
}
