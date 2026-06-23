namespace GarageBalance.Api.Tests.Security;

public sealed class SensitiveFileGitIgnoreTests
{
    private static readonly string[] RequiredIgnoreRules =
    [
        ".env",
        ".env.*",
        "!.env.example",
        "appsettings.Local.json",
        "*.bak",
        "*.backup",
        "*.dump",
        "*.pgdump",
        "*.sql.gz",
        "*.db",
        "*.sqlite",
        "*.sqlite3",
        "*.accdb",
        "*.mdb",
        "backups/",
        "dumps/",
        "local-db-backups/",
        "private-imports/",
        "imports/private/",
        "imports/raw/"
    ];

    [Fact]
    public void RootGitIgnore_KeepsSecretsAccessFilesAndBackupsOutOfGit()
    {
        var repositoryRoot = FindRepositoryRoot();
        var gitIgnorePath = Path.Combine(repositoryRoot, ".gitignore");

        Assert.True(File.Exists(gitIgnorePath), $"Root .gitignore was not found at {gitIgnorePath}.");

        var rules = File.ReadAllLines(gitIgnorePath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var requiredRule in RequiredIgnoreRules)
        {
            Assert.Contains(requiredRule, rules);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find GarageBalance repository root.");
    }
}
