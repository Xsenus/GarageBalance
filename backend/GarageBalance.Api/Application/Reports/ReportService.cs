using System.Globalization;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Reports;

public sealed class ReportService(
    ICashMovementReportQuery cashMovementReportQuery,
    IFundChangeReportQuery fundChangeReportQuery,
    IConsolidatedMonthlyReportQuery consolidatedMonthlyReportQuery,
    IConsolidatedGarageReportQuery consolidatedGarageReportQuery,
    IGarageReportQuery garageReportQuery,
    IFeeReportQuery feeReportQuery,
    IExpenseReportQuery expenseReportQuery,
    IIncomeReportQuery incomeReportQuery,
    IApplicationUnitOfWork unitOfWork,
    IAuditEventWriter auditEventWriter) : IReportService
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

        if (!TryNormalizeReportSort<ConsolidatedReportDto>(ReportSortKind.Consolidated, request.SortBy, request.SortDirection, out var sort, out var sortFailure))
        {
            return sortFailure!;
        }

        var monthlyOffset = Math.Max(request.Offset ?? 0, 0);
        int? monthlyLimit = request.Limit is > 0 ? NormalizeReportLimit(request.Limit.Value) : null;
        var monthlyData = await consolidatedMonthlyReportQuery.GetMonthlyDataAsync(
            periodFrom,
            periodTo,
            sort,
            monthlyOffset,
            monthlyLimit,
            cancellationToken);
        var incomeByMonth = monthlyData.IncomeByMonth.ToDictionary(row => row.Month);
        var expenseByMonth = monthlyData.ExpenseByMonth.ToDictionary(row => row.Month);
        var accrualByMonth = monthlyData.AccrualByMonth.ToDictionary(row => row.Month);
        var readingsByMonth = monthlyData.MeterReadingsByMonth.ToDictionary(row => row.Month);

        var months = MonthPeriod.Enumerate(periodFrom, periodTo).ToList();
        var allMonthlyRows = months
            .Select(month =>
            {
                incomeByMonth.TryGetValue(month, out var income);
                expenseByMonth.TryGetValue(month, out var expense);
                accrualByMonth.TryGetValue(month, out var accrual);
                readingsByMonth.TryGetValue(month, out var readings);
                var startingBalance = month == periodFrom ? monthlyData.GarageStartingBalanceTotal : 0m;
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
        var monthlyRows = monthlyData.MonthlyRows
            .Select(row => new MonthlyReportRowDto(
                row.AccountingMonth,
                row.IncomeTotal,
                row.ExpenseTotal,
                row.AccrualTotal,
                row.Balance,
                row.Debt,
                row.OperationCount,
                row.AccrualCount,
                row.MeterReadingCount))
            .ToList();

        int? garageRowLimit = request.Limit is > 0 ? NormalizeReportLimit(request.Limit.Value) : null;
        var garageData = await consolidatedGarageReportQuery.GetGarageRowsAsync(
            request.Search,
            periodFrom,
            periodTo,
            garageRowLimit,
            cancellationToken);
        var garageRows = garageData.Rows.Select(row => new GarageReportRowDto(
                row.GarageId,
                row.GarageNumber,
                FormatOwnerName(row.OwnerLastName, row.OwnerFirstName, row.OwnerMiddleName),
                row.IncomeTotal,
                row.AccrualTotal,
                row.AccrualTotal - row.IncomeTotal,
                row.MeterReadingCount))
            .ToList();
        var report = new ConsolidatedReportDto(
            periodFrom,
            periodTo,
            allMonthlyRows.Sum(row => row.IncomeTotal),
            allMonthlyRows.Sum(row => row.ExpenseTotal),
            allMonthlyRows.Sum(row => row.AccrualTotal),
            allMonthlyRows.Sum(row => row.Balance),
            allMonthlyRows.Sum(row => row.Debt),
            allMonthlyRows.Sum(row => row.OperationCount),
            allMonthlyRows.Sum(row => row.AccrualCount),
            allMonthlyRows.Sum(row => row.MeterReadingCount),
            monthlyRows,
            garageData.RowCount,
            garageRows,
            monthlyData.IncomeBreakdown
                .Select(row => new NamedAmountTotalDto(row.TypeId, row.Name, row.Amount))
                .ToList(),
            monthlyData.ExpenseBreakdown
                .Select(row => new NamedAmountTotalDto(row.TypeId, row.Name, row.Amount))
                .ToList());

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
                ["offset"] = request.Offset,
                ["sortBy"] = sort.Field,
                ["sortDirection"] = sort.Descending ? "desc" : "asc",
                ["monthlyRowCount"] = report.MonthlyRows.Count,
                ["visibleGarageRows"] = report.GarageRows.Count
            },
            cancellationToken);

        return ReportResult<ConsolidatedReportDto>.Success(report);
    }

    public Task<ReportResult<GarageDetailReportDto>> GetGarageReportAsync(GarageReportRequest request, CancellationToken cancellationToken) =>
        BuildGarageReportAsync(request, NormalizeReportLimit(request.Limit ?? 25), cancellationToken);

    private async Task<ReportResult<GarageDetailReportDto>> BuildGarageReportAsync(GarageReportRequest request, int? queryLimit, CancellationToken cancellationToken)
    {
        var periodFrom = MonthPeriod.Normalize(request.MonthFrom ?? MonthPeriod.CurrentLocalMonth());
        var periodTo = MonthPeriod.Normalize(request.MonthTo ?? periodFrom);
        if (periodTo < periodFrom)
        {
            return ReportResult<GarageDetailReportDto>.Failure("period_invalid", "Дата окончания отчета не может быть раньше даты начала.");
        }

        if (!TryNormalizeReportSort<GarageDetailReportDto>(ReportSortKind.Garages, request.SortBy, request.SortDirection, out var sort, out var sortFailure))
        {
            return sortFailure!;
        }

        var offset = queryLimit.HasValue ? Math.Max(request.Offset ?? 0, 0) : 0;
        var data = await garageReportQuery.GetRowsAsync(
            periodFrom,
            periodTo,
            request.Search,
            (request.GarageIds ?? []).ToHashSet(),
            (request.OwnerIds ?? []).ToHashSet(),
            (request.IncomeTypeIds ?? []).ToHashSet(),
            request.GroupAccruals,
            offset,
            queryLimit,
            sort,
            cancellationToken);
        var rows = data.Rows.Select(row => new GarageDetailReportRowDto(
                row.AccountingMonth,
                row.GarageId,
                row.GarageNumber,
                FormatOwnerName(row.OwnerLastName, row.OwnerFirstName, row.OwnerMiddleName),
                row.IncomeTypeId,
                row.IncomeTypeName,
                row.AccrualAmount,
                row.IncomeAmount,
                row.AccrualAmount - row.IncomeAmount))
            .ToList();
        var report = new GarageDetailReportDto(
            periodFrom,
            periodTo,
            data.AccrualTotal,
            data.IncomeTotal,
            data.AccrualTotal - data.IncomeTotal,
            data.RowCount,
            rows,
            offset,
            queryLimit ?? data.RowCount);

        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.garages_generated",
            "Отчет по гаражам",
            "generated",
            report.PeriodFrom,
            report.PeriodTo,
            report.RowCount,
            request.Search,
            new Dictionary<string, object?>
            {
                ["groupAccruals"] = request.GroupAccruals,
                ["garageFilterCount"] = request.GarageIds?.Count ?? 0,
                ["ownerFilterCount"] = request.OwnerIds?.Count ?? 0,
                ["incomeTypeFilterCount"] = request.IncomeTypeIds?.Count ?? 0,
                ["limit"] = queryLimit,
                ["offset"] = offset,
                ["sortBy"] = sort.Field,
                ["sortDirection"] = sort.Descending ? "desc" : "asc",
                ["visibleRowCount"] = report.Rows.Count
            },
            cancellationToken);

        return ReportResult<GarageDetailReportDto>.Success(report);
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportGarageReportXlsxAsync(GarageReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await BuildGarageReportAsync(request, null, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var headers = request.GroupAccruals
            ? new[] { "Месяц", "Гараж", "Владелец", "Начислено", "Поступило", "Разница" }
            : new[] { "Месяц", "Гараж", "Владелец", "Вид поступления", "Начислено", "Поступило", "Разница" };
        var rows = report.Rows.Select(row => request.GroupAccruals
            ? (IReadOnlyList<XlsxCell>)
            [
                XlsxCell.Text(row.AccountingMonth.ToString("yyyy-MM")),
                XlsxCell.Text(row.GarageNumber),
                XlsxCell.Text(row.OwnerName),
                XlsxCell.Number(row.AccrualAmount),
                XlsxCell.Number(row.IncomeAmount),
                XlsxCell.Number(row.Difference)
            ]
            :
            [
                XlsxCell.Text(row.AccountingMonth.ToString("yyyy-MM")),
                XlsxCell.Text(row.GarageNumber),
                XlsxCell.Text(row.OwnerName),
                XlsxCell.Text(row.IncomeTypeName),
                XlsxCell.Number(row.AccrualAmount),
                XlsxCell.Number(row.IncomeAmount),
                XlsxCell.Number(row.Difference)
            ]).ToArray();
        var content = XlsxWorkbookBuilder.Build(
            [
                new XlsxSheet("Гаражи", headers, rows),
                new XlsxSheet(
                    "Итоги",
                    ["Период с", "Период по", "Начислено", "Поступило", "Разница", "Строк"],
                    [[
                        XlsxCell.Text(report.PeriodFrom.ToString("yyyy-MM-dd")),
                        XlsxCell.Text(report.PeriodTo.ToString("yyyy-MM-dd")),
                        XlsxCell.Number(report.AccrualTotal),
                        XlsxCell.Number(report.IncomeTotal),
                        XlsxCell.Number(report.Difference),
                        XlsxCell.Number(report.RowCount)
                    ]])
            ]);
        var file = new ReportExportFileDto(
            BuildExportFileName("garages", report.PeriodFrom, report.PeriodTo, "xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Отчет по гаражам", "xlsx", file.FileName, report.PeriodFrom, report.PeriodTo, report.RowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportGarageReportPdfAsync(GarageReportRequest request, CancellationToken cancellationToken)
    {
        var reportResult = await BuildGarageReportAsync(request, null, cancellationToken);
        if (!reportResult.Succeeded)
        {
            return ReportResult<ReportExportFileDto>.Failure(reportResult.ErrorCode!, reportResult.ErrorMessage!);
        }

        var report = reportResult.Value!;
        var lines = new List<string>
        {
            $"Period: {report.PeriodFrom:yyyy-MM-dd} - {report.PeriodTo:yyyy-MM-dd}",
            $"Accrued: {FormatAmount(report.AccrualTotal)} | Income: {FormatAmount(report.IncomeTotal)} | Difference: {FormatAmount(report.Difference)} | Rows: {report.RowCount}",
            string.Empty,
            request.GroupAccruals
                ? "Month | Garage | Owner | Accrued | Income | Difference"
                : "Month | Garage | Owner | Income type | Accrued | Income | Difference"
        };
        lines.AddRange(report.Rows.Select(row => request.GroupAccruals
            ? string.Join(" | ", row.AccountingMonth.ToString("yyyy-MM"), row.GarageNumber, row.OwnerName ?? string.Empty, FormatAmount(row.AccrualAmount), FormatAmount(row.IncomeAmount), FormatAmount(row.Difference))
            : string.Join(" | ", row.AccountingMonth.ToString("yyyy-MM"), row.GarageNumber, row.OwnerName ?? string.Empty, row.IncomeTypeName, FormatAmount(row.AccrualAmount), FormatAmount(row.IncomeAmount), FormatAmount(row.Difference))));
        var content = PdfReportDocumentBuilder.Build("GarageBalance garage report", lines);
        var file = new ReportExportFileDto(
            BuildExportFileName("garages", report.PeriodFrom, report.PeriodTo, "pdf"),
            "application/pdf",
            content);
        await AddReportExportAuditAsync(request.ActorUserId, "Отчет по гаражам", "pdf", file.FileName, report.PeriodFrom, report.PeriodTo, report.RowCount, request.Search, cancellationToken);

        return ReportResult<ReportExportFileDto>.Success(file);
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

        if (!TryNormalizeReportSort<IncomeReportDto>(ReportSortKind.Income, request.SortBy, request.SortDirection, out var sort, out var sortFailure))
        {
            return sortFailure!;
        }

        var rowMode = string.IsNullOrWhiteSpace(request.RowMode)
            ? IncomeReportAllRows
            : request.RowMode.Trim().ToLowerInvariant();
        if (rowMode is not (IncomeReportAllRows or IncomeReportAccrualRows or IncomeReportPaymentRows))
        {
            return ReportResult<IncomeReportDto>.Failure("row_mode_invalid", "Режим строк отчета по поступлениям неизвестен.");
        }

        int? limit = request.Limit is > 0 ? NormalizeReportLimit(request.Limit.Value) : null;
        var offset = Math.Max(request.Offset ?? 0, 0);
        var data = await incomeReportQuery.GetRowsAsync(
            dateFrom,
            dateTo,
            rowMode,
            request.GarageIds.ToHashSet(),
            request.OwnerIds.ToHashSet(),
            request.IncomeTypeIds.ToHashSet(),
            request.Search,
            limit,
            offset,
            sort,
            cancellationToken);
        var report = new IncomeReportDto(
            dateFrom,
            dateTo,
            data.AccrualTotal,
            data.IncomeTotal,
            data.AccrualTotal - data.IncomeTotal,
            data.RowCount,
            data.Rows,
            offset,
            limit ?? data.Rows.Count);

        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.income_generated",
            "Отчет по поступлениям",
            "generated",
            report.DateFrom,
            report.DateTo,
            report.RowCount,
            request.Search,
            BuildIncomeReportMetadata(request, rowMode, report.Rows.Count, sort),
            cancellationToken);

        return ReportResult<IncomeReportDto>.Success(report);
    }

    public async Task<ReportResult<ExpenseReportDto>> GetExpenseReportAsync(ExpenseReportRequest request, CancellationToken cancellationToken)
    {
        var (dateFrom, dateTo) = NormalizeDateRange(request.DateFrom, request.DateTo);
        if (dateTo < dateFrom)
        {
            return ReportResult<ExpenseReportDto>.Failure("period_invalid", "Дата окончания отчета не может быть раньше даты начала.");
        }

        if (!TryNormalizeReportSort<ExpenseReportDto>(ReportSortKind.Expense, request.SortBy, request.SortDirection, out var sort, out var sortFailure))
        {
            return sortFailure!;
        }

        var rowMode = string.IsNullOrWhiteSpace(request.RowMode)
            ? ExpenseReportAllRows
            : request.RowMode.Trim().ToLowerInvariant();
        if (rowMode is not (ExpenseReportAllRows or ExpenseReportAccrualRows or ExpenseReportPaymentRows))
        {
            return ReportResult<ExpenseReportDto>.Failure("row_mode_invalid", "Режим строк отчета по выплатам неизвестен.");
        }

        int? limit = request.Limit is > 0 ? NormalizeReportLimit(request.Limit.Value) : null;
        var offset = Math.Max(request.Offset ?? 0, 0);
        var data = await expenseReportQuery.GetRowsAsync(
            dateFrom,
            dateTo,
            rowMode,
            request.SupplierIds.ToHashSet(),
            (request.StaffMemberIds ?? []).ToHashSet(),
            request.ExpenseTypeIds.ToHashSet(),
            request.Search,
            limit,
            offset,
            sort,
            cancellationToken);
        var report = new ExpenseReportDto(
            dateFrom,
            dateTo,
            data.AccrualTotal,
            data.ExpenseTotal,
            data.AccrualTotal - data.ExpenseTotal,
            data.RowCount,
            data.Rows,
            offset,
            limit ?? data.Rows.Count);

        await AddReportAuditAsync(
            request.ActorUserId,
            "reports.expense_generated",
            "Отчет по выплатам",
            "generated",
            report.DateFrom,
            report.DateTo,
            report.RowCount,
            request.Search,
            BuildExpenseReportMetadata(request, rowMode, report.Rows.Count, sort),
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

        if (!TryNormalizeReportSort<FundChangeReportDto>(ReportSortKind.FundChanges, request.SortBy, request.SortDirection, out var sort, out var sortFailure))
        {
            return sortFailure!;
        }

        var offset = Math.Max(request.Offset ?? 0, 0);
        int? limit = request.Limit is > 0 ? NormalizeReportLimit(request.Limit.Value) : null;
        var data = await fundChangeReportQuery.GetFundChangesAsync(dateFrom, dateTo, request.Search, offset, limit, sort, cancellationToken);
        var rows = data.Rows
            .Select(row => new FundChangeReportRowDto(
                row.Id,
                row.FundId,
                row.FundName,
                DateOnly.FromDateTime(row.CreatedAtUtc.UtcDateTime),
                row.OperationKind,
                FormatFundOperationKind(row.OperationKind),
                row.Amount,
                row.BalanceBefore,
                row.BalanceAfter,
                row.ActorUserId,
                row.ActorDisplayName ?? row.ActorUserId?.ToString(),
                row.Reason))
            .ToList();
        var report = new FundChangeReportDto(dateFrom, dateTo, data.DepositTotal, data.WithdrawalTotal, data.RowCount, offset, limit ?? data.RowCount, rows);

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
                ["limit"] = request.Limit,
                ["offset"] = request.Offset
                ,
                ["sortBy"] = sort.Field
                ,
                ["sortDirection"] = sort.Descending ? "desc" : "asc"
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

        if (!TryNormalizeReportSort<CashPaymentReportDto>(ReportSortKind.CashPayments, request.SortBy, request.SortDirection, out var sort, out var sortFailure))
        {
            return sortFailure!;
        }

        var offset = Math.Max(request.Offset ?? 0, 0);
        int? limit = request.Limit is > 0 ? NormalizeReportLimit(request.Limit.Value) : null;
        var data = await cashMovementReportQuery.GetCashPaymentsAsync(dateFrom, dateTo, request.Search, offset, limit, sort, cancellationToken);

        var rows = data.Operations
            .Select(operation => new CashPaymentReportRowDto(
                operation.Id,
                operation.OperationDate,
                operation.Amount,
                operation.HasReceipt,
                BuildCashPaymentPurpose(operation.ExpenseTypeName, operation.SupplierName, operation.Comment),
                operation.SupplierName,
                operation.ExpenseTypeName,
                operation.DocumentNumber,
                operation.Comment))
            .ToList();
        var report = new CashPaymentReportDto(dateFrom, dateTo, data.Total, data.RowCount, offset, limit ?? data.RowCount, rows);

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
                ["limit"] = request.Limit,
                ["offset"] = request.Offset
                ,
                ["sortBy"] = sort.Field
                ,
                ["sortDirection"] = sort.Descending ? "desc" : "asc"
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

        if (!TryNormalizeReportSort<BankDepositReportDto>(ReportSortKind.BankDeposits, request.SortBy, request.SortDirection, out var sort, out var sortFailure))
        {
            return sortFailure!;
        }

        var offset = Math.Max(request.Offset ?? 0, 0);
        int? limit = request.Limit is > 0 ? NormalizeReportLimit(request.Limit.Value) : null;
        var data = await cashMovementReportQuery.GetBankDepositsAsync(dateFrom, dateTo, request.Search, offset, limit, sort, cancellationToken);

        var rows = data.Operations
            .Select(operation => new BankDepositReportRowDto(
                operation.Id,
                DateOnly.FromDateTime(operation.CreatedAtUtc.UtcDateTime),
                operation.Amount,
                operation.FundName,
                operation.Reason))
            .ToList();
        var report = new BankDepositReportDto(dateFrom, dateTo, data.Total, data.RowCount, offset, limit ?? data.RowCount, rows);

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
                ["limit"] = request.Limit,
                ["offset"] = request.Offset
                ,
                ["sortBy"] = sort.Field
                ,
                ["sortDirection"] = sort.Descending ? "desc" : "asc"
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
        if (!TryNormalizeReportSort<FeeReportDto>(ReportSortKind.Fees, request.SortBy, request.SortDirection, out var sort, out var sortFailure))
        {
            return sortFailure!;
        }

        var variation = request.Variation?.Trim();
        var campaigns = (await feeReportQuery.GetActiveCampaignsAsync(cancellationToken)).ToList();
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

        var incomeTypes = hasFeeCampaigns
            ? new List<IncomeType>()
            : (await feeReportQuery.GetActiveIncomeTypesAsync(cancellationToken)).ToList();

        if (!hasFeeCampaigns && !string.IsNullOrWhiteSpace(variation))
        {
            var normalizedVariation = variation.ToUpperInvariant();
            incomeTypes = incomeTypes
                .Where(incomeType => incomeType.Name.ToUpperInvariant().Contains(normalizedVariation, StringComparison.Ordinal))
                .ToList();
        }

        var feeEntries = hasFeeCampaigns
            ? campaigns
                .Select(campaign => (
                    Id: campaign.Id,
                    campaign.Name,
                    Goal: string.IsNullOrWhiteSpace(campaign.Goal) ? BuildFeeGoal(campaign.Name) : campaign.Goal.Trim(),
                    TargetAmount: (decimal?)campaign.TargetAmount))
                .OrderBy(entry => entry.Name)
                .ToList()
            : incomeTypes
                .Select(incomeType => (
                    Id: incomeType.Id,
                    incomeType.Name,
                    Goal: BuildFeeGoal(incomeType.Name),
                    TargetAmount: (decimal?)null))
                .OrderBy(entry => entry.Name)
                .ToList();

        if (feeEntries.Count == 0)
        {
            var emptyReport = new FeeReportDto(variation ?? "Все сборы", 0m, 0m, 0m, 0, [], [], []);
            await AddFeeReportAuditAsync(request, emptyReport, cancellationToken);
            return ReportResult<FeeReportDto>.Success(emptyReport);
        }

        var feeEntryIds = feeEntries.Select(entry => entry.Id).ToList();
        var offset = Math.Max(request.Offset ?? 0, 0);
        int? limit = request.Limit is > 0 ? NormalizeReportLimit(request.Limit.Value) : null;
        var feeData = await feeReportQuery.GetFeeReportPageAsync(
            feeEntryIds,
            hasFeeCampaigns,
            sort,
            offset,
            limit,
            cancellationToken);

        var summaryRows = feeEntries
            .Select(entry =>
            {
                feeData.AccrualTotals.TryGetValue(entry.Id, out var accrued);
                feeData.CollectedTotals.TryGetValue(entry.Id, out var collected);
                return new FeeReportSummaryRowDto(entry.Id, entry.Name, entry.Goal, entry.TargetAmount ?? accrued, collected);
            })
            .Where(row => row.FeeAmount != 0 || row.Collected != 0 || !string.IsNullOrWhiteSpace(variation))
            .ToList();

        var feeGarageRows = feeData.GarageRows
            .Select(row => new FeeReportGarageRowDto(
                row.GarageId,
                row.GarageNumber,
                FormatOwnerName(row.OwnerLastName, row.OwnerFirstName, row.OwnerMiddleName),
                row.FeeEntryId,
                row.FeeName,
                row.Accrued,
                row.Paid,
                row.LastPaymentDate,
                row.Debt))
            .ToList();
        var debtorRows = feeData.DebtorRows
            .Select(row => new FeeReportDebtorRowDto(
                row.GarageId,
                row.GarageNumber,
                FormatOwnerName(row.OwnerLastName, row.OwnerFirstName, row.OwnerMiddleName),
                row.FeeEntryId,
                row.FeeName,
                row.Paid,
                row.LastPaymentDate,
                row.Debt))
            .ToList();

        var rowCount = summaryRows.Count + feeData.GarageRowCount;
        var visibleSummaryRows = ApplyRowLimit(summaryRows, request.Limit);
        var report = new FeeReportDto(
            variation ?? "Все сборы",
            summaryRows.Sum(row => row.FeeAmount),
            summaryRows.Sum(row => row.Collected),
            feeData.DebtTotal,
            rowCount,
            visibleSummaryRows,
            feeGarageRows,
            debtorRows);

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
                    ["Тип", "Дата", "Месяц учета", "Поставщик или сотрудник", "Услуга / статья расхода", "Начислено", "Выплачено", "Разница", "Документ", "Комментарий"],
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
            "Type | Date | Month | Supplier or employee | Expense type | Accrued | Paid | Difference | Document"
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static Dictionary<string, object?> BuildIncomeReportMetadata(IncomeReportRequest request, string rowMode, int visibleRowCount, ReportSort sort)
    {
        return new Dictionary<string, object?>
        {
            ["reportType"] = "income",
            ["rowMode"] = rowMode,
            ["visibleRowCount"] = visibleRowCount,
            ["garageFilterCount"] = request.GarageIds.Count,
            ["ownerFilterCount"] = request.OwnerIds.Count,
            ["incomeTypeFilterCount"] = request.IncomeTypeIds.Count,
            ["limit"] = request.Limit,
            ["offset"] = request.Offset,
            ["sortBy"] = sort.Field,
            ["sortDirection"] = sort.Descending ? "desc" : "asc"
        };
    }

    private static Dictionary<string, object?> BuildExpenseReportMetadata(ExpenseReportRequest request, string rowMode, int visibleRowCount, ReportSort sort)
    {
        return new Dictionary<string, object?>
        {
            ["reportType"] = "expense",
            ["rowMode"] = rowMode,
            ["visibleRowCount"] = visibleRowCount,
            ["supplierFilterCount"] = request.SupplierIds.Count,
            ["staffMemberFilterCount"] = request.StaffMemberIds?.Count ?? 0,
            ["expenseTypeFilterCount"] = request.ExpenseTypeIds.Count,
            ["limit"] = request.Limit,
            ["offset"] = request.Offset,
            ["sortBy"] = sort.Field,
            ["sortDirection"] = sort.Descending ? "desc" : "asc"
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
                ["limit"] = request.Limit,
                ["offset"] = request.Offset,
                ["sortBy"] = request.SortBy,
                ["sortDirection"] = request.SortDirection
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
                ["limit"] = request.Limit,
                ["offset"] = request.Offset,
                ["sortBy"] = request.SortBy,
                ["sortDirection"] = request.SortDirection
            },
            cancellationToken);
    }

    private static string NormalizeReportActionName(string reportTitle)
    {
        return reportTitle switch
        {
            "Сводный отчет" => "consolidated",
            "Отчет по гаражам" => "garages",
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

    private static IEnumerable<T> ApplyEnumerableReportRowLimit<T>(IEnumerable<T> rows, int? limit)
    {
        return limit is > 0
            ? rows.Take(NormalizeReportLimit(limit.Value))
            : rows;
    }

    private static IEnumerable<T> ApplyPage<T>(IEnumerable<T> rows, int offset, int? limit)
    {
        var page = offset > 0 ? rows.Skip(offset) : rows;
        return limit is > 0 ? page.Take(NormalizeReportLimit(limit.Value)) : page;
    }

    private static bool TryNormalizeReportSort<T>(
        ReportSortKind kind,
        string? sortBy,
        string? sortDirection,
        out ReportSort sort,
        out ReportResult<T>? failure)
    {
        if (ReportSorting.TryNormalize(kind, sortBy, sortDirection, out sort, out var errorCode, out var errorMessage))
        {
            failure = null;
            return true;
        }

        failure = ReportResult<T>.Failure(errorCode!, errorMessage!);
        return false;
    }

    private static string BuildCashPaymentPurpose(string? expenseTypeName, string? supplierName, string? comment)
    {
        var parts = new[] { expenseTypeName, supplierName }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var purpose = string.Join(": ", parts);
        return string.IsNullOrWhiteSpace(purpose)
            ? comment ?? "Оплата из кассы"
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

    private static string FormatAmount(decimal value) => MoneyFormatting.Format(value);

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

}
