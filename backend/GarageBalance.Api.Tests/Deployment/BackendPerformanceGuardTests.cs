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
    public void ScreenReportQueries_UseDatabaseLimitsForVisibleRows()
    {
        var source = ReadApiSource("Application/Reports/ReportService.cs");

        Assert.Contains("GetIncomeReportWithoutSearchAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetExpenseReportWithoutSearchAsync", source, StringComparison.Ordinal);
        Assert.Contains("BuildGarageRowsWithoutSearchAsync", source, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(source, "ApplyReportRowLimit(") >= 7,
            "Screen report visible rows must be bounded before materialization for consolidated garage, income, expense, accrual and starting-balance segments.");
        Assert.True(
            CountOccurrences(source, ".Take(NormalizeReportLimit(limit.Value))") >= 1,
            "Report visible-row queries must use the normalized server-side limit before ToListAsync.");
        Assert.True(
            CountOccurrences(source, "CountAsync(cancellationToken)") >= 6,
            "Report totals must keep total row counts without materializing every visible-row candidate.");
        Assert.True(
            CountOccurrences(source, "SumAsync(") >= 6,
            "Report totals must be aggregated in the database instead of being derived only from materialized rows.");
    }

    [Fact]
    public void CashPaymentScreenQuery_UsesDatabaseCountSumAndLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Application/Reports/ReportService.cs");

        Assert.Matches(
            BoundedQueryRegex(@"GetCashPaymentReportAsync[\s\S]*?IsNpgsql\(\)[\s\S]*?CountAsync\(cancellationToken\)[\s\S]*?SumAsync\(operation => operation\.Amount, cancellationToken\)[\s\S]*?ApplyReportRowLimit\([\s\S]*?request\.Limit\)[\s\S]*?ToListAsync\(cancellationToken\)"),
            source);
        Assert.Contains("operation.Supplier.Name.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Contains("operation.ExpenseType.Name.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
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
    public void ImportCreatedRecordsQuery_NormalizesLimitBeforePostgresMaterialization()
    {
        var source = ReadApiSource("Application/Import/ImportService.cs");

        Assert.Matches(
            BoundedQueryRegex(@"GetAccessImportCreatedRecordsAsync[\s\S]*?var limit = NormalizeLimit\(request\.Limit, 100, 500\)[\s\S]*?IsNpgsql\(\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.Matches(
            BoundedQueryRegex(@"GetAccessImportCreatedRecordsAsync[\s\S]*?else[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToList\(\)"),
            source);
    }

    [Fact]
    public void AuditHistoryQueries_KeepServerSidePaginationAndStructuredFiltersBeforeMaterialization()
    {
        var source = ReadApiSource("Application/Audit/AuditService.cs");
        var requiredFilters = new[]
        {
            "auditEvent.CreatedAtUtc >= request.DateFrom.Value",
            "auditEvent.CreatedAtUtc <= request.DateTo.Value",
            "ApplyNonDateFilters(query, request)",
            "auditEvent.Action == action",
            "ApplySectionFilter(query, request.Section)",
            "ApplyActionKindFilter(query, request.ActionKind)",
            "auditEvent.EntityType == entityType",
            "auditEvent.ActorUserId == request.ActorUserId.Value",
            "ApplyQuickFilter(query, request.QuickFilter)",
            "ApplyRelatedFilters(query, request)",
            "auditEvent.RelatedGarageNumber",
            "auditEvent.RelatedAccountingMonth",
            "auditEvent.RelatedCounterpartyName",
            "auditEvent.RelatedDocumentNumber"
        };

        Assert.Contains("GetEventsPageAsync", source, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Matches(
            BoundedQueryRegex(@"ApplyNonDateFilters\(query, request\)[\s\S]*?CountAsync\(cancellationToken\)[\s\S]*?OrderByDescending\(auditEvent => auditEvent\.CreatedAtUtc\)[\s\S]*?\.Skip\(offset\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.Matches(
            BoundedQueryRegex(@"OrderByDescending\(auditEvent => auditEvent\.CreatedAtUtc\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.All(requiredFilters, filter => Assert.Contains(filter, source, StringComparison.Ordinal));
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

    [Fact]
    public void FinalPerformanceChecklistCoversAutomatedGatesPostgresQueriesFrontendAndAcceptanceThresholds()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "final-performance-checklist.md"));

        Assert.Contains("final performance verification", document, StringComparison.Ordinal);
        Assert.Contains("BackendPerformanceGuardTests", document, StringComparison.Ordinal);
        Assert.Contains("dotnet test GarageBalance.slnx --no-restore --configuration Debug", document, StringComparison.Ordinal);
        Assert.Contains("npm run test -- --reporter=dot --maxWorkers=1 --testTimeout=180000", document, StringComparison.Ordinal);
        Assert.Contains("npm run check:bundle", document, StringComparison.Ordinal);
        Assert.Contains("main JS gzip: `180 KiB`", document, StringComparison.Ordinal);
        Assert.Contains("EXPLAIN (ANALYZE, BUFFERS)", document, StringComparison.Ordinal);
        Assert.Contains("GIN trigram indexes", document, StringComparison.Ordinal);
        Assert.Contains("limit", document, StringComparison.Ordinal);
        Assert.Contains("rowCount", document, StringComparison.Ordinal);
        Assert.Contains("CountAsync", document, StringComparison.Ordinal);
        Assert.Contains("SumAsync", document, StringComparison.Ordinal);
        Assert.Contains("browser console", document, StringComparison.Ordinal);
        Assert.Contains("realistic customer data", document, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=False", document, StringComparison.Ordinal);
        Assert.Contains("psql=False", document, StringComparison.Ordinal);
        Assert.Contains("docker=False", document, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Не удалось найти корень репозитория GarageBalance.");
    }

    private static Regex BoundedQueryRegex(string pattern)
    {
        return new Regex(pattern, RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    }
}
