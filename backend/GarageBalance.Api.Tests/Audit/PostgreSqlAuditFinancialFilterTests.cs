using System.IO.Compression;
using System.Text;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Audit;

public sealed class PostgreSqlAuditFinancialFilterTests
{
    [PostgreSqlFact]
    public async Task CashBankHistoryPreservesSectionIntersectionPaginationAndExports()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var start = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        var types = new[] { "cash_bank_balance_operation", "cash_bank_balance_operation", "cash_bank_balance_operation", "cash_bank_balance_settings", "cash_bank_balance_operation", "application_setting", "financial_operation" };
        context.AuditEvents.AddRange(types.Select((type, index) => new AuditEvent
        {
            CreatedAtUtc = start.AddSeconds(index),
            Section = index == 4 ? null : index == 6 ? "finance" : "settings",
            Action = $"filter.case_{index}",
            EntityType = type,
            Summary = $"Synthetic history {index}"
        }));
        await context.SaveChangesAsync();
        var service = new AuditService(new EfAuditEventRepository(context));
        foreach (var scenario in new (string? Section, string? QuickFilter, int[] Expected)[]
        {
            (null, "financial", [6, 4, 3, 2, 1, 0]),
            ("settings", null, [5, 3, 2, 1, 0]),
            ("settings", "financial", [3, 2, 1, 0]),
            ("finance", "financial", [6]),
            ("users", "financial", [])
        })
        {
            var request = new AuditEventListRequest(start, start.AddDays(1), null, "Synthetic history", Section: scenario.Section, QuickFilter: scenario.QuickFilter);
            var expected = scenario.Expected.Select(index => $"filter.case_{index}").ToArray();
            Assert.Equal(expected, (await service.GetEventsAsync(request, CancellationToken.None)).Select(item => item.Action));
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
            var sheet = await reader.ReadToEndAsync();
            for (var index = 0; index < types.Length; index++)
            {
                var action = $"filter.case_{index}";
                Assert.Equal(expected.Contains(action), csv.Contains(action, StringComparison.Ordinal));
                Assert.Equal(expected.Contains(action), sheet.Contains(action, StringComparison.Ordinal));
            }
        }
    }
}
