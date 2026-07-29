using System.Diagnostics;
using System.Text.Json;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class NginxTimingAnalyzerTests
{
    [Fact]
    public async Task Analyzer_RanksNormalizedRoutesWithoutReturningSensitiveLogFields()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "infrastructure", "scripts", "analyze-nginx-timing.ps1");
        var temporaryDirectory = Directory.CreateTempSubdirectory("garagebalance-timing-");
        var logPath = Path.Combine(temporaryDirectory.FullName, "timing.log");

        try
        {
            await File.WriteAllLinesAsync(logPath,
            [
                "2026-07-30T01:00:00+03:00 request_id=secret-one remote=192.0.2.1 method=GET uri=/api/dictionaries/suppliers/4b83f43f-2056-4b8d-a92f-56e91d120f73 status=200 request_time=0.100 upstream_response_time=0.090",
                "2026-07-30T01:00:01+03:00 request_id=secret-two remote=192.0.2.2 method=GET uri=/api/dictionaries/suppliers/57f4c512-e735-4497-9511-49d884fa8ef7 status=200 request_time=0.200 upstream_response_time=0.190",
                "2026-07-30T01:00:02+03:00 request_id=secret-three remote=192.0.2.3 method=GET uri=/api/dictionaries/suppliers/57f4c512-e735-4497-9511-49d884fa8ef7?search=private status=500 request_time=0.400 upstream_response_time=0.390",
                "2026-07-30T01:00:03+03:00 request_id=secret-four remote=192.0.2.4 method=GET uri=/api/dictionaries/suppliers/4b83f43f-2056-4b8d-a92f-56e91d120f73 status=200 request_time=0.300 upstream_response_time=0.290",
                "2026-07-30T01:00:04+03:00 request_id=secret-five remote=192.0.2.5 method=PUT uri=/api/finance/meter-readings/ffffc768-1c80-dbee-6810-190d4a98ab41 status=400 request_time=0.050 upstream_response_time=0.040",
                "malformed connection without timing fields"
            ]);

            var result = await RunAnalyzerAsync(scriptPath, logPath);

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal(5, document.RootElement.GetProperty("parsedRows").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("ignoredRows").GetInt32());
            var routes = document.RootElement.GetProperty("routes");
            Assert.Equal(2, routes.GetArrayLength());
            var supplier = routes.EnumerateArray().Single(route =>
                route.GetProperty("section").GetString() == "dictionaries");
            Assert.Equal("/api/dictionaries/suppliers/:id", supplier.GetProperty("route").GetString());
            Assert.Equal(4, supplier.GetProperty("count").GetInt32());
            Assert.Equal(200, supplier.GetProperty("p50Milliseconds").GetDouble());
            Assert.Equal(400, supplier.GetProperty("p95Milliseconds").GetDouble());
            Assert.Equal(400, supplier.GetProperty("maxMilliseconds").GetDouble());
            Assert.Equal(25, supplier.GetProperty("errorRatePercent").GetDouble());
            Assert.Equal(2, document.RootElement.GetProperty("sectionCount").GetInt32());
            var finance = document.RootElement.GetProperty("sections").EnumerateArray().Single(section =>
                section.GetProperty("section").GetString() == "finance");
            Assert.Equal(1, finance.GetProperty("clientErrorCount").GetInt32());
            Assert.Equal(0, finance.GetProperty("serverErrorCount").GetInt32());
            Assert.Equal(100, finance.GetProperty("errorRatePercent").GetDouble());
            var financeRoute = routes.EnumerateArray().Single(route =>
                route.GetProperty("section").GetString() == "finance");
            Assert.Equal("/api/finance/meter-readings/:id", financeRoute.GetProperty("route").GetString());
            Assert.DoesNotContain("private", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("192.0.2", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-", result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            temporaryDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Analyzer_ReadsTimingLogFromStandardInputWithoutCreatingACopy()
    {
        var scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "infrastructure",
            "scripts",
            "analyze-nginx-timing.ps1");
        const string input = "2026-07-30T01:00:00+03:00 request_id=safe-id remote=192.0.2.1 method=GET uri=/health status=200 request_time=0.004";

        var result = await RunAnalyzerAsync(scriptPath, "STDIN", input);

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(1, document.RootElement.GetProperty("parsedRows").GetInt32());
        Assert.Equal("health", document.RootElement.GetProperty("routes")[0].GetProperty("section").GetString());
    }

    private static async Task<ProcessResult> RunAnalyzerAsync(
        string scriptPath,
        string inputPath,
        string? standardInput = null)
    {
        var executable = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardInput = true,
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
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-InputPath");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("-AsJson");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("PowerShell was not started.");
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
        }

        process.StandardInput.Close();
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
}
