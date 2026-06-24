namespace GarageBalance.Api.Tests.Deployment;

public sealed class DatabaseMigrationPolicyTests
{
    [Fact]
    public void ProductionBackendCode_DoesNotCreateOrDeleteSchemaOutsideMigrations()
    {
        var apiRoot = Path.Combine(FindRepositoryRoot(), "backend", "GarageBalance.Api");
        var forbiddenPatterns = new[]
        {
            "EnsureCreated(",
            "EnsureCreatedAsync(",
            "EnsureDeleted(",
            "EnsureDeletedAsync("
        };

        var offenders = EnumerateProductionSourceFiles(apiRoot)
            .SelectMany(path =>
            {
                var text = File.ReadAllText(path);

                return forbiddenPatterns
                    .Where(pattern => text.Contains(pattern, StringComparison.Ordinal))
                    .Select(pattern => $"{Path.GetRelativePath(apiRoot, path)} contains {pattern}");
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ProductionBackendCode_UsesMigrationFolderForSchemaDdl()
    {
        var apiRoot = Path.Combine(FindRepositoryRoot(), "backend", "GarageBalance.Api");
        var migrationRoot = Path.Combine(apiRoot, "Infrastructure", "Data", "Migrations");
        var forbiddenDdl = new[]
        {
            "CREATE TABLE",
            "ALTER TABLE",
            "DROP TABLE",
            "CREATE INDEX",
            "DROP INDEX"
        };

        var offenders = EnumerateProductionSourceFiles(apiRoot)
            .Where(path => !IsUnderDirectory(path, migrationRoot))
            .SelectMany(path =>
            {
                var text = File.ReadAllText(path);

                return forbiddenDdl
                    .Where(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    .Select(pattern => $"{Path.GetRelativePath(apiRoot, path)} contains raw schema DDL: {pattern}");
            })
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void MigrationFolder_ContainsSnapshotAndConcreteMigrations()
    {
        var migrationRoot = Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "Migrations");

        Assert.True(Directory.Exists(migrationRoot), $"Migration folder was not found: {migrationRoot}");
        Assert.True(
            File.Exists(Path.Combine(migrationRoot, "GarageBalanceDbContextModelSnapshot.cs")),
            "EF Core model snapshot is required to keep migration diffs reviewable.");

        var concreteMigrations = Directory.EnumerateFiles(migrationRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).Equals("GarageBalanceDbContextModelSnapshot.cs", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(concreteMigrations);
    }

    private static IEnumerable<string> EnumerateProductionSourceFiles(string apiRoot)
    {
        return Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsUnderDirectory(path, Path.Combine(apiRoot, "bin")))
            .Where(path => !IsUnderDirectory(path, Path.Combine(apiRoot, "obj")));
    }

    private static bool IsUnderDirectory(string filePath, string directoryPath)
    {
        var relativePath = Path.GetRelativePath(directoryPath, filePath);

        return relativePath.Length > 0 &&
            relativePath[0] != '.' &&
            !Path.IsPathRooted(relativePath);
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
