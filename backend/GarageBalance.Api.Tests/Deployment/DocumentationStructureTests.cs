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
        "disaster-recovery.md",
        "docker-install-update-guide.md",
        "docker-windows-lan-guide.md",
        "final-performance-checklist.md",
        "full-project-behavior-audit-2026-08-03.md",
        "integrations-guide.md",
        "local-pc-install-checklist.md",
        "migration-verification-checklist.md",
        "monthly-cycle-checklist.md",
        "performance/report-query-baseline.md",
        "postgres-backup-restore.md",
        "reports-guide.md",
        "roadmaps/comments-080926-implementation-roadmap.md",
        "roadmaps/customer-comments-2026-07-22-roadmap.md",
        "roadmaps/customer-comments-2026-07-27-roadmap.md",
        "roadmaps/customer-comments-2026-08-24-roadmap.md",
        "roadmaps/customer-comments-2026-08-26-roadmap.md",
        "roadmaps/customer-comments-2026-09-02-roadmap.md",
        "roadmaps/customer-comments-2026-09-05-roadmap.md",
        "roadmaps/customer-comments-2026-09-11-roadmap.md",
        "roadmaps/customer-comments-2026-09-18-roadmap.md",
        "roadmaps/dictionary-and-form-cleanup-2026-08-12-roadmap.md",
        "roadmaps/docker-user-distribution-roadmap.md",
        "roadmaps/full-performance-optimization-2026-07-29-roadmap.md",
        "roadmaps/full-project-behavior-audit-2026-08-03-roadmap.md",
        "roadmaps/full-project-remediation-2026-08-04-roadmap.md",
        "roadmaps/full-visual-product-audit-2026-09-06-roadmap.md",
        "roadmaps/garage-annual-payment-fields-2026-09-16-roadmap.md",
        "roadmaps/garage-annual-payments-search-payouts-2026-09-16-roadmap.md",
        "roadmaps/income-accrual-integrity-2026-09-04-roadmap.md",
        "roadmaps/sgk-compact-ui-2026-09-14-roadmap.md",
        "roadmaps/tariffs-and-income-full-audit-2026-09-11-roadmap.md",
        "roadmaps/ui-forms-and-dictionaries-audit-2026-08-01-roadmap.md",
        "roadmaps/visual-audit-remediation-2026-09-07-roadmap.md",
        "security-data-protection.md",
        "staging-showcase-guide.md",
        "storage-migration-cli.md",
        "storage-operations.md",
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
        // Dated audit working copies are local evidence, not maintained product guides.
        // They are intentionally kept outside the documentation index and Git history.
        var actualFiles = Directory
            .EnumerateFiles(docsDirectory, "*.md", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(docsDirectory, path).Replace('\\', '/'))
            .Where(path => !DatedAuditWorkingCopyRegex().IsMatch(path))
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

    [GeneratedRegex(@"(^|/)storage-backup-multistorage-audit-\d{4}-\d{2}-\d{2}\.md$", RegexOptions.CultureInvariant)]
    private static partial Regex DatedAuditWorkingCopyRegex();

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
