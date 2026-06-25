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

    [Fact]
    public void ConcreteMigrations_HaveStrictlyIncreasingUniqueTimestamps()
    {
        var migrationNames = EnumerateConcreteMigrationNames()
            .Order(StringComparer.Ordinal)
            .ToArray();

        var previousTimestamp = string.Empty;
        var duplicateTimestamps = migrationNames
            .Select(name => name[..14])
            .GroupBy(timestamp => timestamp, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicateTimestamps);
        foreach (var migrationName in migrationNames)
        {
            var timestamp = migrationName[..14];

            Assert.Matches("^\\d{14}_.+\\.cs$", migrationName);
            Assert.True(
                string.CompareOrdinal(timestamp, previousTimestamp) > 0,
                $"Migration timestamp must be strictly increasing. Offender: {migrationName}.");
            previousTimestamp = timestamp;
        }
    }

    [Fact]
    public void DesignerMigrations_HaveMigrationAttributeMatchingFileName()
    {
        var migrationRoot = FindMigrationRoot();
        var offenders = Directory.EnumerateFiles(migrationRoot, "*.Designer.cs", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var expectedMigrationId = Path.GetFileName(path).Replace(".Designer.cs", string.Empty, StringComparison.Ordinal);
                var text = File.ReadAllText(path);
                var expectedAttribute = $"[Migration(\"{expectedMigrationId}\")]";

                return text.Contains(expectedAttribute, StringComparison.Ordinal)
                    ? null
                    : $"{Path.GetFileName(path)} must contain {expectedAttribute}";
            })
            .Where(message => message is not null)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void MigrationSourceFiles_AreUtf8WithoutBom()
    {
        var migrationRoot = FindMigrationRoot();
        var offenders = Directory.EnumerateFiles(migrationRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(HasUtf8Bom)
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> EnumerateConcreteMigrationNames()
    {
        return Directory.EnumerateFiles(FindMigrationRoot(), "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Where(name => !name.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(name => !name.Equals("GarageBalanceDbContextModelSnapshot.cs", StringComparison.Ordinal));
    }

    private static string FindMigrationRoot()
    {
        return Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "Migrations");
    }

    private static bool HasUtf8Bom(string path)
    {
        var bytes = File.ReadAllBytes(path);

        return bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF;
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
