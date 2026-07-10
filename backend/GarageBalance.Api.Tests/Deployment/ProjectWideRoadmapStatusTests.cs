namespace GarageBalance.Api.Tests.Deployment;

public sealed class ProjectWideRoadmapStatusTests
{
    [Fact]
    public void UsersObjectCoverageIsMarkedCompleteWhenAuthAndUserAuditFlowsAreCovered()
    {
        var usersLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Пользователи: создание", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Пользователи:", usersLine, StringComparison.Ordinal);
        Assert.Contains("отключение активного пользователя требует причину", usersLine, StringComparison.Ordinal);
        Assert.Contains("отключенного пользователя можно восстановить", usersLine, StringComparison.Ordinal);
        Assert.Contains("повторное сохранение без изменения", usersLine, StringComparison.Ordinal);
        Assert.Contains("auth.login_failed", usersLine, StringComparison.Ordinal);
        Assert.Contains("auth.login_inactive", usersLine, StringComparison.Ordinal);
        Assert.Contains("auth.login_rate_limited", usersLine, StringComparison.Ordinal);
        Assert.Contains("429", usersLine, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnersObjectCoverageIsMarkedCompleteWhenCreateUpdateArchiveRestoreFlowsAreCovered()
    {
        var ownersLine = File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(), "docs", "project-wide-history-and-safety-roadmap.md"))
            .Single(line => line.Contains("Владельцы: создание", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]` Владельцы:", ownersLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.owner_created", ownersLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.owner_updated", ownersLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.owner_archived", ownersLine, StringComparison.Ordinal);
        Assert.Contains("dictionary.owner_restored", ownersLine, StringComparison.Ordinal);
        Assert.Contains("архивирование требует причину", ownersLine, StringComparison.Ordinal);
        Assert.Contains("no-op не создает событие", ownersLine, StringComparison.Ordinal);
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
