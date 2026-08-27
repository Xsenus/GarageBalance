using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GarageBalance.Api.Tests.Deployment;

[Collection(ApiBenchmarkScriptCollection.Name)]
public sealed class ApiBenchmarkScriptTests
{
    private static readonly string[] ExpectedWorkspaceSections =
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
    public void DefaultScenarios_CoverMainReadSectionsWithFixedSafeThresholds()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(
            Path.Combine(root, "infrastructure", "scripts", "benchmark-api.ps1"));
        using var document = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(root, "infrastructure", "performance", "api-smoke-scenarios.json")));
        var scenarios = document.RootElement.EnumerateArray().ToArray();

        Assert.Contains("$scenarios = @($scenarioDocument)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$scenarioDocument.GetEnumerator()", script, StringComparison.Ordinal);
        Assert.Equal(14, scenarios.Length);
        Assert.Equal(
            scenarios.Length,
            scenarios.Select(item => item.GetProperty("name").GetString()).Distinct().Count());
        Assert.Equal(
            ExpectedWorkspaceSections.OrderBy(section => section),
            scenarios
                .Where(item => item.TryGetProperty("workspaceSection", out _))
                .Select(item => item.GetProperty("workspaceSection").GetString())
                .OrderBy(section => section));
        Assert.Contains(scenarios, item => item.GetProperty("path").GetString() == "/health/ready");
        Assert.Contains(scenarios, item => item.GetProperty("path").GetString()!.StartsWith("/api/dictionaries/", StringComparison.Ordinal));
        Assert.Contains(scenarios, item => item.GetProperty("path").GetString()!.StartsWith("/api/finance/", StringComparison.Ordinal));
        Assert.Contains(scenarios, item => item.GetProperty("path").GetString()!.StartsWith("/api/reports/", StringComparison.Ordinal));
        Assert.Contains(scenarios, item => item.GetProperty("path").GetString()!.StartsWith("/api/import/", StringComparison.Ordinal));
        Assert.Contains(
            scenarios,
            item => item.GetProperty("name").GetString() == "meter-year"
                && item.GetProperty("path").GetString()!.Contains("meterKind=electricity", StringComparison.Ordinal));
        Assert.All(scenarios, item =>
        {
            Assert.True(item.GetProperty("p50Milliseconds").GetDouble() > 0);
            Assert.True(item.GetProperty("p95Milliseconds").GetDouble() >=
                item.GetProperty("p50Milliseconds").GetDouble());
            Assert.Equal(0, item.GetProperty("maxErrorRatePercent").GetDouble());
            Assert.DoesNotContain("skip=", item.GetProperty("path").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("take=", item.GetProperty("path").GetString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Benchmark_PassesFastScenarioAndDoesNotExposeBearerTokenOrResponseBody()
    {
        await using var server = await BenchmarkServer.StartAsync(
            _ => new BenchmarkResponse(HttpStatusCode.OK, """{"private":"response-body"}"""));
        var scenarioPath = await WriteScenarioAsync(p50: 500, p95: 500, maxErrorRate: 0);

        try
        {
            var result = await RunBenchmarkAsync(server.BaseUrl, scenarioPath, "secret-benchmark-token");

            Assert.True(result.ExitCode == 0, result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            Assert.True(document.RootElement.GetProperty("passed").GetBoolean());
            Assert.Equal(4, document.RootElement.GetProperty("requestCount").GetInt32());
            Assert.DoesNotContain("secret-benchmark-token", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("response-body", result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(scenarioPath);
        }
    }

    [Fact]
    public async Task Benchmark_FailsWhenMeasuredErrorRateExceedsFixedScenarioThreshold()
    {
        await using var server = await BenchmarkServer.StartAsync(
            requestNumber => new BenchmarkResponse(
                requestNumber % 2 == 0 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
                "{}"));
        var scenarioPath = await WriteScenarioAsync(p50: 500, p95: 500, maxErrorRate: 0);

        try
        {
            var result = await RunBenchmarkAsync(server.BaseUrl, scenarioPath);

            Assert.True(result.ExitCode == 1, result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            Assert.False(document.RootElement.GetProperty("passed").GetBoolean());
            Assert.Equal(1, document.RootElement.GetProperty("failedScenarioCount").GetInt32());
            Assert.True(
                document.RootElement.GetProperty("scenarios")[0]
                    .GetProperty("errorRatePercent")
                    .GetDouble() > 0);
        }
        finally
        {
            File.Delete(scenarioPath);
        }
    }

    private static async Task<string> WriteScenarioAsync(double p50, double p95, double maxErrorRate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"garagebalance-api-benchmark-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
            new[]
            {
                new
                {
                    name = "test-route",
                    path = "/benchmark",
                    authorized = false,
                    p50Milliseconds = p50,
                    p95Milliseconds = p95,
                    maxErrorRatePercent = maxErrorRate
                }
            }));
        return path;
    }

    private static async Task<ProcessResult> RunBenchmarkAsync(
        string baseUrl,
        string scenarioPath,
        string? token = null)
    {
        var root = FindRepositoryRoot();
        var executable = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }

        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(root, "infrastructure", "scripts", "benchmark-api.ps1"));
        startInfo.ArgumentList.Add("-BaseUrl");
        startInfo.ArgumentList.Add(baseUrl);
        startInfo.ArgumentList.Add("-ScenarioPath");
        startInfo.ArgumentList.Add(scenarioPath);
        startInfo.ArgumentList.Add("-Iterations");
        startInfo.ArgumentList.Add("4");
        startInfo.ArgumentList.Add("-WarmupIterations");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-AsJson");
        if (token is not null)
        {
            startInfo.Environment["GARAGEBALANCE_BENCHMARK_TOKEN"] = token;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("PowerShell was not started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record BenchmarkResponse(HttpStatusCode StatusCode, string Body);

    private sealed class BenchmarkServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task loop;
        private readonly Func<int, BenchmarkResponse> responseFactory;
        private int requestNumber;

        private BenchmarkServer(TcpListener listener, Func<int, BenchmarkResponse> responseFactory)
        {
            this.listener = listener;
            this.responseFactory = responseFactory;
            loop = AcceptLoopAsync();
        }

        public string BaseUrl { get; private init; } = string.Empty;

        public static Task<BenchmarkServer> StartAsync(Func<int, BenchmarkResponse> responseFactory)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return Task.FromResult(new BenchmarkServer(listener, responseFactory)
            {
                BaseUrl = $"http://127.0.0.1:{port}"
            });
        }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            listener.Stop();
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException) when (cancellation.IsCancellationRequested)
            {
            }

            cancellation.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                _ = HandleAsync(client);
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var buffer = new byte[4096];
                _ = await stream.ReadAsync(buffer, cancellation.Token);
                var response = responseFactory(Interlocked.Increment(ref requestNumber));
                var body = Encoding.UTF8.GetBytes(response.Body);
                var reason = response.StatusCode == HttpStatusCode.OK ? "OK" : "Internal Server Error";
                var header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {(int)response.StatusCode} {reason}\r\n" +
                    "Content-Type: application/json\r\n" +
                    $"Content-Length: {body.Length}\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(header, cancellation.Token);
                await stream.WriteAsync(body, cancellation.Token);
            }
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApiBenchmarkScriptCollection
{
    public const string Name = "API benchmark scripts";
}
