namespace GarageBalance.Api.Tests.Deployment;

public sealed class FrontendDialogPolicyTests
{
    [Fact]
    public void ProductionFrontendDoesNotUseBlockingBrowserDialogs()
    {
        var repositoryRoot = FindRepositoryRoot();
        var frontendSource = Path.Combine(repositoryRoot, "frontend", "src");
        var disallowedTokens = new[]
        {
            "window.confirm",
            "window.alert",
            "window.prompt",
            "confirm(",
            "confirm (",
            "alert(",
            "alert (",
            "prompt(",
            "prompt ("
        };

        var sourceFiles = Directory
            .EnumerateFiles(frontendSource, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                IsFrontendSourceFile(path) &&
                !Path.GetFileName(path).Contains(".test.", StringComparison.Ordinal) &&
                !Path.GetFileName(path).Contains(".spec.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            var relativePath = Path.GetRelativePath(repositoryRoot, file);

            foreach (var token in disallowedTokens)
            {
                Assert.False(
                    text.Contains(token, StringComparison.Ordinal),
                    $"{relativePath} contains blocking browser dialog token `{token}`. Use the internal confirmation dialog instead.");
            }
        }
    }

    private static bool IsFrontendSourceFile(string path)
    {
        return path.EndsWith(".ts", StringComparison.Ordinal) ||
            path.EndsWith(".tsx", StringComparison.Ordinal) ||
            path.EndsWith(".js", StringComparison.Ordinal) ||
            path.EndsWith(".jsx", StringComparison.Ordinal);
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
