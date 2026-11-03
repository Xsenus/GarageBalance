using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Audit;

namespace GarageBalance.Api.Tests.Audit;

public sealed class CleanupDigestAuditTests
{
    [Fact]
    public void CleanupPreservesOnlyExactTechnicalDigestsAndStillMasksPersonalData()
    {
        var digest = "a1234567890b" + new string('c', 52);
        Assert.Equal(64, digest.Length);
        Assert.NotEqual(digest, AuditTextMasker.Mask(digest));
        var writer = new AuditEventWriter(new Store());
        var metadata = new Dictionary<string, object?> { ["planHash"] = digest, ["backupSha256"] = digest, ["note"] = "1234-567890" };
        var cleanup = writer.Add(new(null, "finance.selective_history_cleared", "maintenance", "synthetic", "Synthetic", Metadata: metadata))!;
        using var json = JsonDocument.Parse(cleanup.MetadataJson!);
        Assert.Equal(digest, json.RootElement.GetProperty("planHash").GetString());
        Assert.Equal(digest, json.RootElement.GetProperty("backupSha256").GetString());
        Assert.DoesNotContain("1234-567890", json.RootElement.GetProperty("note").GetString());
        var unrelated = writer.Add(new(null, "synthetic.other", "synthetic", "synthetic", "Synthetic", Metadata: metadata))!;
        using var other = JsonDocument.Parse(unrelated.MetadataJson!);
        Assert.NotEqual(digest, other.RootElement.GetProperty("planHash").GetString());
    }

    private sealed class Store : IAuditEventStore { public void Add(AuditEvent auditEvent) { } }
}
