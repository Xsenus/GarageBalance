using System.Globalization;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Application.Reports;

public sealed class ServiceReportService(IServiceReportRepository repository, IBusinessDateProvider businessDate,
    IAuditEventWriter audit, IApplicationUnitOfWork unitOfWork) : IServiceReportService
{
    public Task<ServiceReportColumnsDto> GetColumnsAsync(CancellationToken cancellationToken) => repository.GetColumnsAsync(cancellationToken);

    public async Task<ReportResult<ServiceReportColumnsDto>> SaveColumnsAsync(UpdateServiceReportColumnsRequest request, Guid? actorId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = await repository.GetColumnsAsync(cancellationToken);
        if (request.Columns is null || request.Columns.Length is < 1 or > 20
            || request.Columns.Any(column => column is null || column.Id == Guid.Empty || column.Id == Guid.Parse("970bf090-583f-46f3-a04f-67e96665b829") || string.IsNullOrWhiteSpace(column.Name)
                || column.Name.Trim().Length > 80 || column.Name.Trim().Equals("Прочее", StringComparison.OrdinalIgnoreCase) || column.ServiceIds is null)
            || request.Columns.Select(column => column.Id).Distinct().Count() != request.Columns.Length
            || request.Columns.Select(column => column.Name.Trim().ToUpperInvariant()).Distinct().Count() != request.Columns.Length)
            return ReportResult<ServiceReportColumnsDto>.Failure("columns_invalid", "Задайте от 1 до 20 колонок с уникальными названиями. «Прочее» заполняется автоматически.");
        var ids = request.Columns.SelectMany(column => column.ServiceIds).ToArray();
        if (ids.Length > 100 || ids.Distinct().Count() != ids.Length || ids.Any(id => !existing.Services.Any(service => service.Id == id && service.IncomeTypeId.HasValue)))
            return ReportResult<ServiceReportColumnsDto>.Failure("services_invalid", "Выберите не более 100 существующих услуг. Услугу можно назначить только одной колонке.");
        var links = request.Columns.SelectMany(column => column.ServiceIds.Select(id => new { column.Id, Income = existing.Services.Single(service => service.Id == id).IncomeTypeId })).Where(link => link.Income.HasValue).ToArray();
        if (links.GroupBy(link => link.Income).Any(group => group.Select(link => link.Id).Distinct().Count() > 1))
            return ReportResult<ServiceReportColumnsDto>.Failure("services_ambiguous", "Услуги одного вида поступления должны находиться в одной колонке: исторические оплаты не различают их.");
        await repository.SaveColumnsAsync(request with { Columns = request.Columns.Select(column => column with { Name = column.Name.Trim() }).ToArray() }, actorId, cancellationToken);
        audit.Add(new(actorId, "settings.service_report_columns_updated", "application_setting", "service_report_columns",
            "Изменён состав колонок отчётов оплаты и задолженности.", EntityDisplayName: "Колонки отчётов",
            OldValues: new Dictionary<string, object?> { ["columns"] = JsonSerializer.Serialize(existing.Columns) }, NewValues: new Dictionary<string, object?> { ["columns"] = JsonSerializer.Serialize(request.Columns) },
            FieldLabels: new Dictionary<string, string> { ["columns"] = "Колонки отчётов" }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ReportResult<ServiceReportColumnsDto>.Success(await repository.GetColumnsAsync(cancellationToken));
    }

    public async Task<ReportResult<ServiceReportDto>> GetAsync(ServiceReportRequest request, bool debt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request = Normalize(request);
        var error = Validate(request);
        if (error is not null) return ReportResult<ServiceReportDto>.Failure("period_invalid", error);
        return ReportResult<ServiceReportDto>.Success(debt ? await repository.GetDebtAsync(request, cancellationToken) : await repository.GetPaymentsAsync(request, cancellationToken));
    }

    public async Task<ReportResult<ReportExportFileDto>> ExportAsync(ServiceReportRequest request, bool debt, bool pdf, Guid? actorId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request = Normalize(request) with { Offset = 0, Limit = QueryLimits.MaximumReportExportRows };
        var error = Validate(request);
        if (error is not null) return ReportResult<ReportExportFileDto>.Failure("period_invalid", error);
        var data = debt ? await repository.GetDebtAsync(request, cancellationToken) : await repository.GetPaymentsAsync(request, cancellationToken);
        if (data.RowCount > QueryLimits.MaximumReportExportRows) return ReportResult<ReportExportFileDto>.Failure("export_too_large", "В отчёте больше 5000 строк. Сузьте период или выберите гараж.");
        var title = debt ? request.OverdueOnly ? "Просроченная задолженность" : "Начисленная задолженность" : "Оплата по услугам";
        var headers = (debt ? new[] { "Гараж" } : new[] { "Дата", "Гараж" }).Concat(data.Columns.Select(column => column.Name)).Append("ИТОГО").ToArray();
        var rows = new List<IReadOnlyList<XlsxCell>>();
        var dayTotals = data.Days.ToDictionary(day => day.Date);
        for (var index = 0; index < data.Rows.Count; index++)
        {
            var row = data.Rows[index];
            var cells = new List<XlsxCell>();
            if (!debt) cells.Add(XlsxCell.Text(row.Date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? ""));
            cells.Add(XlsxCell.Text(row.GarageNumber));
            cells.AddRange(row.Amounts.Select(XlsxCell.Number));
            cells.Add(XlsxCell.Number(row.Total));
            rows.Add(cells);
            if (!debt && row.Date != (index + 1 < data.Rows.Count ? data.Rows[index + 1].Date : null))
            {
                var day = dayTotals[row.Date!.Value];
                rows.Add(new[] { XlsxCell.Text(day.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)), XlsxCell.Text("Итого за день") }
                    .Concat(day.Amounts.Select(XlsxCell.Number)).Append(XlsxCell.Number(day.Total)).ToArray());
            }
        }
        rows.Add((debt ? new[] { XlsxCell.Text("ИТОГО") } : new[] { XlsxCell.Text(""), XlsxCell.Text("ИТОГО") }).Concat(data.Totals.Select(XlsxCell.Number)).Append(XlsxCell.Number(data.Total)).ToArray());
        var period = debt ? $"На {data.DateTo:dd.MM.yyyy}" : $"{data.DateFrom?.ToString("dd.MM.yyyy") ?? "С начала учёта"} — {data.DateTo:dd.MM.yyyy}";
        var content = pdf ? TabularReportPdfDocumentBuilder.Build(title, period, [], BuildPdfSections(headers, rows, debt), cancellationToken)
            : XlsxWorkbookBuilder.Build([new XlsxSheet(title, headers, rows)], cancellationToken);
        audit.Add(new(actorId, "reports.service_report_exported", "report", debt ? "service_debt" : "service_payments", $"Выгружен отчёт «{title}».", EntityDisplayName: title));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var format = pdf ? "pdf" : "xlsx";
        return ReportResult<ReportExportFileDto>.Success(new($"garagebalance-{(debt ? "debt" : "services")}-{data.DateTo:yyyyMMdd}.{format}", pdf ? "application/pdf" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", content));
    }

    private static IReadOnlyList<TabularPdfSection> BuildPdfSections(string[] headers, List<IReadOnlyList<XlsxCell>> rows, bool debt)
    {
        var prefix = debt ? 1 : 2;
        var sections = new List<TabularPdfSection>();
        // Split wide custom reports horizontally instead of shrinking twenty columns beyond readability.
        for (var start = prefix; start < headers.Length - 1; start += 7)
        {
            var indices = Enumerable.Range(0, prefix).Concat(Enumerable.Range(start, Math.Min(7, headers.Length - 1 - start))).Append(headers.Length - 1).ToArray();
            var caption = headers.Length - prefix - 1 > 7 ? $"Колонки {start - prefix + 1}–{Math.Min(start - prefix + 7, headers.Length - prefix - 1)}. ИТОГО — по всем услугам." : null;
            sections.Add(new(caption, indices.Select(index => new TabularPdfColumn(headers[index], AlignRight: index >= prefix, BodyFontSize: 7.5f)).ToArray(),
                rows.Select(row => (IReadOnlyList<string>)indices.Select(index => row[index].Kind == XlsxCellKind.Decimal ? decimal.Parse(row[index].Value, CultureInfo.InvariantCulture).ToString("N2", CultureInfo.InvariantCulture) : row[index].Value).ToArray()).ToArray()));
        }
        return sections;
    }

    private ServiceReportRequest Normalize(ServiceReportRequest request) => request with
    {
        DateTo = request.DateTo ?? new DateOnly(businessDate.Today.Year, businessDate.Today.Month, DateTime.DaysInMonth(businessDate.Today.Year, businessDate.Today.Month)),
        Offset = Math.Max(request.Offset, 0),
        Limit = QueryLimits.NormalizePageSize(request.Limit)
    };
    private static string? Validate(ServiceReportRequest request) => request.GarageId == Guid.Empty ? "Выберите гараж или снимите фильтр." : request.DateFrom > request.DateTo ? "Дата начала не может быть позже даты окончания." : null;
}
