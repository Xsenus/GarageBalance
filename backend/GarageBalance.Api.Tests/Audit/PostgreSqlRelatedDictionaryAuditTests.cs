using System.IO.Compression;
using System.Text;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Audit;

public sealed class PostgreSqlRelatedDictionaryAuditTests
{
    [PostgreSqlFact]
    public async Task RelatedDictionaryFiltersPreserveHistoricalRowsAndCombinedExports()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var start = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        var types = new[] { "garage", "supplier", "staff_member", "owner", "service", "financial_operation", "financial_operation", "garage" };
        var events = types.Select((type, index) => new AuditEvent
        {
            CreatedAtUtc = start.AddSeconds(index),
            Section = index is 5 or 6 ? "finance" : "dictionary",
            Action = $"related.case_{index}",
            EntityType = type,
            EntityId = index == 7 ? null : "object-902",
            EntityDisplayName = index == 7 ? null : @"Контроль 902%_\",
            RelatedGarageId = index == 5 ? "object-902" : null,
            RelatedCounterpartyId = index == 6 ? "object-902" : null,
            Summary = "Synthetic related dictionary"
        }).ToArray();
        context.AuditEvents.AddRange(events);
        await context.SaveChangesAsync();
        foreach (var scenario in new (string? Garage, string? Counterparty, string? EntityType, string? Section, int[] Expected)[]
        {
            (" OBJECT-902 ", null, null, null, [5, 0]),
            ("902", null, "garage", "dictionary", [0]),
            (@"КОНТРОЛЬ 902%_\", null, null, null, [0]),
            (null, "OBJECT-902", null, null, [6, 3, 2, 1]),
            (null, "object-902", "supplier", "dictionary", [1]),
            (null, "object-902", "staff_member", "dictionary", [2]),
            (null, @"КОНТРОЛЬ 902%_\", null, null, [3, 2, 1]),
            ("missing", null, null, null, []),
            ("object-902", "object-902", null, null, []),
            (null, "902%X", null, null, [])
        })
        {
            var service = new AuditService(new EfAuditEventRepository(context));
            var request = new AuditEventListRequest(start, start.AddDays(1), null, "Synthetic related dictionary",
                EntityType: scenario.EntityType, Section: scenario.Section,
                RelatedGarage: scenario.Garage, RelatedCounterparty: scenario.Counterparty);
            var expected = scenario.Expected.Select(index => events[index].Id).ToArray();
            Assert.Equal(expected, (await service.GetEventsAsync(request, CancellationToken.None)).Select(item => item.Id));
            for (var offset = 0; offset <= expected.Length; offset++)
            {
                var page = await service.GetEventsPageAsync(request with { Offset = offset, Limit = 1 }, CancellationToken.None);
                Assert.Equal(expected.Length, page.TotalCount);
                Assert.Equal(expected.Skip(offset).Take(1), page.Items.Select(item => item.Id));
            }
            var csv = Encoding.UTF8.GetString((await service.ExportEventsCsvAsync(request, CancellationToken.None)).Content);
            using var archive = new ZipArchive(new MemoryStream((await service.ExportEventsXlsxAsync(request, CancellationToken.None)).Content));
            using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            var sheet = await reader.ReadToEndAsync();
            for (var index = 0; index < events.Length; index++)
            {
                Assert.Equal(scenario.Expected.Contains(index), csv.Contains(events[index].Action, StringComparison.Ordinal));
                Assert.Equal(scenario.Expected.Contains(index), sheet.Contains(events[index].Action, StringComparison.Ordinal));
            }
        }
        var stored = await context.AuditEvents.AsNoTracking().Where(item => item.Summary == "Synthetic related dictionary").ToArrayAsync();
        Assert.All(stored.Where(item => item.Section == "dictionary"), item =>
        {
            Assert.Null(item.RelatedGarageId);
            Assert.Null(item.RelatedCounterpartyId);
        });
    }
}
