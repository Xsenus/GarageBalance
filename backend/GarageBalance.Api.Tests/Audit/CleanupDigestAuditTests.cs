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

    [Theory]
    [InlineData("planHash")]
    [InlineData("settingBeforeHash")]
    [InlineData("epochBeforeHash")]
    [InlineData("settingAfterHash")]
    [InlineData("epochAfterHash")]
    [InlineData("forecastInputFingerprint")]
    public void AnnualRelease_PreservesOnlyValidExactTechnicalDigests(string key)
    {
        var digest = "a1234567890b" + new string('c', 52);
        var writer = new AuditEventWriter(new Store());
        var metadata = new Dictionary<string, object?> { [key] = digest, ["phone"] = "1234-567890", ["note"] = digest };
        var released = writer.Add(new(null, "finance.annual_accrual_generation_resumed", "application_setting", "synthetic", "Synthetic", Metadata: metadata))!;
        using var json = JsonDocument.Parse(released.MetadataJson!);
        Assert.Equal(digest, json.RootElement.GetProperty(key).GetString());
        Assert.NotEqual(digest, json.RootElement.GetProperty("note").GetString());
        Assert.Equal("[секрет скрыт]", json.RootElement.GetProperty("phone").GetString());
        foreach (var action in new[] { "synthetic.other", "finance.annual_accrual_generation_resumed.other", "finance.annual_accrual_generation_resumed " })
        {
            var other = writer.Add(new(null, action, "application_setting", "synthetic", "Synthetic", Metadata: metadata))!;
            using var otherJson = JsonDocument.Parse(other.MetadataJson!);
            Assert.NotEqual(digest, otherJson.RootElement.GetProperty(key).GetString());
        }
        foreach (var invalid in new[] { digest + "c", digest[..63], "g" + digest[1..], " " + digest })
        {
            var invalidEvent = writer.Add(new(null, "finance.annual_accrual_generation_resumed", "application_setting", "synthetic", "Synthetic",
                Metadata: new Dictionary<string, object?> { [key] = invalid }))!;
            using var invalidJson = JsonDocument.Parse(invalidEvent.MetadataJson!);
            Assert.NotEqual(invalid.Trim(), invalidJson.RootElement.GetProperty(key).GetString());
        }
    }

    [Theory]
    [InlineData("backupSha256")]
    [InlineData("settingBeforeHash ")]
    [InlineData("SettingBeforeHash")]
    [InlineData("passwordHash")]
    public void AnnualRelease_UnknownDigestFieldsRemainMasked(string key)
    {
        var digest = "a1234567890b" + new string('c', 52);
        var audit = new AuditEventWriter(new Store()).Add(new(null, "finance.annual_accrual_generation_resumed", "application_setting", "synthetic", "Synthetic",
            Metadata: new Dictionary<string, object?> { [key] = digest }))!;
        using var json = JsonDocument.Parse(audit.MetadataJson!);
        Assert.NotEqual(digest, json.RootElement.GetProperty(key.Trim()).GetString());
    }

    private sealed class Store : IAuditEventStore { public void Add(AuditEvent auditEvent) { } }
}
