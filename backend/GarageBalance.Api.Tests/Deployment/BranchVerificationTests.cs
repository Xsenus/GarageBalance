namespace GarageBalance.Api.Tests.Deployment;

public sealed class BranchVerificationTests
{
    [Fact]
    public void FeatureBranchesAndPullRequestsRunCompleteSuitesWithoutDeploymentCredentials()
    {
        var workflow = ReadWorkflow();

        Assert.Contains("push:\n    branches-ignore:\n      - master", workflow, StringComparison.Ordinal);
        Assert.Contains("  pull_request:", workflow, StringComparison.Ordinal);
        Assert.Contains("  workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: read", workflow, StringComparison.Ordinal);
        Assert.Contains("image: postgres:17", workflow, StringComparison.Ordinal);
        Assert.Contains("GARAGEBALANCE_POSTGRES_TEST_CONNECTION:", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test GarageBalance.slnx --configuration Release --no-build --no-restore", workflow, StringComparison.Ordinal);
        Assert.Contains("if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }", workflow, StringComparison.Ordinal);
        Assert.Contains("--collect:\"XPlat Code Coverage\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--settings backend-coverage.runsettings", workflow, StringComparison.Ordinal);
        Assert.Contains("verify-backend-coverage.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run test:coverage", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--filter", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--quick", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("environment: staging", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ssh ", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("  deploy:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh release create", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void QualityGatesCoverBuildsFormattingPrivacyAuditsAndMigrationDrift()
    {
        var workflow = ReadWorkflow();

        foreach (var requiredCommand in new[]
        {
            "dotnet build GarageBalance.slnx --configuration Release --no-restore",
            "dotnet format GarageBalance.slnx --verify-no-changes --no-restore",
            "verify-backend-dependencies.ps1",
            "verify-package-privacy.ps1",
            "check-docker-distribution.ps1 -RequireDocker",
            "dotnet tool run dotnet-ef migrations has-pending-model-changes",
            "dotnet tool run dotnet-ef migrations script --idempotent",
            "npm run lint",
            "npm run build",
            "npm run check:bundle",
            "npm audit --package-lock-only --audit-level=high",
        })
        {
            Assert.Contains(requiredCommand, workflow, StringComparison.Ordinal);
        }

        Assert.Contains("if: always()\n        uses: actions/upload-artifact@v6", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void DockerVerificationWaitsForAllGatesAndAlwaysRemovesItsTestVolumes()
    {
        var workflow = ReadWorkflow();
        var dockerJob = workflow[workflow.IndexOf("  docker:\n", StringComparison.Ordinal)..];

        Assert.Contains("needs:\n      - backend\n      - frontend\n      - quality", dockerJob, StringComparison.Ordinal);
        Assert.Contains("COMPOSE_PROJECT_NAME: garagebalance-verification", dockerJob, StringComparison.Ordinal);
        Assert.Contains("docker compose config --quiet", dockerJob, StringComparison.Ordinal);
        Assert.Contains("docker compose build", dockerJob, StringComparison.Ordinal);
        Assert.Contains("docker compose up --detach --wait --wait-timeout 180", dockerJob, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:5580/health/ready", dockerJob, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:55173/health/ready", dockerJob, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:55173/api/auth/me)", dockerJob, StringComparison.Ordinal);
        Assert.Contains("if: always()\n        run: docker compose down --volumes --remove-orphans", dockerJob, StringComparison.Ordinal);
        Assert.DoesNotContain("docker push", dockerJob, StringComparison.Ordinal);
    }

    private static string ReadWorkflow() => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), ".github", "workflows", "verify.yml")).ReplaceLineEndings("\n");

    internal static string FindRepositoryRoot()
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

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
