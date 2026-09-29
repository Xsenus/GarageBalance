using System.Globalization;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Application.Reports;

public sealed record DailyServicePaymentReportRequest(DateOnly? ThroughDate, Guid? GarageId = null, int? Offset = null, int? Limit = null, Guid? ActorUserId = null);
public sealed record DailyServicePaymentReportDto(DateOnly DateFrom, DateOnly ThroughDate, DailyServicePaymentReportData Data);

public interface IDailyServicePaymentReportService
{
    Task<ReportResult<DailyServicePaymentReportDto>> GetAsync(DailyServicePaymentReportRequest request, CancellationToken cancellationToken);
    Task<ReportResult<ReportExportFileDto>> ExportAsync(DailyServicePaymentReportRequest request, bool pdf, CancellationToken cancellationToken);
}

public sealed class DailyServicePaymentReportService(IDailyServicePaymentReportQuery query, IBusinessDateProvider businessDateProvider,
    IAuditEventWriter auditEventWriter, IApplicationUnitOfWork unitOfWork) : IDailyServicePaymentReportService
{
    public async Task<ReportResult<DailyServicePaymentReportDto>> GetAsync(DailyServicePaymentReportRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.GarageId == Guid.Empty)
        {
            return ReportResult<DailyServicePaymentReportDto>.Failure("garage_required", "Выберите гараж или снимите фильтр гаража.");
        }
        var date = request.ThroughDate ?? businessDateProvider.Today;
        var data = await query.GetRowsAsync(date, request.GarageId, Math.Max(request.Offset ?? 0, 0),
            QueryLimits.NormalizePageSize(request.Limit ?? QueryLimits.DefaultPageSize), cancellationToken);
        return ReportResult<DailyServicePaymentReportDto>.Success(new(new DateOnly(date.Year, date.Month, 1), date, data));
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportAsync(DailyServicePaymentReportRequest request, bool pdf, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.GarageId == Guid.Empty)
        {
            return ReportResult<ReportExportFileDto>.Failure("garage_required", "Выберите гараж или снимите фильтр гаража.");
        }
        var date = request.ThroughDate ?? businessDateProvider.Today;
        var data = await query.GetRowsAsync(date, request.GarageId, 0, QueryLimits.MaximumReportExportRows, cancellationToken, forExport: true);
        if (data.RowCount > QueryLimits.MaximumReportExportRows)
        {
            return ReportResult<ReportExportFileDto>.Failure("export_too_large", "В отчёте больше 5000 строк. Выберите гараж для выгрузки.");
        }
        var headers = new List<string> { "Дата", "Гараж", "Электроэнергия", "Вода", "Мусор", "Наружное освещение", "Членский взнос", "Целевой взнос" };
        if (data.HasOther) headers.Add("Прочее");
        headers.Add("ИТОГО");
        var rows = new List<IReadOnlyList<XlsxCell>>();
        foreach (var day in data.Days)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var row in data.Rows.Where(row => row.Date == day.Date))
            {
                rows.Add(BuildRow(row.Date, row.GarageNumber, row.Amounts, data.HasOther));
            }
            rows.Add(BuildRow(day.Date, "Итого за день", day.Amounts, data.HasOther));
        }
        rows.Add(BuildRow(null, "Итого с начала месяца", data.MonthTotal, data.HasOther));
        var period = $"{new DateOnly(date.Year, date.Month, 1):dd.MM.yyyy} — {date:dd.MM.yyyy}";
        var content = pdf
            ? TabularReportPdfDocumentBuilder.Build("Ежедневная оплата услуг", period, [],
                [new TabularPdfSection(null, headers.Select((header, index) => new TabularPdfColumn(header, AlignRight: index >= 2,
                    BodyFontSize: index >= 2 ? 7.5f : null)).ToArray(),
                    rows.Select(row => (IReadOnlyList<string>)row.Select(cell => cell.Kind == XlsxCellKind.Decimal
                        ? decimal.Parse(cell.Value, CultureInfo.InvariantCulture).ToString("N2", CultureInfo.InvariantCulture) : cell.Value).ToArray()).ToArray())], cancellationToken)
            : XlsxWorkbookBuilder.Build([new XlsxSheet("Оплата услуг", headers, rows)], cancellationToken);
        var format = pdf ? "pdf" : "xlsx";
        var file = new ReportExportFileDto($"garagebalance-daily-services-{date:yyyyMMdd}.{format}",
            pdf ? "application/pdf" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", content);
        auditEventWriter.Add(new AuditEventWriteRequest(request.ActorUserId, "reports.daily_service_payments_exported", "report", "daily_service_payments",
            $"Выгружен отчёт ежедневной оплаты услуг за {period}.", EntityDisplayName: "Ежедневная оплата услуг",
            Metadata: new Dictionary<string, object?> { ["format"] = format, ["rowCount"] = data.RowCount, ["hasGarageFilter"] = request.GarageId.HasValue }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ReportResult<ReportExportFileDto>.Success(file);
    }

    private static IReadOnlyList<XlsxCell> BuildRow(DateOnly? date, string garage, DailyServicePaymentAmounts amounts, bool hasOther)
    {
        var cells = new List<XlsxCell> { XlsxCell.Text(date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? string.Empty), XlsxCell.Text(garage),
            XlsxCell.Number(amounts.Electricity), XlsxCell.Number(amounts.Water), XlsxCell.Number(amounts.Trash), XlsxCell.Number(amounts.OutdoorLighting),
            XlsxCell.Number(amounts.Membership), XlsxCell.Number(amounts.Target) };
        if (hasOther) cells.Add(XlsxCell.Number(amounts.Other));
        cells.Add(XlsxCell.Number(amounts.Total));
        return cells;
    }
}
