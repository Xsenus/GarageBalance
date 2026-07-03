namespace GarageBalance.Api.Tests.Deployment;

public sealed class ApiEndpointDocumentationTests
{
    [Theory]
    [InlineData("/api/import/access/runs/{id}/report")]
    [InlineData("/api/reports/consolidated/export/xlsx")]
    [InlineData("/api/reports/consolidated/export/pdf")]
    [InlineData("/api/reports/income/export/xlsx")]
    [InlineData("/api/reports/income/export/pdf")]
    [InlineData("/api/reports/expense/export/xlsx")]
    [InlineData("/api/reports/expense/export/pdf")]
    public void ReadmeDocumentsAuditWritingExportsAsPost(string route)
    {
        var readme = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "README.md"));

        Assert.Contains($"POST {route}", readme, StringComparison.Ordinal);
        Assert.DoesNotContain($"GET {route}", readme, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/import/access/runs/{id}/report")]
    [InlineData("/api/reports/consolidated/export/xlsx")]
    [InlineData("/api/reports/consolidated/export/pdf")]
    [InlineData("/api/reports/income/export/xlsx")]
    [InlineData("/api/reports/income/export/pdf")]
    [InlineData("/api/reports/expense/export/xlsx")]
    [InlineData("/api/reports/expense/export/pdf")]
    public void ProjectRoadmapDocumentsAuditWritingExportsAsPost(string route)
    {
        var roadmap = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "project-roadmap.md"));

        Assert.Contains($"POST {route}", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain($"GET {route}", roadmap, StringComparison.Ordinal);
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
