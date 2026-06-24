using System.Text.RegularExpressions;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class BackendPerformanceGuardTests
{
    [Theory]
    [InlineData("Application/Audit/AuditService.cs", @"OrderByDescending[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Application/Users/UserManagementService.cs", @"OrderBy\(user => user\.[^)]+\)[\s\S]*?\.Take\(NormalizeListLimit\(limit\)\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Application/Finance/FinanceService.cs", @"\.Take\(NormalizeListLimit\(request\.Limit\)\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Application/Import/ImportService.cs", @"else[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Application/Import/ImportQuarantineService.cs", @"else[\s\S]*?\.Take\(normalizedLimit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Application/Dictionaries/DictionaryService.cs", @"\.Take\(NormalizeListLimit\(limit\)\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    public void ApplicationListQueries_MaterializeBoundedResultSets(string relativePath, string boundedQueryPattern)
    {
        var source = ReadApiSource(relativePath);

        Assert.Matches(BoundedQueryRegex(boundedQueryPattern), source);
    }

    [Fact]
    public void FinanceWorkingLists_AllUseNormalizedRequestLimit()
    {
        var source = ReadApiSource("Application/Finance/FinanceService.cs");

        Assert.True(
            CountOccurrences(source, ".Take(NormalizeListLimit(request.Limit))") >= 4,
            "Finance working lists must keep explicit server-side limits before materialization.");
    }

    [Fact]
    public void FinancePageQueries_UseCountSkipAndTakeBeforeMaterialization()
    {
        var source = ReadApiSource("Application/Finance/FinanceService.cs");

        Assert.True(
            CountOccurrences(source, "CountAsync(cancellationToken)") >= 4,
            "Finance page queries must return total counts without materializing full result sets.");
        Assert.True(
            CountOccurrences(source, ".Skip(normalizedOffset)") >= 4,
            "Finance page queries must apply server-side offset before materialization.");
        Assert.True(
            CountOccurrences(source, ".Take(normalizedLimit)") >= 4,
            "Finance page queries must apply server-side limit before materialization.");
        Assert.True(
            CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 8,
            "Finance list queries should materialize only after ordering and server-side bounds.");
    }

    [Fact]
    public void ImportSqliteFallbacks_AreExplicitlyScopedToTestProviderAndStillApplyLimit()
    {
        var source = ReadApiSource("Application/Import/ImportService.cs");

        Assert.Contains("Microsoft.EntityFrameworkCore.Sqlite", source, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(source, ".Take(limit)") >= 4,
            "Import list and log queries must keep limits in both PostgreSQL and SQLite fallback branches.");
    }

    [Fact]
    public void DictionarySearchQueries_KeepExplicitLimitForSearchAndDefaultLists()
    {
        var source = ReadApiSource("Application/Dictionaries/DictionaryService.cs");

        Assert.True(
            CountOccurrences(source, ".Take(NormalizeListLimit(limit))") >= 5,
            "Dictionary default lists must use normalized server-side limits.");
        Assert.True(
            CountOccurrences(source, ".Take(normalizedLimit)") >= 2,
            "Dictionary search branches must keep their explicit normalized limit.");
    }

    [Fact]
    public void DictionarySearchMigration_AddsPostgresTrigramIndexesForContainsSearch()
    {
        var source = ReadApiSource("Infrastructure/Data/Migrations/20260625031500_DictionarySearchTrigramIndexes.cs");
        var expectedIndexNames = new[]
        {
            "IX_owners_LastName_trgm",
            "IX_owners_FirstName_trgm",
            "IX_owners_MiddleName_trgm",
            "IX_owners_Phone_trgm",
            "IX_owners_FullName_trgm",
            "IX_garages_Number_trgm",
            "IX_suppliers_Name_trgm",
            "IX_suppliers_Inn_trgm",
            "IX_suppliers_ContactPerson_trgm"
        };

        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pg_trgm", source, StringComparison.Ordinal);
        Assert.Equal(expectedIndexNames.Length, CountOccurrences(source, "CreateTrigramIndex(migrationBuilder,"));
        Assert.True(
            CountOccurrences(source, "USING gin") >= 1,
            "Dictionary contains-search must keep PostgreSQL GIN trigram indexes.");
        Assert.True(
            CountOccurrences(source, "gin_trgm_ops") >= 1,
            "Dictionary contains-search indexes must use pg_trgm operator class.");
        Assert.True(
            CountOccurrences(source, "WHERE \"IsArchived\" = FALSE") >= 1,
            "Dictionary search indexes must stay scoped to active records.");
        Assert.All(expectedIndexNames, indexName => Assert.Contains(indexName, source, StringComparison.Ordinal));
    }

    private static string ReadApiSource(string relativePath)
    {
        return File.ReadAllText(Path.Combine(FindApiProjectRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindApiProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "backend", "GarageBalance.Api");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Не удалось найти проект GarageBalance.Api.");
    }

    private static Regex BoundedQueryRegex(string pattern)
    {
        return new Regex(pattern, RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    }
}
