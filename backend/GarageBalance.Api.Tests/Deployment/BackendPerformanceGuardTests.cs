using System.Text.RegularExpressions;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class BackendPerformanceGuardTests
{
    [Theory]
    [InlineData("Infrastructure/Data/EfAuditEventRepository.cs", @"OrderByDescending[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Infrastructure/Data/EfUserManagementRepository.cs", @"OrderBy\(user => user\.[^)]+\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Application/Finance/FinanceService.cs", @"\.Take\(NormalizeListLimit\(request\.Limit\)\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Infrastructure/Data/EfImportRepository.cs", @"IsSqliteProvider[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Infrastructure/Data/EfImportQuarantineRepository.cs", @"return await query[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Application/Dictionaries/DictionaryService.cs", @"\.Take\(NormalizeListLimit\(limit\)\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    public void ProductionListQueries_MaterializeBoundedResultSets(string relativePath, string boundedQueryPattern)
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
    public void OwnerRepository_UsesDatabaseCountOffsetAndLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfOwnerRepository.cs");

        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 2);
        Assert.True(CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 2);
    }

    [Fact]
    public void SupplierGroupRepository_UsesProductionCountOffsetAndLimitWithScopedSqliteFallback()
    {
        var source = ReadApiSource("Infrastructure/Data/EfSupplierGroupRepository.cs");

        Assert.Contains("IsSqliteProvider()", source, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 3);
    }

    [Fact]
    public void SupplierRepository_UsesDatabaseCountOffsetAndLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfSupplierRepository.cs");

        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 2);
        Assert.True(CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 2);
    }

    [Fact]
    public void SupplierContactRepository_UsesDatabaseLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfSupplierContactRepository.cs");

        Assert.Contains(".Take(limit)", source, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync(cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaffDepartmentRepository_UsesDatabaseLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfStaffDepartmentRepository.cs");
        Assert.Contains(".Take(limit)", source, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync(cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaffMemberRepository_UsesDatabaseLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfStaffMemberRepository.cs");
        Assert.Contains(".Take(limit)", source, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync(cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaffMemberRepository_ExpenseWorksheetQueryKeepsCompleteFilteredActiveSet()
    {
        var source = ReadApiSource("Infrastructure/Data/EfStaffMemberRepository.cs");
        Assert.Contains("GetActiveForExpenseWorksheetAsync", source, StringComparison.Ordinal);
        Assert.Contains(".Where(member => !member.IsArchived)", source, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(member => member.FullName)", source, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync(cancellationToken)", source, StringComparison.Ordinal);
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
        var garageSource = ReadApiSource("Infrastructure/Data/EfConsolidatedGarageReportQuery.cs");
        var expenseSource = ReadApiSource("Infrastructure/Data/EfExpenseReportQuery.cs");
        var incomeSource = ReadApiSource("Infrastructure/Data/EfIncomeReportQuery.cs");

        Assert.Contains("incomeReportQuery.GetRowsAsync", source, StringComparison.Ordinal);
        Assert.Contains("expenseReportQuery.GetRowsAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetRowsWithoutSearchAsync", garageSource, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(incomeSource, "ApplyLimit(") >= 4 &&
            CountOccurrences(expenseSource, "ApplyLimit(") >= 4,
            "Remaining report visible rows must be bounded before materialization for income, expense, accrual and starting-balance segments.");
        Assert.True(
            incomeSource.Contains("query.Take(limit.Value)", StringComparison.Ordinal) &&
            expenseSource.Contains("query.Take(limit.Value)", StringComparison.Ordinal),
            "Report visible-row queries must use the normalized server-side limit before ToListAsync.");
        Assert.True(
            CountOccurrences(incomeSource, "CountAsync(cancellationToken)") >= 3 &&
            CountOccurrences(expenseSource, "CountAsync(cancellationToken)") >= 3,
            "Report totals must keep total row counts without materializing every visible-row candidate.");
        Assert.True(
            CountOccurrences(incomeSource, "SumAsync(") >= 3 &&
            CountOccurrences(expenseSource, "SumAsync(") >= 3,
            "Report totals must be aggregated in the database instead of being derived only from materialized rows.");
        Assert.Matches(
            BoundedQueryRegex(@"GetRowsWithoutSearchAsync[\s\S]*?CountAsync\(cancellationToken\)[\s\S]*?ApplyLimit\(query, limit\)\.ToListAsync\(cancellationToken\)"),
            garageSource);
        Assert.True(
            CountOccurrences(garageSource, ".GroupBy(") >= 3,
            "Search-compatible consolidated garage fallback must aggregate income, accrual and readings by garage.");
    }

    [Fact]
    public void CashPaymentScreenQuery_UsesDatabaseCountSumAndLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfCashMovementReportQuery.cs");

        Assert.Matches(
            BoundedQueryRegex(@"GetCashPaymentsAsync[\s\S]*?IsNpgsql\(\)[\s\S]*?CountAsync\(cancellationToken\)[\s\S]*?SumAsync\(operation => operation\.Amount, cancellationToken\)[\s\S]*?ApplyLimit\(ordered, limit\)\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.Contains("operation.Supplier.Name.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Contains("operation.ExpenseType.Name.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BankDepositScreenQuery_UsesDatabaseCountSumAndLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfCashMovementReportQuery.cs");

        Assert.Matches(
            BoundedQueryRegex(@"GetBankDepositsAsync[\s\S]*?operation\.Fund\.Name\.ToLower\(\)\.Contains\(normalizedSearch\)[\s\S]*?CountAsync\(cancellationToken\)[\s\S]*?SumAsync\(operation => operation\.Amount, cancellationToken\)[\s\S]*?ApplyLimit\(orderedQuery, limit\)\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.Contains("operation.Reason.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FundChangeScreenQuery_UsesDatabaseCountSumsAndLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfFundChangeReportQuery.cs");

        Assert.Matches(
            BoundedQueryRegex(@"GetFundChangesAsync[\s\S]*?CountAsync\(cancellationToken\)[\s\S]*?FundOperationKinds\.Deposit[\s\S]*?SumAsync[\s\S]*?FundOperationKinds\.Withdraw[\s\S]*?SumAsync[\s\S]*?ApplyLimit\(ordered, limit\)\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.Contains("operation.Fund.Name.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Contains("operation.OperationKind.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Contains("operation.Reason.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Contains("ToDictionaryAsync(user => user.Id, user => user.DisplayName", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsolidatedMonthlyQuery_AggregatesMonthlyTotalsAndCountsInDatabase()
    {
        var source = ReadApiSource("Infrastructure/Data/EfConsolidatedMonthlyReportQuery.cs");

        Assert.True(
            CountOccurrences(source, ".GroupBy(") >= 4,
            "Consolidated monthly query must group income, expense, accrual and meter rows in the database.");
        Assert.True(
            CountOccurrences(source, "group.Sum(") >= 3,
            "Consolidated monetary totals must be aggregated before materialization.");
        Assert.True(
            CountOccurrences(source, "group.Count()") >= 4,
            "Consolidated monthly row counts must be aggregated before materialization.");
        Assert.Contains("SumAsync(garage => garage.StartingBalance, cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FeeReportQuery_AggregatesTotalsAndGarageRowsInDatabase()
    {
        var source = ReadApiSource("Infrastructure/Data/EfFeeReportQuery.cs");

        Assert.True(
            CountOccurrences(source, ".GroupBy(") >= 4,
            "Fee report query must aggregate accrual totals, collected totals, garage accruals and garage payments in the database.");
        Assert.True(
            CountOccurrences(source, "group.Sum(") >= 4,
            "Fee monetary totals must be summed before materialization.");
        Assert.True(
            CountOccurrences(source, "ToDictionaryAsync(") >= 2,
            "Fee summary totals must be materialized as bounded dictionaries from grouped database results.");
        Assert.Contains("group.Max(operation => (DateOnly?)operation.OperationDate)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportSqliteFallbacks_AreExplicitlyScopedToTestProviderAndStillApplyLimit()
    {
        var source = ReadApiSource("Infrastructure/Data/EfImportRepository.cs");

        Assert.Contains("Microsoft.EntityFrameworkCore.Sqlite", source, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(source, ".Take(limit)") >= 4,
            "Import list and log queries must keep limits in both PostgreSQL and SQLite fallback branches.");
    }

    [Fact]
    public void ImportCreatedRecordsQuery_NormalizesLimitBeforePostgresMaterialization()
    {
        var serviceSource = ReadApiSource("Application/Import/ImportService.cs");
        var repositorySource = ReadApiSource("Infrastructure/Data/EfImportRepository.cs");

        Assert.Contains("var limit = NormalizeLimit(request.Limit, 100, 500)", serviceSource, StringComparison.Ordinal);
        Assert.Matches(
            BoundedQueryRegex(@"GetCreatedRecordsAsync[\s\S]*?IsNpgsql\(\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)"),
            repositorySource);
        Assert.Matches(
            BoundedQueryRegex(@"GetCreatedRecordsAsync[\s\S]*?return \(await query\.ToListAsync\(cancellationToken\)\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToList\(\)"),
            repositorySource);
    }

    [Fact]
    public void AuditHistoryQueries_KeepServerSidePaginationAndStructuredFiltersBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfAuditEventRepository.cs");
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
        var staffMemberRepositorySource = ReadApiSource("Infrastructure/Data/EfStaffMemberRepository.cs");

        Assert.True(
            CountOccurrences(source, ".Take(NormalizeListLimit(limit))") >= 4,
            "Dictionary default lists must use normalized server-side limits.");
        Assert.Contains(
            "staffMemberRepository.GetListAsync(departmentId, normalizedSearch, includeArchived, NormalizeListLimit(limit)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", staffMemberRepositorySource, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(source, ".Take(normalizedLimit)") >= 2,
            "Dictionary search branches must keep their explicit normalized limit.");
    }

    [Fact]
    public void FundOperationsAndReleaseLists_KeepNormalizedOutputBounds()
    {
        var fundSource = ReadApiSource("Application/Funds/FundService.cs");
        var fundRepositorySource = ReadApiSource("Infrastructure/Data/EfFundRepository.cs");
        var releaseSource = ReadApiSource("Application/Releases/AppReleaseService.cs");

        Assert.Contains("var boundedLimit = Math.Clamp(limit, 1, 100)", fundSource, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(fundRepositorySource, ".Take(limit)") >= 2,
            "Fund operation lists must apply the same bound in PostgreSQL and SQLite branches.");
        Assert.Contains("private const int DefaultLimit = 10", releaseSource, StringComparison.Ordinal);
        Assert.Contains("private const int MaxLimit = 50", releaseSource, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(releaseSource, ".Take(NormalizeLimit(limit))") >= 2,
            "Public and manageable release lists must keep normalized output limits.");
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
