using System.Text.Json;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class FormsComplianceReleaseNotesTests
{
    private static readonly string[][] RequiredTopicVersions =
    [
        ["0.711.0", "0.712.0", "0.714.0"],
        ["0.716.0", "0.717.0", "0.718.0", "0.719.0", "0.720.0", "0.721.0", "0.755.0"],
        ["0.723.0", "0.724.0", "0.725.0", "0.726.0", "0.727.0", "0.728.0", "0.729.0", "0.730.0", "0.731.0", "0.732.0", "0.751.0"],
        ["0.738.0", "0.739.0", "0.740.0", "0.741.0", "0.742.0"],
        ["0.745.0", "0.746.0", "0.747.0", "0.748.0", "0.749.0", "0.750.0"],
        ["0.757.0"]
    ];

    [Fact]
    public void FormsComplianceTopicsHavePublishedUserFacingReleaseNotes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var releasePath = Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "AppReleases", "releases.json");
        var coveragePath = Path.Combine(repositoryRoot, "docs", "forms-compliance-release-notes.md");
        var roadmapPath = Path.Combine(repositoryRoot, "docs", "forms-compliance-fixes-roadmap.md");
        var coverage = File.ReadAllText(coveragePath);
        var roadmap = File.ReadAllText(roadmapPath);

        using var document = JsonDocument.Parse(File.ReadAllText(releasePath));
        var releases = document.RootElement.EnumerateArray().ToDictionary(
            release => release.GetProperty("version").GetString()!,
            StringComparer.Ordinal);

        foreach (var topicVersions in RequiredTopicVersions)
        {
            foreach (var version in topicVersions)
            {
                var release = releases[version];
                Assert.True(release.GetProperty("isPublished").GetBoolean(), $"Release {version} must be published.");
                Assert.False(string.IsNullOrWhiteSpace(release.GetProperty("title").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(release.GetProperty("summary").GetString()));

                var items = release.GetProperty("items").EnumerateArray().ToArray();
                Assert.NotEmpty(items);
                Assert.All(items, item =>
                {
                    Assert.Contains(item.GetProperty("type").GetString(), new[] { "new", "improved", "fixed", "important" });
                    Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("text").GetString()));
                });
            }
        }

        Assert.Contains("Просрочка и распределение поступлений", coverage, StringComparison.Ordinal);
        Assert.Contains("Выплаты, долг и аванс", coverage, StringComparison.Ordinal);
        Assert.Contains("Показания счетчиков", coverage, StringComparison.Ordinal);
        Assert.Contains("Фонды и назначения", coverage, StringComparison.Ordinal);
        Assert.Contains("Восемь отчетов и экспорт", coverage, StringComparison.Ordinal);
        Assert.Contains("Зеленые колонки гаражей", coverage, StringComparison.Ordinal);
        Assert.Contains("повторный сводный релиз не создается", coverage, StringComparison.Ordinal);
        Assert.Contains(
            "- [x] Добавить понятные пользователю записи `Что нового` по просрочке, выплатам, счетчикам, фондам и отчетам.",
            roadmap,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GarageBalance.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
