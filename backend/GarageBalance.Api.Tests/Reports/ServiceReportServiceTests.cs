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

public sealed class ServiceReportServiceTests
{
    [Fact]
    public async Task WidePdfSplitsColumnsWithoutDroppingAmountsOrDailyTotals()
    {
        var repository = new FakeRepository { ColumnsCount = 20 };
        var result = await Create(repository).ExportAsync(new(), false, true, null, default);
        using var pdf = PdfDocument.Open(result.Value!.Content);
        var text = string.Concat(pdf.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)));
        var compact = string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
        foreach (var index in Enumerable.Range(1, 20)) Assert.Contains($"Услуга{index}", compact, StringComparison.Ordinal);
        Assert.Contains("Колонки1–7", compact, StringComparison.Ordinal); Assert.Contains("Колонки15–20", compact, StringComparison.Ordinal);
        Assert.Contains("Итогозадень", compact, StringComparison.Ordinal); Assert.Contains("600.00", compact, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalizesDefaultsAndValidatesRangeGarageAndCancellation(bool debt)
    {
        var repository = new FakeRepository(); var service = Create(repository);
        var result = await service.GetAsync(new(Offset: -1, Limit: int.MaxValue), debt, default);
        Assert.True(result.Succeeded); Assert.Equal(new DateOnly(2046, 9, 30), repository.Last!.DateTo);
        Assert.Null(repository.Last.DateFrom); Assert.Equal(0, repository.Last.Offset); Assert.Equal(QueryLimits.MaximumPageSize, repository.Last.Limit);
        Assert.Equal(debt, repository.Debt);
        Assert.False((await service.GetAsync(new(GarageId: Guid.Empty), debt, default)).Succeeded);
        Assert.False((await service.GetAsync(new(new(2046, 10, 1), new(2046, 9, 30)), debt, default)).Succeeded);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetAsync(new(), debt, cancellation.Token));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("name")]
    [InlineData("other")]
    [InlineData("duplicateName")]
    [InlineData("duplicateId")]
    [InlineData("duplicateService")]
    [InlineData("missingService")]
    [InlineData("ambiguous")]
    public async Task RejectsInvalidConfigurationBeforeSaving(string failure)
    {
        var repository = new FakeRepository(); var service = Create(repository);
        var columns = new[] { new ServiceReportColumn(Guid.NewGuid(), "Свет", [repository.Service1]), new ServiceReportColumn(Guid.NewGuid(), "Вода", []) };
        switch (failure)
        {
            case "empty": columns = []; break;
            case "name": columns[0] = columns[0] with { Name = " " }; break;
            case "other": columns[0] = columns[0] with { Name = "Прочее" }; break;
            case "duplicateName": columns[1] = columns[1] with { Name = "Свет" }; break;
            case "duplicateId": columns[1] = columns[1] with { Id = columns[0].Id }; break;
            case "duplicateService": columns[1] = columns[1] with { ServiceIds = [repository.Service1] }; break;
            case "missingService": columns[1] = columns[1] with { ServiceIds = [Guid.NewGuid()] }; break;
            case "ambiguous": columns[1] = columns[1] with { ServiceIds = [repository.Service2] }; break;
        }
        var result = await service.SaveColumnsAsync(new(Guid.NewGuid(), columns), null, default);
        Assert.False(result.Succeeded); Assert.Null(repository.Saved);
    }

    [Fact]
    public async Task SavesTrimmedNamesWithActorAuditAndPropagatesPersistenceFailures()
    {
        var repository = new FakeRepository(); var audit = new FakeAudit(); var work = new FakeWork(); var service = Create(repository, audit, work);
        var actor = Guid.NewGuid(); var version = Guid.NewGuid();
        var result = await service.SaveColumnsAsync(new(version, [new(Guid.NewGuid(), " Свет ", [repository.Service1, repository.Service2])]), actor, default);
        Assert.True(result.Succeeded); Assert.Equal("Свет", repository.Saved!.Columns[0].Name); Assert.Equal(version, repository.Saved.Version);
        Assert.Equal(actor, repository.Actor); Assert.Equal(actor, Assert.Single(audit.Events).ActorUserId); Assert.Equal(1, work.Saves);
        work.Fail = true; await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveColumnsAsync(repository.Saved, actor, default));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExportsWholeFilterWithNumericGarageNoMergesAndAudit(bool debt, bool pdf)
    {
        var repository = new FakeRepository(); var audit = new FakeAudit(); var work = new FakeWork(); var service = Create(repository, audit, work);
        var result = await service.ExportAsync(new(Offset: 90, Limit: 10), debt, pdf, null, default);
        Assert.True(result.Succeeded); Assert.Equal(5000, repository.Last!.Limit); Assert.Equal(0, repository.Last.Offset);
        Assert.Equal(1, work.Saves); Assert.Single(audit.Events);
        Assert.NotEmpty(result.Value!.Content);
        if (!pdf)
        {
            using var zip = new ZipArchive(new MemoryStream(result.Value.Content));
            using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open(); var sheet = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Assert.Empty(sheet.Descendants(ns + "mergeCell"));
            Assert.Contains(sheet.Descendants(ns + "c"), cell => cell.Element(ns + "v")?.Value == "85" && cell.Attribute("t")?.Value != "inlineStr");
        }
        repository.RowCount = 5001;
        Assert.False((await service.ExportAsync(new(), debt, pdf, null, default)).Succeeded);
        Assert.False((await service.ExportAsync(new(GarageId: Guid.Empty), debt, pdf, null, default)).Succeeded);
    }

    [Theory]
    [InlineData("payments")]
    [InlineData("accrued")]
    [InlineData("overdue")]
    public async Task SaveTargetsOnlySelectedReportAndValidatesScope(string report)
    {
        var repository = new FakeRepository(); var audit = new FakeAudit(); var service = Create(repository, audit);
        var request = new UpdateServiceReportColumnsRequest(Guid.NewGuid(), [new(Guid.NewGuid(), "Колонка", [])], report);
        var result = await service.SaveColumnsAsync(request, null, default);
        Assert.True(result.Succeeded);
        Assert.Equal(report, result.Value!.Report);
        Assert.Equal(report, repository.Saved!.Report);
        Assert.EndsWith($".{report}", Assert.Single(audit.Events).EntityId);
        Assert.Equal("report_invalid", (await service.SaveColumnsAsync(request with { Report = "other" }, null, default)).ErrorCode);
        Assert.Single(audit.Events);
        Assert.True(ServiceReportScopes.IsValid(report));
        Assert.NotEqual(Guid.Empty, ServiceReportScopes.SettingsId(report));
        Assert.Throws<ArgumentException>(() => ServiceReportScopes.SettingsId("other"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveColumnsAsync(request, null, cancellation.Token));
    }

    private static ServiceReportService Create(FakeRepository repository, FakeAudit? audit = null, FakeWork? work = null) => new(repository, new TestBusinessDateProvider(new(2046, 9, 18)), audit ?? new(), work ?? new());
    private sealed class FakeRepository : IServiceReportRepository
    {
        public Guid Service1 { get; } = Guid.NewGuid(); public Guid Service2 { get; } = Guid.NewGuid(); private Guid Income { get; } = Guid.NewGuid();
        public ServiceReportRequest? Last { get; private set; }
        public bool Debt { get; private set; }
        public int RowCount { get; set; } = 1;
        public int ColumnsCount { get; set; } = 1;
        public UpdateServiceReportColumnsRequest? Saved { get; private set; }
        public Guid? Actor { get; private set; }
        public Task<ServiceReportColumnsDto> GetColumnsAsync(CancellationToken cancellationToken, string report = "payments") => Task.FromResult(new ServiceReportColumnsDto(Guid.NewGuid(), [], [new(Service1, "Свет", Income, false), new(Service2, "Свет старый", Income, true)], report));
        public Task SaveColumnsAsync(UpdateServiceReportColumnsRequest request, Guid? actorId, CancellationToken cancellationToken) { Saved = request; Actor = actorId; return Task.CompletedTask; }
        public Task<ServiceReportDto> GetPaymentsAsync(ServiceReportRequest request, CancellationToken cancellationToken) { Debt = false; return Get(request); }
        public Task<ServiceReportDto> GetDebtAsync(ServiceReportRequest request, CancellationToken cancellationToken) { Debt = true; return Get(request); }
        private Task<ServiceReportDto> Get(ServiceReportRequest request) { Last = request; var amounts = Enumerable.Repeat(30m, ColumnsCount).ToArray(); return Task.FromResult(new ServiceReportDto(request.DateFrom, request.DateTo!.Value, Enumerable.Range(1, ColumnsCount).Select(index => new ServiceReportColumn(Guid.NewGuid(), $"Услуга {index}", [])).ToArray(), [new(request.DateTo, Guid.NewGuid(), "85", amounts)], [new(request.DateTo.Value, amounts)], amounts, RowCount, request.Offset, request.Limit)); }
    }
    private sealed class FakeAudit : IAuditEventWriter { public List<AuditEventWriteRequest> Events { get; } = []; public AuditEvent? Add(AuditEventWriteRequest request) { Events.Add(request); return null; } }
    private sealed class FakeWork : IApplicationUnitOfWork { public int Saves { get; private set; } public bool Fail { get; set; } public Task SaveChangesAsync(CancellationToken cancellationToken) { Saves++; if (Fail) throw new InvalidOperationException("Persistence failed"); return Task.CompletedTask; } }
}
