namespace GarageBalance.Api.Tests.Deployment;

public sealed class GitHandoffChecklistTests
{
    [Fact]
    public void GitHandoffChecklistProtectsPrivateFilesAndRequiresVerificationBeforePush()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "git-handoff-checklist.md"));

        Assert.Contains("git status --short", document, StringComparison.Ordinal);
        Assert.Contains("git ls-files --others --exclude-standard", document, StringComparison.Ordinal);
        Assert.Contains(".gitignore", document, StringComparison.Ordinal);
        Assert.Contains("releases.json", document, StringComparison.Ordinal);
        Assert.Contains("dotnet test", document, StringComparison.Ordinal);
        Assert.Contains("npm run test -- --runInBand", document, StringComparison.Ordinal);
        Assert.Contains("npm run build", document, StringComparison.Ordinal);
        Assert.Contains("npm run lint", document, StringComparison.Ordinal);
        Assert.Contains("dotnet format --verify-no-changes", document, StringComparison.Ordinal);
        Assert.Contains("generate-migration-script.ps1", document, StringComparison.Ordinal);
        Assert.Contains("git diff --check", document, StringComparison.Ordinal);
        Assert.Contains("UTF-8/no BOM", document, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=False", document, StringComparison.Ordinal);
        Assert.Contains("git push", document, StringComparison.Ordinal);
        Assert.Contains("только после прямой команды пользователя", document, StringComparison.Ordinal);
        Assert.Contains(".accdb", document, StringComparison.Ordinal);
        Assert.Contains(".pgdump", document, StringComparison.Ordinal);
        Assert.Contains("artifacts/", document, StringComparison.Ordinal);
        Assert.Contains("appsettings.Local.json", document, StringComparison.Ordinal);
        Assert.Contains("private-imports/", document, StringComparison.Ordinal);
        Assert.Contains("1C Fresh", document, StringComparison.Ordinal);
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
