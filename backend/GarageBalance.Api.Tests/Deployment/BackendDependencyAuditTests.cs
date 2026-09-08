using System.Diagnostics;
using System.Text;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class BackendDependencyAuditTests
{
    [Theory]
    [InlineData("clean", 0, true, "backendDependencyAudit=passed; projects=1")]
    [InlineData("direct", 0, false, "Example.Package 1.0.0: High")]
    [InlineData("transitive", 0, false, "Example.Package 1.0.0: High")]
    [InlineData("clean", 7, false, "command failed with exit code 7")]
    [InlineData("empty", 0, false, "empty or unsupported report")]
    [InlineData("missing", 0, false, "empty or unsupported report")]
    [InlineData("null-project", 0, false, "empty or unsupported report")]
    [InlineData("missing-path", 0, false, "empty or unsupported report")]
    [InlineData("version", 0, false, "empty or unsupported report")]
    [InlineData("warning", 0, false, "could not complete")]
    [InlineData("error", 0, false, "could not complete")]
    [InlineData("invalid", 0, false, "")]
    public async Task AuditGateFailsForVulnerabilitiesAndIncompleteAudits(
        string scenario, int commandExitCode, bool shouldSucceed, string expectedOutput)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"garagebalance_dependency_audit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var reportPath = Path.Combine(temporaryDirectory, "report.json");
            var runnerPath = Path.Combine(temporaryDirectory, "run.ps1");
            await File.WriteAllTextAsync(reportPath, CreateReport(scenario), new UTF8Encoding(false));
            await File.WriteAllTextAsync(runnerPath,
                """
                param([string]$AuditScript)
                $ErrorActionPreference = 'Stop'
                function global:dotnet {
                    $global:LASTEXITCODE = [int]$env:GARAGEBALANCE_MOCK_AUDIT_EXIT
                    Get-Content -LiteralPath $env:GARAGEBALANCE_MOCK_AUDIT_REPORT -Raw
                }
                & $AuditScript
                """, new UTF8Encoding(false));

            var startInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            if (OperatingSystem.IsWindows())
            {
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
            }

            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(runnerPath);
            startInfo.ArgumentList.Add("-AuditScript");
            startInfo.ArgumentList.Add(Path.Combine(BranchVerificationTests.FindRepositoryRoot(),
                "infrastructure", "scripts", "verify-backend-dependencies.ps1"));
            startInfo.Environment["GARAGEBALANCE_MOCK_AUDIT_EXIT"] = commandExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            startInfo.Environment["GARAGEBALANCE_MOCK_AUDIT_REPORT"] = reportPath;

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("PowerShell was not started.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var output = await outputTask + await errorTask;
                Assert.True(shouldSucceed ? process.ExitCode == 0 : process.ExitCode != 0, output);
                Assert.Contains(expectedOutput, output, StringComparison.Ordinal);
                if (!shouldSucceed)
                {
                    Assert.DoesNotContain("backendDependencyAudit=passed", output, StringComparison.Ordinal);
                }
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static string CreateReport(string scenario) => scenario switch
    {
        "empty" => """{"version":1,"projects":[]}""",
        "missing" => """{"version":1}""",
        "null-project" => """{"version":1,"projects":[null]}""",
        "missing-path" => """{"version":1,"projects":[{}]}""",
        "version" => """{"version":2,"projects":[{"path":"Example.csproj"}]}""",
        "warning" => """{"version":1,"projects":[{"path":"Example.csproj"}],"logs":[{"level":"warning","message":"Feed unavailable"}]}""",
        "error" => """{"version":1,"projects":[{"path":"Example.csproj"}],"logs":[{"level":"error","message":"Feed unavailable"}]}""",
        "invalid" => "not a JSON report",
        "direct" or "transitive" => $$"""
            {"version":1,"projects":[{"path":"Example.csproj","frameworks":[{"framework":"net10.0","{{(scenario == "direct" ? "topLevelPackages" : "transitivePackages")}}":[{"id":"Example.Package","resolvedVersion":"1.0.0","vulnerabilities":[{"severity":"High","advisoryurl":"https://example.invalid/advisory"}]}]}]}]}
            """,
        _ => """{"version":1,"projects":[{"path":"Example.csproj"}]}""",
    };
}
