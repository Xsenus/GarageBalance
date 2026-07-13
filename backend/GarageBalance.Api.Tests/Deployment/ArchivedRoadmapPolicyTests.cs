using System.Security.Cryptography;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class ArchivedRoadmapPolicyTests
{
    [Fact]
    public void ArchivedRoadmapsAreMovedFrozenAndExcludedFromAutomaticWork()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainRoadmap = Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md");
        var historyRoadmap = Path.Combine(repositoryRoot, "docs", "archive", "project-wide-history-and-safety-roadmap.md");
        var agentInstructions = File.ReadAllText(Path.Combine(repositoryRoot, "AGENTS.md"));

        Assert.True(File.Exists(mainRoadmap));
        Assert.True(File.Exists(historyRoadmap));
        Assert.False(File.Exists(Path.Combine(repositoryRoot, "docs", "project-roadmap.md")));
        Assert.False(File.Exists(Path.Combine(repositoryRoot, "docs", "project-wide-history-and-safety-roadmap.md")));
        Assert.Equal("7DD08A8A0B0EB0283AE2982D1FAF3B36EF9300CFAF22260A088A28258944478B", ComputeSha256(mainRoadmap));
        Assert.Equal("3CD88F3609B98E5532B4AEA5D2AD95718FBC6E19C39963C26D333B87AEFD42B9", ComputeSha256(historyRoadmap));
        Assert.Contains("Everything under `docs/archive/` is frozen archive material.", agentInstructions, StringComparison.Ordinal);
        Assert.Contains("may resume only after the user explicitly names the archived roadmap or item", agentInstructions, StringComparison.Ordinal);
        Assert.Contains("do not update archived history", agentInstructions, StringComparison.Ordinal);
    }

    private static string ComputeSha256(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
