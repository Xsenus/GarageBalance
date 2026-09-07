using System.IO.Compression;
using System.Text;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Audit;

public sealed class PostgreSqlAuditDateBoundaryTests
{
    [PostgreSqlFact]
    public async Task LocalDayOffsetsPreserveBoundaryEventsInPagesListsAndExports()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var start = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.FromHours(7));
        var end = start.AddDays(1).AddTicks(-10);
        var timestamps = new[] { start.AddTicks(-10), start, start.AddHours(12), end, start.AddDays(1) };
        context.AuditEvents.AddRange(timestamps.Select((time, index) => new AuditEvent
        {
            CreatedAtUtc = time.ToUniversalTime(),
            Action = $"audit.boundary_{index}",
            Section = "boundary",
            EntityType = "garage",
            Summary = $"Boundary event {index}"
        }));
        await context.SaveChangesAsync();
        var service = new AuditService(new EfAuditEventRepository(context));
        foreach (var request in new[]
        {
            new AuditEventListRequest(start, end, null, null, Section: "boundary"),
            new AuditEventListRequest(start, null, null, null, Section: "boundary"),
            new AuditEventListRequest(null, end, null, null, Section: "boundary"),
            new AuditEventListRequest(start.ToOffset(TimeSpan.FromHours(-4)), end.ToOffset(TimeSpan.FromHours(-4)), null, null, Section: "boundary"),
            new AuditEventListRequest(null, null, null, null, Section: "boundary")
        })
        {
            var expected = timestamps.Select((time, index) => (time, index))
                .Where(item => (!request.DateFrom.HasValue || item.time >= request.DateFrom) && (!request.DateTo.HasValue || item.time <= request.DateTo))
                .Reverse().Select(item => $"audit.boundary_{item.index}").ToArray();
            var list = await service.GetEventsAsync(request, CancellationToken.None);
            Assert.Equal(expected, list.Select(item => item.Action));
            for (var offset = 0; offset <= expected.Length; offset++)
            {
                var page = await service.GetEventsPageAsync(request with { Offset = offset, Limit = 1 }, CancellationToken.None);
                Assert.Equal(expected.Length, page.TotalCount);
                Assert.Equal(expected.Skip(offset).Take(1), page.Items.Select(item => item.Action));
            }

            var csv = Encoding.UTF8.GetString((await service.ExportEventsCsvAsync(request, CancellationToken.None)).Content);
            var xlsx = await service.ExportEventsXlsxAsync(request, CancellationToken.None);
            using var archive = new ZipArchive(new MemoryStream(xlsx.Content));
            using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            var worksheet = await reader.ReadToEndAsync();
            for (var index = 0; index < timestamps.Length; index++)
            {
                var action = $"audit.boundary_{index}";
                Assert.Equal(expected.Contains(action), csv.Contains(action, StringComparison.Ordinal));
                Assert.Equal(expected.Contains(action), worksheet.Contains(action, StringComparison.Ordinal));
            }
        }

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetEventsPageAsync(new(start, end, null, null), canceled.Token));
    }
}
