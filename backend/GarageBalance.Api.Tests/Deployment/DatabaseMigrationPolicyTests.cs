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

    [Fact]
    public void MigrationScriptGeneratorCreatesIdempotentSqlArtifact()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "infrastructure",
            "scripts",
            "generate-migration-script.ps1"));

        Assert.Contains("[CmdletBinding()]", script, StringComparison.Ordinal);
        Assert.Contains("artifacts\\deploy-migrations.sql", script, StringComparison.Ordinal);
        Assert.Contains("dotnet-ef", script, StringComparison.Ordinal);
        Assert.Contains("IsPathRooted", script, StringComparison.Ordinal);
        Assert.Contains("migrations", script, StringComparison.Ordinal);
        Assert.Contains("script", script, StringComparison.Ordinal);
        Assert.Contains("--idempotent", script, StringComparison.Ordinal);
        Assert.Contains("--project", script, StringComparison.Ordinal);
        Assert.Contains("--startup-project", script, StringComparison.Ordinal);
        Assert.Contains("Migration script is empty", script, StringComparison.Ordinal);
        Assert.Contains("migrationScriptPath=", script, StringComparison.Ordinal);
        Assert.Contains("migrationScriptBytes=", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditEventsSchema_KeepsStructuredHistoryColumnsAndIndexes()
    {
        var migrationRoot = FindMigrationRoot();
        var snapshot = File.ReadAllText(Path.Combine(migrationRoot, "GarageBalanceDbContextModelSnapshot.cs"));
        var migrationSources = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(migrationRoot, "*.cs", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetFileName(path).EndsWith(".Designer.cs", StringComparison.Ordinal))
                .Where(path => !Path.GetFileName(path).Equals("GarageBalanceDbContextModelSnapshot.cs", StringComparison.Ordinal))
                .Select(File.ReadAllText));

        var requiredStringProperties = new[]
        {
            "Section",
            "ActionKind",
            "EntityDisplayName",
            "RelatedGarageId",
            "RelatedGarageNumber",
            "RelatedAccountingMonth",
            "RelatedCounterpartyId",
            "RelatedCounterpartyName",
            "RelatedDocumentId",
            "RelatedDocumentNumber"
        };

        var requiredSnapshotIndexes = new[]
        {
            "b.HasIndex(\"ActorUserId\")",
            "b.HasIndex(\"CreatedAtUtc\")",
            "b.HasIndex(\"Section\")",
            "b.HasIndex(\"ActionKind\")",
            "b.HasIndex(\"EntityType\", \"EntityId\")",
            "b.HasIndex(\"Section\", \"ActionKind\", \"CreatedAtUtc\")",
            "b.HasIndex(\"RelatedGarageId\")",
            "b.HasIndex(\"RelatedGarageNumber\")",
            "b.HasIndex(\"RelatedAccountingMonth\")",
            "b.HasIndex(\"RelatedCounterpartyId\")",
            "b.HasIndex(\"RelatedCounterpartyName\")",
            "b.HasIndex(\"RelatedDocumentId\")",
            "b.HasIndex(\"RelatedDocumentNumber\")"
        };

        var requiredMigrationIndexNames = new[]
        {
            "IX_audit_events_ActorUserId",
            "IX_audit_events_Section",
            "IX_audit_events_ActionKind",
            "IX_audit_events_Section_ActionKind_CreatedAtUtc",
            "IX_audit_events_RelatedGarageId",
            "IX_audit_events_RelatedGarageNumber",
            "IX_audit_events_RelatedAccountingMonth",
            "IX_audit_events_RelatedCounterpartyId",
            "IX_audit_events_RelatedCounterpartyName",
            "IX_audit_events_RelatedDocumentId",
            "IX_audit_events_RelatedDocumentNumber"
        };

        foreach (var property in requiredStringProperties)
        {
            Assert.Contains($"b.Property<string>(\"{property}\")", snapshot, StringComparison.Ordinal);
        }

        foreach (var index in requiredSnapshotIndexes)
        {
            Assert.Contains(index, snapshot, StringComparison.Ordinal);
        }

        foreach (var indexName in requiredMigrationIndexNames)
        {
            Assert.Contains(indexName, migrationSources, StringComparison.Ordinal);
        }
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
