using System.Text.Json;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class FrontendFirstOpenBaselineTests
{
    private static readonly string[] ExpectedSections =
    [
        "users",
        "tariffsAndFees",
        "contractors",
        "dictionaries",
        "meterReadings",
        "payments",
        "funds",
        "reports",
        "import",
        "audit",
        "releases",
        "settings"
    ];

    [Fact]
    public void Baseline_CoversEveryWorkspaceSectionWithConsistentCounts()
    {
        using var document = ReadBaseline();
        var root = document.RootElement;
        var sections = root.GetProperty("sections").EnumerateArray().ToArray();

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(ExpectedSections, sections.Select(section => section.GetProperty("name").GetString()));
        Assert.All(sections, section =>
        {
            var apiOperations = section.GetProperty("apiOperations").EnumerateArray().ToArray();
            var staticAssets = section.GetProperty("staticAssets").EnumerateArray().ToArray();
            var apiCount = section.GetProperty("apiRequestCount").GetInt32();
            var staticCount = section.GetProperty("staticRequestCount").GetInt32();

            Assert.Equal(apiCount, apiOperations.Length);
            Assert.Equal(staticCount, staticAssets.Length);
            Assert.Equal(apiCount + staticCount, section.GetProperty("requestCount").GetInt32());
            Assert.True(section.GetProperty("apiBytes").GetInt64() >= 0);
            Assert.True(section.GetProperty("staticBytes").GetInt64() > 0);
            Assert.True(section.GetProperty("maxDurationMilliseconds").GetDouble() >= 0);
            Assert.Equal(0, section.GetProperty("errorCount").GetInt32());
        });
    }

    [Fact]
    public void Baseline_ContainsRealisticSeedAndExactMainShellSizes()
    {
        using var document = ReadBaseline();
        var capture = document.RootElement.GetProperty("capture");
        var shell = document.RootElement.GetProperty("mainShell");
        var assets = shell.GetProperty("assets").EnumerateArray().ToArray();

        Assert.Equal(500, capture.GetProperty("garageCount").GetInt32());
        Assert.Equal(60, capture.GetProperty("monthCount").GetInt32());
        Assert.Equal(30000, capture.GetProperty("accrualCount").GetInt32());
        Assert.Equal(30000, capture.GetProperty("paymentCount").GetInt32());
        Assert.Equal(30000, capture.GetProperty("meterReadingCount").GetInt32());
        Assert.Equal("no-store", capture.GetProperty("cacheMode").GetString());
        Assert.False(capture.GetProperty("queryStringsRecorded").GetBoolean());
        Assert.False(capture.GetProperty("responseBodiesRecorded").GetBoolean());

        Assert.Equal(3, shell.GetProperty("requestCount").GetInt32());
        Assert.Equal(assets.Sum(asset => asset.GetProperty("rawBytes").GetInt64()),
            shell.GetProperty("rawBytes").GetInt64());
        Assert.Equal(assets.Sum(asset => asset.GetProperty("gzipBytes").GetInt64()),
            shell.GetProperty("gzipBytes").GetInt64());
    }

    [Fact]
    public void Baseline_DoesNotStoreQueriesIdentifiersOrSensitivePayloads()
    {
        var json = File.ReadAllText(GetBaselinePath());
        using var document = JsonDocument.Parse(json);

        Assert.DoesNotContain('?', json);
        Assert.DoesNotContain("Bearer ", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"password\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"token\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"email\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@example", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("responseBody", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(
            document.RootElement.GetProperty("sections").EnumerateArray()
                .SelectMany(section => section.GetProperty("apiOperations").EnumerateArray()),
            operation => Assert.Matches(
                "^(GET|PUT|POST|DELETE|PATCH) /api/[a-z0-9/-]+$",
                operation.GetString()!));
    }

    [Fact]
    public void FinalBaseline_CoversEverySectionAndPublicWebVitals()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetFinalBaselinePath()));
        var root = document.RootElement;
        var capture = root.GetProperty("capture");
        var shell = root.GetProperty("publicShell");
        var rendering = root.GetProperty("reactRendering");
        var sections = root.GetProperty("sections").EnumerateArray().ToArray();

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(500, capture.GetProperty("garageCount").GetInt32());
        Assert.Equal(60, capture.GetProperty("monthCount").GetInt32());
        Assert.Equal(5, capture.GetProperty("iterationsPerApiOperation").GetInt32());
        Assert.False(capture.GetProperty("queryStringsRecorded").GetBoolean());
        Assert.False(capture.GetProperty("responseBodiesRecorded").GetBoolean());
        Assert.Equal(ExpectedSections, sections.Select(section => section.GetProperty("name").GetString()));

        Assert.Equal("h2", shell.GetProperty("protocol").GetString());
        Assert.True(shell.GetProperty("largestContentfulPaintMilliseconds").GetDouble() < 2500);
        Assert.True(shell.GetProperty("interactionToNextPaintMilliseconds").GetDouble() < 200);
        Assert.True(shell.GetProperty("cumulativeLayoutShift").GetDouble() < 0.1);
        Assert.Equal(0, shell.GetProperty("consoleErrorCount").GetInt32());
        Assert.True(shell.GetProperty("gzip").GetBoolean());
        Assert.True(shell.GetProperty("http2").GetBoolean());

        Assert.Equal(1, rendering.GetProperty("deliberateNavigationWorkspaceRenderCommits").GetInt32());
        Assert.Equal(1, rendering.GetProperty("rapidTwelveSectionBurstWorkspaceRenderCommits").GetInt32());
        Assert.Equal(0, rendering.GetProperty("sidebarToggleWorkspaceRenderCommits").GetInt32());

        Assert.All(sections, section =>
        {
            var apiCount = section.GetProperty("apiRequestCount").GetInt32();
            var staticCount = section.GetProperty("staticRequestCount").GetInt32();
            Assert.Equal(apiCount + staticCount, section.GetProperty("requestCount").GetInt32());
            Assert.True(section.GetProperty("apiEncodedBodyBytes").GetInt64() >= 0);
            Assert.True(section.GetProperty("staticEncodedBodyBytes").GetInt64() > 0);
            Assert.True(section.GetProperty("p50Milliseconds").GetDouble() <=
                        section.GetProperty("p95Milliseconds").GetDouble());
            Assert.True(section.GetProperty("p95Milliseconds").GetDouble() <=
                        section.GetProperty("maxMilliseconds").GetDouble());
            Assert.Equal(0, section.GetProperty("errorRatePercent").GetDouble());
            Assert.Equal(1, section.GetProperty("renderCommits").GetInt32());
        });
    }

    [Fact]
    public void FinalBaseline_DoesNotStoreQueriesCredentialsOrResponseBodies()
    {
        var json = File.ReadAllText(GetFinalBaselinePath());
        using var document = JsonDocument.Parse(json);

        Assert.DoesNotContain('?', json);
        Assert.DoesNotContain("Bearer ", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"password\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"token\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"email\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@example", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("responseBody", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ExpectedSections.Length,
            document.RootElement.GetProperty("sections").GetArrayLength());
    }

    private static JsonDocument ReadBaseline() => JsonDocument.Parse(File.ReadAllText(GetBaselinePath()));

    private static string GetBaselinePath() =>
        Path.Combine(FindRepositoryRoot(), "infrastructure", "performance", "frontend-first-open-baseline.json");

    private static string GetFinalBaselinePath() =>
        Path.Combine(FindRepositoryRoot(), "infrastructure", "performance", "frontend-final-baseline.json");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
