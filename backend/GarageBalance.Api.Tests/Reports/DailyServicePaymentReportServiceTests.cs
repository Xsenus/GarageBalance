using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Tests.Common;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace GarageBalance.Api.Tests.Reports;

public sealed class DailyServicePaymentReportServiceTests
{
    [Fact]
    public async Task GetUsesBusinessDateBoundsAndGarageFilter()
    {
        var date = new DateOnly(2046, 9, 18);
        var query = new FakeQuery();
        var audit = new FakeAudit();
        var work = new FakeWork();
        var service = new DailyServicePaymentReportService(query, new TestBusinessDateProvider(date), audit, work);
        var garage = Guid.NewGuid();
        var result = await service.GetAsync(new(null, garage, -10, int.MaxValue), CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2046, 9, 1), result.Value!.DateFrom);
        Assert.Equal(date, result.Value.ThroughDate);
        Assert.Equal((date, garage, 0, 500, false), query.Last);
        Assert.Empty(audit.Events);
        Assert.Equal(0, work.Saves);
        await service.GetAsync(new(date.AddDays(-1)), CancellationToken.None);
        Assert.Equal((date.AddDays(-1), (Guid?)null, 0, 25, false), query.Last);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportKeepsRowsDayAndMonthTotalsAndAuditsOnlySuccessfulExport(bool pdf)
    {
        var date = new DateOnly(2046, 9, 18);
        var amounts = new DailyServicePaymentAmounts(10.1m, 20.2m, 30.3m, 40.4m, 50.5m, 60.6m, 7m);
        var data = new DailyServicePaymentReportData([new(date, Guid.NewGuid(), "85", amounts), new(date, Guid.NewGuid(), "А-20", amounts)],
            [new(date, new(20.2m, 40.4m, 60.6m, 80.8m, 101m, 121.2m, 14m))], new(20.2m, 40.4m, 60.6m, 80.8m, 101m, 121.2m, 14m), 2, true, 0, 5000);
        var query = new FakeQuery { Data = data };
        var audit = new FakeAudit();
        var work = new FakeWork();
        var service = new DailyServicePaymentReportService(query, new TestBusinessDateProvider(date), audit, work);
        var actor = Guid.NewGuid();
        var result = await service.ExportAsync(new(date, Offset: 100, Limit: 1, ActorUserId: actor), pdf, CancellationToken.None);
        Assert.True(result.Succeeded);
        var file = result.Value!;
        Assert.Equal($"garagebalance-daily-services-20460918.{(pdf ? "pdf" : "xlsx")}", file.FileName);
        Assert.Equal((date, (Guid?)null, 0, 5000, true), query.Last);
        var entry = Assert.Single(audit.Events);
        Assert.Equal(actor, entry.ActorUserId);
        Assert.Equal("reports.daily_service_payments_exported", entry.Action);
        Assert.Equal(2, entry.Metadata!["rowCount"]);
        Assert.Equal(1, work.Saves);
        if (pdf)
        {
            Assert.Equal("application/pdf", file.ContentType);
            using var document = PdfDocument.Open(file.Content);
            var text = string.Join("\n", document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)));
            Assert.Contains("85", text);
            Assert.Contains("А-20", text);
            Assert.Contains("438.20", text);
        }
        else
        {
            using var archive = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
            using var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var sheet = XDocument.Load(stream);
            Assert.DoesNotContain(sheet.Descendants(), element => element.Name.LocalName is "mergeCells" or "mergeCell");
            var numericGarage = sheet.Descendants().Single(element => element.Name.LocalName == "c" && element.Attribute("r")?.Value == "B2");
            Assert.Null(numericGarage.Attribute("t"));
            Assert.Equal("85", numericGarage.Value);
            Assert.Contains(sheet.Descendants(), element => element.Name.LocalName == "t" && element.Value == "Прочее");
            Assert.Contains(sheet.Descendants(), element => element.Name.LocalName == "t" && element.Value == "Итого за день");
            Assert.Contains(sheet.Descendants(), element => element.Name.LocalName == "v" && element.Value == "438.2");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportHandlesEmptyReportWithoutOtherColumn(bool pdf)
    {
        var query = new FakeQuery();
        var service = new DailyServicePaymentReportService(query, new TestBusinessDateProvider(new(2046, 2, 1)), new FakeAudit(), new FakeWork());
        var result = await service.ExportAsync(new(null), pdf, CancellationToken.None);
        Assert.True(result.Succeeded);
        if (!pdf)
        {
            using var archive = new ZipArchive(new MemoryStream(result.Value!.Content));
            using var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var sheet = XDocument.Load(stream);
            Assert.DoesNotContain(sheet.Descendants(), element => element.Value == "Прочее");
            Assert.Contains(sheet.Descendants(), element => element.Name.LocalName == "t" && element.Value == "Итого с начала месяца");
        }
    }

    [Fact]
    public async Task RejectsInvalidFilterOversizedExportAndCancellationWithoutAudit()
    {
        var query = new FakeQuery();
        var audit = new FakeAudit();
        var work = new FakeWork();
        var service = new DailyServicePaymentReportService(query, new TestBusinessDateProvider(new(2046, 9, 18)), audit, work);
        Assert.False((await service.GetAsync(new(null, Guid.Empty), CancellationToken.None)).Succeeded);
        Assert.False((await service.ExportAsync(new(null, Guid.Empty), false, CancellationToken.None)).Succeeded);
        Assert.Equal(0, query.Calls);
        query.Data = query.Data with { RowCount = 5001 };
        var tooLarge = await service.ExportAsync(new(null), false, CancellationToken.None);
        Assert.Equal("export_too_large", tooLarge.ErrorCode);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(new(null), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(new(null), true, cancellation.Token));
        Assert.Equal(1, query.Calls);
        Assert.Empty(audit.Events);
        Assert.Equal(0, work.Saves);
    }

    [Fact]
    public async Task QueryFailureAndAuditSaveFailureDoNotReturnSuccessfulExport()
    {
        var query = new FakeQuery { Fail = true };
        var audit = new FakeAudit();
        var service = new DailyServicePaymentReportService(query, new TestBusinessDateProvider(new(2046, 9, 18)), audit, new FakeWork { Fail = true });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync(new(null), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(new(null), false, CancellationToken.None));
        Assert.Empty(audit.Events);
        query.Fail = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(new(null), false, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultiPagePdfPreservesEveryGarageHeadersAndTotalsWithinPageBounds(bool hasOther)
    {
        var date = new DateOnly(2046, 9, 18);
        const int count = 64;
        const decimal amount = 999999999.99m;
        var amounts = new DailyServicePaymentAmounts(amount, amount, amount, amount, amount, amount, hasOther ? amount : 0);
        var total = new DailyServicePaymentAmounts(amount * count, amount * count, amount * count, amount * count,
            amount * count, amount * count, hasOther ? amount * count : 0);
        var rows = Enumerable.Range(1, count).Select(index => new DailyServicePaymentRow(date, Guid.NewGuid(), $"ГАРАЖ-{index:000}", amounts)).ToArray();
        var query = new FakeQuery { Data = new(rows, [new(date, total)], total, count, hasOther, 0, 5000) };
        var service = new DailyServicePaymentReportService(query, new TestBusinessDateProvider(date), new FakeAudit(), new FakeWork());
        var result = await service.ExportAsync(new(date), true, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        using var document = PdfDocument.Open(result.Value!.Content);
        var pages = document.GetPages().ToArray();
        Assert.True(pages.Length > 1);
        var text = string.Concat(pages.Select(page => ContentOrderTextExtractor.GetText(page)));
        var compactText = string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
        foreach (var row in rows)
            Assert.Contains(row.GarageNumber, compactText, StringComparison.Ordinal);
        Assert.Contains("Итогозадень", compactText, StringComparison.Ordinal);
        Assert.Contains("Итогосначаламесяца", compactText, StringComparison.Ordinal);
        Assert.Contains(total.Total.ToString("N2", CultureInfo.InvariantCulture), compactText, StringComparison.Ordinal);
        // A value split across lines still passes the compact-text assertion.
        // Require both large totals to remain complete words inside their cells.
        var totalWords = pages.SelectMany(page => page.GetWords()).Select(word => word.Text).ToArray();
        Assert.Equal(2, totalWords.Count(word => word == total.Total.ToString("N2", CultureInfo.InvariantCulture)));
        Assert.Equal((hasOther ? 7 : 6) * 2,
            totalWords.Count(word => word == total.Electricity.ToString("N2", CultureInfo.InvariantCulture)));
        Assert.Equal(hasOther, compactText.Contains("Прочее", StringComparison.Ordinal));
        Assert.All(pages, page =>
        {
            var pageText = string.Concat(ContentOrderTextExtractor.GetText(page).Where(character => !char.IsWhiteSpace(character)));
            Assert.Contains("Электроэнергия", pageText, StringComparison.Ordinal);
            Assert.All(page.Letters, letter =>
            {
                Assert.InRange(letter.BoundingBox.Left, -0.5, page.Width + 0.5);
                Assert.InRange(letter.BoundingBox.Right, -0.5, page.Width + 0.5);
                Assert.InRange(letter.BoundingBox.Bottom, -0.5, page.Height + 0.5);
                Assert.InRange(letter.BoundingBox.Top, -0.5, page.Height + 0.5);
            });
        });
    }

    private sealed class FakeQuery : IDailyServicePaymentReportQuery
    {
        public DailyServicePaymentReportData Data { get; set; } = new([], [], new(0, 0, 0, 0, 0, 0, 0), 0, false, 0, 25);
        public (DateOnly, Guid?, int, int, bool) Last { get; private set; }
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public Task<DailyServicePaymentReportData> GetRowsAsync(DateOnly throughDate, Guid? garageId, int offset, int limit, CancellationToken cancellationToken, bool forExport = false)
        {
            Calls++;
            Last = (throughDate, garageId, offset, limit, forExport);
            if (Fail) throw new InvalidOperationException("Synthetic query failure");
            return Task.FromResult(Data);
        }
    }
    private sealed class FakeAudit : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public AuditEvent? Add(AuditEventWriteRequest request) { Events.Add(request); return null; }
    }
    private sealed class FakeWork : IApplicationUnitOfWork
    {
        public int Saves { get; private set; }
        public bool Fail { get; set; }
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            if (Fail) throw new InvalidOperationException("Synthetic audit save failure");
            return Task.CompletedTask;
        }
    }
}
