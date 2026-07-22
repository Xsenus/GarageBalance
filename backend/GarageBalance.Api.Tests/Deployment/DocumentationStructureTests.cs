using System.Text;
using System.Text.RegularExpressions;

namespace GarageBalance.Api.Tests.Deployment;

public sealed partial class DocumentationStructureTests
{
    private static readonly string[] ExpectedDocumentationFiles =
    [
        "README.md",
        "admin-operations-guide.md",
        "data-model-erd.md",
        "development-guide.md",
        "diagnostic-logging-guide.md",
        "docker-install-update-guide.md",
        "docker-windows-lan-guide.md",
        "final-performance-checklist.md",
        "integrations-guide.md",
        "local-pc-install-checklist.md",
        "migration-verification-checklist.md",
        "monthly-cycle-checklist.md",
        "postgres-backup-restore.md",
        "reports-guide.md",
        "roadmaps/customer-comments-2026-07-22-roadmap.md",
        "roadmaps/docker-user-distribution-roadmap.md",
        "security-data-protection.md",
        "testing-guide.md",
        "troubleshooting-guide.md",
        "user-guide.md",
        "version-update-checklist.md",
        "vps-deployment-checklist.md"
    ];

    [Fact]
    public void DocumentationDirectoryContainsOnlyMaintainedGuides()
    {
        var docsDirectory = Path.Combine(FindRepositoryRoot(), "docs");
        var actualFiles = Directory
            .EnumerateFiles(docsDirectory, "*.md", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(docsDirectory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedDocumentationFiles.Order(StringComparer.Ordinal), actualFiles);
    }

    [Fact]
    public void RootReadmeIsAConciseEntryPointAndDocumentationIndexIsComplete()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readmePath = Path.Combine(repositoryRoot, "README.md");
        var readme = File.ReadAllText(readmePath);
        var index = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "README.md"));

        Assert.True(new FileInfo(readmePath).Length < 30_000, "README should remain a concise project entry point.");
        Assert.Contains("Быстрый запуск через Docker", readme, StringComparison.Ordinal);
        Assert.Contains("Локальная разработка", readme, StringComparison.Ordinal);
        Assert.Contains("Сборка и проверки", readme, StringComparison.Ordinal);
        Assert.Contains("Миграции базы данных", readme, StringComparison.Ordinal);

        foreach (var fileName in ExpectedDocumentationFiles.Where(fileName => fileName != "README.md"))
        {
            Assert.Contains($"({fileName})", index, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LocalMarkdownLinksResolve()
    {
        var repositoryRoot = FindRepositoryRoot();
        var markdownFiles = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "docs"), "*.md", SearchOption.AllDirectories)
            .Append(Path.Combine(repositoryRoot, "README.md"))
            .Append(Path.Combine(repositoryRoot, "AGENTS.md"));

        foreach (var markdownFile in markdownFiles)
        {
            var content = File.ReadAllText(markdownFile);

            foreach (Match match in MarkdownLinkRegex().Matches(content))
            {
                var target = match.Groups[1].Value.Trim();
                if (target.StartsWith('#') ||
                    target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var pathPart = Uri.UnescapeDataString(target.Split('#', 2)[0]);
                var resolvedPath = Path.GetFullPath(pathPart, Path.GetDirectoryName(markdownFile)!);

                Assert.True(
                    File.Exists(resolvedPath) || Directory.Exists(resolvedPath),
                    $"Broken local Markdown link '{target}' in '{Path.GetRelativePath(repositoryRoot, markdownFile)}'.");
            }
        }
    }

    [Fact]
    public void MaintainedDocumentationUsesStrictUtf8WithoutBom()
    {
        var repositoryRoot = FindRepositoryRoot();
        var markdownFiles = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "docs"), "*.md", SearchOption.AllDirectories)
            .Append(Path.Combine(repositoryRoot, "README.md"))
            .Append(Path.Combine(repositoryRoot, "AGENTS.md"));
        var strictUtf8 = new UTF8Encoding(false, true);

        foreach (var markdownFile in markdownFiles)
        {
            var bytes = File.ReadAllBytes(markdownFile);
            Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), $"UTF-8 BOM found in '{markdownFile}'.");

            var content = strictUtf8.GetString(bytes);
            Assert.DoesNotContain('\uFFFD', content);
        }
    }

    [GeneratedRegex(@"\[[^\]]+\]\(([^)]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex();

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
