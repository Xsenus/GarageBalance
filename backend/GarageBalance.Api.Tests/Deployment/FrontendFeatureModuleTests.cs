namespace GarageBalance.Api.Tests.Deployment;

public sealed class FrontendFeatureModuleTests
{
    [Fact]
    public void ReleasePanelRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var releasePanelText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "releases",
            "ReleasePanel.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        Assert.Contains("import { ReleasePanel } from './features/releases/ReleasePanel'", appText, StringComparison.Ordinal);
        Assert.Contains("<ReleasePanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function ReleasePanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("createReleaseEditorState", appText, StringComparison.Ordinal);

        Assert.Contains("export function ReleasePanel(", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("releaseClient.getReleases", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("releaseClient.getManageableReleases", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("hasPermission(auth, permissions.appReleasesManage)", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Что нового\"", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/releases/ReleasePanel.tsx", roadmapLine, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
