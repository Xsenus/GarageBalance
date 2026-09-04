namespace GarageBalance.Api.Tests.Deployment;

public sealed class DockerDistributionTests
{
    [Fact]
    public void ReleaseComposeUsesVersionedImagesAndPersistentStorage()
    {
        var distribution = DistributionDirectory();
        var compose = File.ReadAllText(Path.Combine(distribution, "docker-compose.yml"));

        Assert.Contains("name: garagebalance", compose, StringComparison.Ordinal);
        Assert.Contains("image: garagebalance-api:${GARAGEBALANCE_VERSION", compose, StringComparison.Ordinal);
        Assert.Contains("image: garagebalance-frontend:${GARAGEBALANCE_VERSION", compose, StringComparison.Ordinal);
        Assert.Contains("image: postgres:17-alpine", compose, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(compose, "pull_policy: never"));
        Assert.DoesNotContain("ghcr.io/", compose, StringComparison.Ordinal);
        Assert.Contains("postgres-data:/var/lib/postgresql/data", compose, StringComparison.Ordinal);
        Assert.Contains("data-protection-keys:${DATA_PROTECTION_KEYS_PATH", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("build:", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsCommandsGenerateSecretsLoadImagesAndProtectUpdates()
    {
        var distribution = DistributionDirectory();
        var common = File.ReadAllText(Path.Combine(distribution, "GarageBalance.Common.ps1"));
        var update = File.ReadAllText(Path.Combine(distribution, "update.ps1"));
        var diagnostics = File.ReadAllText(Path.Combine(distribution, "diagnostics.ps1"));
        var stop = File.ReadAllText(Path.Combine(distribution, "stop.ps1"));

        Assert.Contains("RandomNumberGenerator", common, StringComparison.Ordinal);
        Assert.Contains("INITIAL_ADMIN_PASSWORD", common, StringComparison.Ordinal);
        Assert.Contains("admin-credentials.txt", common, StringComparison.Ordinal);
        Assert.Contains("Complete-GarageBalanceInitialAdministratorBootstrap", common, StringComparison.Ordinal);
        Assert.Contains("WaitForExit($TimeoutSeconds * 1000)", common, StringComparison.Ordinal);
        Assert.Contains("$process.Kill()", common, StringComparison.Ordinal);
        Assert.Contains("docker", common, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"load\", \"--input\"", common, StringComparison.Ordinal);
        Assert.Contains("Wait-GarageBalanceHealth", common, StringComparison.Ordinal);
        Assert.Contains("backup.ps1", update, StringComparison.Ordinal);
        Assert.True(
            update.IndexOf("backup.ps1", StringComparison.Ordinal) < update.IndexOf("Import-GarageBalanceImages", StringComparison.Ordinal),
            "A verified backup must run before new images are imported.");
        Assert.DoesNotContain("GarageBalanceEnvFile", diagnostics.Split("$commands =", 2)[0], StringComparison.Ordinal);
        Assert.Contains("WaitForExit($TimeoutSeconds * 1000)", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Команда Docker прервана после тайм-аута", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Invoke-GarageBalanceComposeQuiet", File.ReadAllText(Path.Combine(distribution, "backup.ps1")), StringComparison.Ordinal);
        Assert.Contains("готовый backup-файл не найден или пуст", File.ReadAllText(Path.Combine(distribution, "backup.ps1")), StringComparison.Ordinal);
        Assert.DoesNotContain("-v", stop, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnvironmentInitializationGeneratesAndThenRemovesPlainAdminBootstrapPassword()
    {
        var source = DistributionDirectory();
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"garagebalance_distribution_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            File.Copy(Path.Combine(source, ".env.example"), Path.Combine(temporaryDirectory, ".env.example"));
            File.Copy(Path.Combine(source, "GarageBalance.Common.ps1"), Path.Combine(temporaryDirectory, "GarageBalance.Common.ps1"));
            var testScript = Path.Combine(temporaryDirectory, "verify.ps1");
            await File.WriteAllTextAsync(
                testScript,
                """
                param([Parameter(Mandatory = $true)][string]$Root)
                $ErrorActionPreference = "Stop"
                . (Join-Path $Root "GarageBalance.Common.ps1")
                Initialize-GarageBalanceEnvironment
                $before = Get-GarageBalanceEnvironment
                $password = $before["INITIAL_ADMIN_PASSWORD"]
                $credentialsPath = Join-Path $Root "admin-credentials.txt"
                $credentials = [System.IO.File]::ReadAllText($credentialsPath)
                if ($before["INITIAL_ADMIN_ENABLED"] -ne "true" -or
                    $password -eq "__GENERATE__" -or
                    $password.Length -lt 20 -or
                    -not $credentials.Contains($password) -or
                    -not $credentials.Contains("admin@garagebalance.local")) {
                    throw "Initial administrator credentials were not generated correctly."
                }
                Complete-GarageBalanceInitialAdministratorBootstrap
                $after = Get-GarageBalanceEnvironment
                if ($after["INITIAL_ADMIN_ENABLED"] -ne "false" -or
                    $after["INITIAL_ADMIN_PASSWORD"] -ne "__CREATED__" -or
                    -not ([System.IO.File]::ReadAllText($credentialsPath)).Contains($password)) {
                    throw "Initial administrator bootstrap was not finalized safely."
                }
                Write-Output "initial-admin-bootstrap=ok"
                """,
                new System.Text.UTF8Encoding(false));

            var executable = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
            var startInfo = new System.Diagnostics.ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(testScript);
            startInfo.ArgumentList.Add("-Root");
            startInfo.ArgumentList.Add(temporaryDirectory);

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException("PowerShell was not started.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;

            Assert.True(process.ExitCode == 0, error);
            Assert.Contains("initial-admin-bootstrap=ok", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Пароль:", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void ReleaseWorkflowVerifiesBuildsAndPackagesAutonomousBundle()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "publish-docker-release.yml"));

        Assert.Contains("tags:", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test GarageBalance.slnx", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run test:coverage", workflow, StringComparison.Ordinal);
        Assert.Contains("check-docker-distribution.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("docker/build-push-action@v6", workflow, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(workflow, "load: true"));
        Assert.DoesNotContain("docker/login-action", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("push: true", workflow, StringComparison.Ordinal);
        Assert.Contains("docker save", workflow, StringComparison.Ordinal);
        Assert.Contains("postgres-17-alpine.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("GarageBalance-Docker-${VERSION}.zip", workflow, StringComparison.Ordinal);
        Assert.Contains("sha256sum", workflow, StringComparison.Ordinal);
        Assert.Contains("gh release create", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiImageUsesTheSamePostgreSqlMajorVersionAsReleaseCompose()
    {
        var root = FindRepositoryRoot();
        var dockerfile = File.ReadAllText(Path.Combine(root, "backend", "GarageBalance.Api", "Dockerfile"));
        var compose = File.ReadAllText(Path.Combine(root, "distribution", "docker", "docker-compose.yml"));

        Assert.Contains("postgresql-client-17", dockerfile, StringComparison.Ordinal);
        Assert.Contains("image: postgres:17-alpine", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void DistributionContainsOnlyTemplatesNotRealSecrets()
    {
        var distribution = DistributionDirectory();
        var environment = File.ReadAllText(Path.Combine(distribution, ".env.example"));

        Assert.False(File.Exists(Path.Combine(distribution, ".env")));
        Assert.Contains("POSTGRES_PASSWORD=__GENERATE__", environment, StringComparison.Ordinal);
        Assert.Contains("JWT_SIGNING_KEY=__GENERATE__", environment, StringComparison.Ordinal);
        Assert.Contains("INITIAL_ADMIN_ENABLED=true", environment, StringComparison.Ordinal);
        Assert.Contains("INITIAL_ADMIN_EMAIL=admin@garagebalance.local", environment, StringComparison.Ordinal);
        Assert.Contains("INITIAL_ADMIN_PASSWORD=__GENERATE__", environment, StringComparison.Ordinal);
        Assert.Contains("GARAGEBALANCE_VERSION=__GARAGEBALANCE_VERSION__", environment, StringComparison.Ordinal);
        Assert.Equal("__GARAGEBALANCE_VERSION__", File.ReadAllText(Path.Combine(distribution, "release-version.txt")).Trim());
    }

    [Fact]
    public void ReleaseComposeCreatesInitialAdministratorWithoutExposingItsPasswordInDiagnostics()
    {
        var distribution = DistributionDirectory();
        var compose = File.ReadAllText(Path.Combine(distribution, "docker-compose.yml"));
        var start = File.ReadAllText(Path.Combine(distribution, "start.ps1"));
        var common = File.ReadAllText(Path.Combine(distribution, "GarageBalance.Common.ps1"));
        var diagnostics = File.ReadAllText(Path.Combine(distribution, "diagnostics.ps1"));

        Assert.Contains("InitialAdministrator__Enabled: ${INITIAL_ADMIN_ENABLED:-false}", compose, StringComparison.Ordinal);
        Assert.Contains("InitialAdministrator__Email: ${INITIAL_ADMIN_EMAIL:-}", compose, StringComparison.Ordinal);
        Assert.Contains("InitialAdministrator__DisplayName: ${INITIAL_ADMIN_DISPLAY_NAME:-}", compose, StringComparison.Ordinal);
        Assert.Contains("InitialAdministrator__Password: ${INITIAL_ADMIN_PASSWORD:-}", compose, StringComparison.Ordinal);
        Assert.Contains("Complete-GarageBalanceInitialAdministratorBootstrap", start, StringComparison.Ordinal);
        Assert.Contains("--force-recreate\", \"api", start, StringComparison.Ordinal);
        Assert.Contains("INITIAL_ADMIN_ENABLED\" -Value \"false", common, StringComparison.Ordinal);
        Assert.Contains("INITIAL_ADMIN_PASSWORD\" -Value \"__CREATED__", common, StringComparison.Ordinal);
        Assert.DoesNotContain("admin-credentials.txt", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("INITIAL_ADMIN_PASSWORD", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public void OfflineBundleImportsEveryImageRequiredByCompose()
    {
        var distribution = DistributionDirectory();
        var common = File.ReadAllText(Path.Combine(distribution, "GarageBalance.Common.ps1"));
        var compose = File.ReadAllText(Path.Combine(distribution, "docker-compose.yml"));
        var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "publish-docker-release.yml"));

        foreach (var archive in new[]
        {
            "garagebalance-api-$Version.tar.gz",
            "garagebalance-frontend-$Version.tar.gz",
            "postgres-17-alpine.tar.gz"
        })
        {
            Assert.Contains(archive, common, StringComparison.Ordinal);
            Assert.Contains(archive.Replace("$Version", "${VERSION}", StringComparison.Ordinal), workflow, StringComparison.Ordinal);
        }

        Assert.Contains("image: postgres:17-alpine", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("docker pull", common, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsLanGuideMatchesSupportedComposeSettingsAndSafeFirewallScope()
    {
        var root = FindRepositoryRoot();
        var distribution = DistributionDirectory();
        var guide = File.ReadAllText(Path.Combine(root, "docs", "docker-windows-lan-guide.md"));
        var bundleReadme = File.ReadAllText(Path.Combine(distribution, "README.txt"));
        var environment = File.ReadAllText(Path.Combine(distribution, ".env.example"));
        var compose = File.ReadAllText(Path.Combine(distribution, "docker-compose.yml"));

        Assert.Contains("FRONTEND_BIND_ADDRESS=0.0.0.0", guide, StringComparison.Ordinal);
        Assert.Contains("FRONTEND_ORIGIN=http://192.168.1.50:8080", guide, StringComparison.Ordinal);
        Assert.Contains("API_BIND_ADDRESS=127.0.0.1", guide, StringComparison.Ordinal);
        Assert.Contains("POSTGRES_BIND_ADDRESS=127.0.0.1", guide, StringComparison.Ordinal);
        Assert.Contains("New-NetFirewallRule", guide, StringComparison.Ordinal);
        Assert.Contains("-RemoteAddress LocalSubnet", guide, StringComparison.Ordinal);
        Assert.Contains("backup.cmd", guide, StringComparison.Ordinal);
        Assert.Contains("не копирует `.env`", guide, StringComparison.Ordinal);
        Assert.Contains("garagebalance_data-protection-keys", guide, StringComparison.Ordinal);
        Assert.Contains("docker-windows-lan-guide.md", bundleReadme, StringComparison.Ordinal);

        foreach (var variable in new[] { "FRONTEND_BIND_ADDRESS", "FRONTEND_PORT", "FRONTEND_ORIGIN", "API_BIND_ADDRESS", "POSTGRES_BIND_ADDRESS" })
        {
            Assert.Contains($"{variable}=", environment, StringComparison.Ordinal);
            Assert.Contains($"${{{variable}", compose, StringComparison.Ordinal);
        }
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var position = 0;
        while ((position = value.IndexOf(search, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += search.Length;
        }

        return count;
    }

    private static string DistributionDirectory() =>
        Path.Combine(FindRepositoryRoot(), "distribution", "docker");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")) &&
                (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                 File.Exists(Path.Combine(directory.FullName, ".git"))))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
