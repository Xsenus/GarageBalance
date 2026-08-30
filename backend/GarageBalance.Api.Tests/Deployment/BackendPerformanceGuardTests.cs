using System.Text.RegularExpressions;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class BackendPerformanceGuardTests
{
    [Theory]
    [InlineData("Infrastructure/Data/EfAuditEventRepository.cs", @"OrderByDescending[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Infrastructure/Data/EfUserManagementRepository.cs", @"OrderBy\(user => user\.[^)]+\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Infrastructure/Data/EfFinancialOperationRepository.cs", @"return await Order\(ApplySearch[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Infrastructure/Data/EfImportRepository.cs", @"IsSqliteProvider[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Infrastructure/Data/EfImportQuarantineRepository.cs", @"return await query[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    [InlineData("Infrastructure/Data/EfFeeCampaignRepository.cs", @"\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)")]
    public void ProductionListQueries_MaterializeBoundedResultSets(string relativePath, string boundedQueryPattern)
    {
        var source = ReadApiSource(relativePath);

        Assert.Matches(BoundedQueryRegex(boundedQueryPattern), source);
    }

    [Fact]
    public void FinanceWorkingLists_AllUseNormalizedRequestLimit()
    {
        var source = ReadApiSource("Application/Finance/FinanceService.cs");
        var financialOperationRepositorySource = ReadApiSource("Infrastructure/Data/EfFinancialOperationRepository.cs");
        var meterReadingRepositorySource = ReadApiSource("Infrastructure/Data/EfMeterReadingRepository.cs");
        var accrualRepositorySource = ReadApiSource("Infrastructure/Data/EfAccrualRepository.cs");
        var supplierAccrualRepositorySource = ReadApiSource("Infrastructure/Data/EfSupplierAccrualRepository.cs");

        Assert.Contains("financialOperationRepository.GetListAsync", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(financialOperationRepositorySource, ".Take(limit)") >= 4);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", financialOperationRepositorySource, StringComparison.Ordinal);
        Assert.Equal(6, CountOccurrences(financialOperationRepositorySource, "EF.Functions.ILike("));
        Assert.DoesNotContain(".ToLower().Contains(normalizedSearch)", financialOperationRepositorySource, StringComparison.Ordinal);
        Assert.Contains("meterReadingRepository.GetListAsync", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(meterReadingRepositorySource, ".Take(limit)") >= 4);
        Assert.Contains("accrualRepository.GetListAsync", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(accrualRepositorySource, ".Take(limit)") >= 4);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", accrualRepositorySource, StringComparison.Ordinal);
        Assert.Equal(6, CountOccurrences(accrualRepositorySource, "EF.Functions.ILike("));
        Assert.DoesNotContain(".ToLower().Contains(normalizedSearch)", accrualRepositorySource, StringComparison.Ordinal);
        Assert.Contains("supplierAccrualRepository.GetListAsync", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(supplierAccrualRepositorySource, ".Take(limit)") >= 4);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", supplierAccrualRepositorySource, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(supplierAccrualRepositorySource, "EF.Functions.ILike("));
        Assert.DoesNotContain(".ToLower().Contains(normalizedSearch)", supplierAccrualRepositorySource, StringComparison.Ordinal);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", meterReadingRepositorySource, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(meterReadingRepositorySource, "EF.Functions.ILike("));
        Assert.DoesNotContain(".ToLower().Contains(normalizedSearch)", meterReadingRepositorySource, StringComparison.Ordinal);
    }

    [Fact]
    public void FinanceSummary_UsesSingleAggregateQueryPerGrowingTable()
    {
        var totalsSource = ReadApiSource("Infrastructure/Data/EfFinanceTotalsQuery.cs");
        var serviceSource = ReadApiSource("Application/Finance/FinanceService.cs");

        Assert.Contains("IncomeCount = group.Count(operation => operation.OperationKind == FinancialOperationKinds.Income)", totalsSource, StringComparison.Ordinal);
        Assert.Contains("ExpenseCount = group.Count(operation => operation.OperationKind == FinancialOperationKinds.Expense)", totalsSource, StringComparison.Ordinal);
        Assert.Contains("AccrualTotal = group.Sum(accrual => (decimal?)accrual.Amount) ?? 0m", totalsSource, StringComparison.Ordinal);
        Assert.Contains(".Concat(accrualTotalsQuery)", totalsSource, StringComparison.Ordinal);
        Assert.Contains(".Concat(meterReadingTotalsQuery)", totalsSource, StringComparison.Ordinal);
        Assert.Contains(".Concat(supplierAccrualTotalsQuery)", totalsSource, StringComparison.Ordinal);
        Assert.Contains("var rows = await operationTotalsQuery", totalsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("financeSectionCountQuery", serviceSource, StringComparison.Ordinal);
        Assert.Contains("financeTotalsQuery.GetAsync", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("meterReadingRepository.CountActiveAsync", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("supplierAccrualRepository.CountActiveAsync", serviceSource, StringComparison.Ordinal);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", totalsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("operation.DocumentNumber.ToLower().Contains(normalizedSearch)", totalsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("operation.Comment.ToLower().Contains(normalizedSearch)", totalsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("operation.CounterpartyName.ToLower().Contains(normalizedSearch)", totalsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("accrual.Comment.ToLower().Contains(normalizedSearch)", totalsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("meterReading.Comment.ToLower().Contains(normalizedSearch)", totalsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("supplierAccrual.Comment.ToLower().Contains(normalizedSearch)", totalsSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToLower().Contains(normalizedSearch)", totalsSource, StringComparison.Ordinal);
        Assert.Equal(15, CountOccurrences(totalsSource, "EF.Functions.ILike("));
        Assert.Contains("operation.Garage.Number, pattern", totalsSource, StringComparison.Ordinal);
        Assert.Contains("operation.Supplier.Name, pattern", totalsSource, StringComparison.Ordinal);
        Assert.Contains("operation.StaffMember.FullName, pattern", totalsSource, StringComparison.Ordinal);
        Assert.Contains("accrual.IncomeType.Name, pattern", totalsSource, StringComparison.Ordinal);
        Assert.Contains("accrual.ExpenseType.Name, pattern", totalsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FinanceAvailableBalanceQuery_CombinesIncomeExpensesAndBankDepositsIntoOneDatabaseCommand()
    {
        var source = ReadApiSource("Infrastructure/Data/EfFinanceAvailableBalanceQuery.cs");
        var serviceSource = ReadApiSource("Application/Finance/FinanceService.cs");

        Assert.Contains("financialOperationQuery", source, StringComparison.Ordinal);
        Assert.Contains("bankDepositQuery", source, StringComparison.Ordinal);
        Assert.Contains("operation.OperationKind == FinancialOperationKinds.Income", source, StringComparison.Ordinal);
        Assert.Contains("CashExpenseTotal = group.Sum", source, StringComparison.Ordinal);
        Assert.Contains("BankExpenseTotal = group.Sum", source, StringComparison.Ordinal);
        Assert.Contains("dbContext.CashBankTransfers", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, ".Concat("));
        Assert.True(CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 1);
        Assert.Contains("var availableAmounts = CalculateAvailableAmounts(worksheetData.AvailableBalance);", serviceSource, StringComparison.Ordinal);
        Assert.Contains("var balance = await financeAvailableBalanceQuery.GetAsync", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FundDashboard_ReusesLoadedFundsAndAggregatesTotalsInOneDatabaseCommand()
    {
        var serviceSource = ReadApiSource("Application/Funds/FundService.cs");
        var repositorySource = ReadApiSource("Infrastructure/Data/EfFundRepository.cs");
        var totalsMethod = repositorySource[
            repositorySource.IndexOf("public async Task<FundTotalsData> GetTotalsAsync", StringComparison.Ordinal)..repositorySource.IndexOf("public async Task<decimal> GetAvailableToDistributeAsync", StringComparison.Ordinal)];
        var availableToDistributePostgreSqlBranch = repositorySource[
            repositorySource.IndexOf("public async Task<decimal> GetAvailableToDistributeAsync", StringComparison.Ordinal)..repositorySource.IndexOf("var linkedFinancialOperationIds", StringComparison.Ordinal)];

        Assert.Contains("var funds = (await repository.GetFundsAsync(cancellationToken)).ToList();", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureDefaultFundsAsync", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChangesAsync", serviceSource[
            serviceSource.IndexOf("public async Task<IReadOnlyList<FundDto>> GetFundsAsync", StringComparison.Ordinal)..serviceSource.IndexOf("public async Task<FundResult<FundDto>> CreateFundAsync", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.DoesNotContain("GetNormalizedFundNamesAsync", serviceSource, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(totalsMethod, ".Sum("));
        Assert.Equal(2, CountOccurrences(totalsMethod, ".GroupBy("));
        Assert.Equal(1, CountOccurrences(totalsMethod, ".Concat("));
        Assert.Equal(1, CountOccurrences(totalsMethod, ".ToListAsync(cancellationToken)"));
        Assert.DoesNotContain("FirstOrDefaultAsync", totalsMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("SumAsync", totalsMethod, StringComparison.Ordinal);
        Assert.Contains("SUM(delta) OVER", availableToDistributePostgreSqlBranch, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<decimal>", availableToDistributePostgreSqlBranch, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(availableToDistributePostgreSqlBranch, ".SingleAsync(cancellationToken)"));
        Assert.DoesNotContain("ToListAsync", availableToDistributePostgreSqlBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void FundCorrections_LoadOnlyTheAffectedChronologicalTail()
    {
        var serviceSource = ReadApiSource("Application/Funds/FundService.cs");
        var incomeAssignmentSource = ReadApiSource("Application/Funds/IncomeFundAssignmentService.cs");
        var expenseDisbursementSource = ReadApiSource("Application/Funds/ExpenseFundDisbursementService.cs");
        var repositorySource = ReadApiSource("Infrastructure/Data/EfFundRepository.cs");

        Assert.DoesNotContain("GetOperationsOrderedAsync", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOperationsOrderedAsync", incomeAssignmentSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOperationsOrderedAsync", expenseDisbursementSource, StringComparison.Ordinal);
        Assert.Contains("GetOperationsFromAsync", serviceSource, StringComparison.Ordinal);
        Assert.Contains("GetOperationsSinceAsync", incomeAssignmentSource, StringComparison.Ordinal);
        Assert.Contains("GetOperationsSinceAsync", expenseDisbursementSource, StringComparison.Ordinal);
        Assert.True(CountOccurrences(repositorySource, "operation.CreatedAtUtc >= createdAtUtc") >= 2);
        Assert.Contains(".OrderBy(operation => operation.CreatedAtUtc)", repositorySource, StringComparison.Ordinal);
        Assert.Contains("operations[0].BalanceBefore", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentAllocationLedger_LoadsOnlyExactGarageAndIncomeTypePairs()
    {
        var source = ReadApiSource("Infrastructure/Data/EfAccrualPaymentAllocationRepository.cs");

        Assert.Contains("BuildLedgerQuery(distinctKeys)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Where(BuildExactKeyPredicate<") >= 3);
        Assert.Contains("keys.GroupBy(key => key.GarageId)", source, StringComparison.Ordinal);
        Assert.Contains("item.OperationKind == FinancialOperationKinds.Income", source, StringComparison.Ordinal);
        Assert.Contains("!item.IsCanceled", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FinancialOperationRepository_UsesDatabaseCountOffsetAndLimitWithScopedSqliteFallback()
    {
        var source = ReadApiSource("Infrastructure/Data/EfFinancialOperationRepository.cs");

        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 4);
        Assert.Contains("normalizedSearch is not null && IsSqliteProvider()", source, StringComparison.Ordinal);
        Assert.Contains("OperationMatchesSearch", source, StringComparison.Ordinal);
        Assert.Contains("garageId.HasValue", source, StringComparison.Ordinal);
        Assert.Contains("supplierId.HasValue", source, StringComparison.Ordinal);
        Assert.Contains("staffMemberId.HasValue", source, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(operation => operation.DocumentNumber)", source, StringComparison.Ordinal);
        Assert.Contains("FindForUpdateAsync", source, StringComparison.Ordinal);
        Assert.Contains("Aggregate(dbContext.FinancialOperations)", source, StringComparison.Ordinal);
        Assert.Contains("ActiveDocumentDuplicateExistsAsync", source, StringComparison.Ordinal);
        Assert.Contains("AnyAsync(operation =>", source, StringComparison.Ordinal);
        Assert.Contains("GetIncomeTotalBeforeMonthAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetIncomeMonthlyBucketsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetIncomeTypeBucketsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetWorksheetDataAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSummaryAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetOpeningDebtPaymentTotalAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetBankExpenseTotalAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetCashBalanceDataAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetStaffExpenseTotalAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetPreviousGarageIncomeTotalAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetPreviousSupplierExpenseTotalAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".GroupBy(operation => operation.AccountingMonth)", source, StringComparison.Ordinal);
        Assert.Contains("GetPostgresPageAsync(query, offset, limit, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<int>(\"SELECT 1 AS \\\"Value\\\"\")", source, StringComparison.Ordinal);
        Assert.Contains("TotalCount = query.Count()", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(totalsRow)", source, StringComparison.Ordinal);
        var postgresPageStart = source.IndexOf("private async Task<FinancialOperationPageData> GetPostgresPageAsync", StringComparison.Ordinal);
        var postgresPageEnd = source.IndexOf("public Task<FinancialOperation?> FindForUpdateAsync", postgresPageStart, StringComparison.Ordinal);
        var postgresPageMethod = source[postgresPageStart..postgresPageEnd];
        Assert.Equal(1, CountOccurrences(postgresPageMethod, ".ToListAsync(cancellationToken)"));
        Assert.Contains(".SumAsync(operation => operation.Amount", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FinancialOperationDisplayQuery_BatchesDebtAndAllocationDataBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfFinancialOperationDisplayQuery.cs");

        Assert.Contains("class EfFinancialOperationDisplayQuery", source, StringComparison.Ordinal);
        Assert.Contains("operationIds.Contains(operation.Id)", source, StringComparison.Ordinal);
        Assert.Contains("previous.OperationDate < operation.OperationDate", source, StringComparison.Ordinal);
        Assert.Contains("visibleOperations.Any(operation => operation.GarageId == accrual.GarageId)", source, StringComparison.Ordinal);
        Assert.Contains("visibleOperations.Any(operation => operation.SupplierId == accrual.SupplierId)", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(garageBucketRows)", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(supplierBucketRows)", source, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(source, ".ToListAsync(cancellationToken)"));
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
    public void GarageRepository_UsesProductionPaginationAndDatabaseBalanceAggregatesWithScopedSqliteFallback()
    {
        var source = ReadApiSource("Infrastructure/Data/EfGarageRepository.cs");
        Assert.Contains("IsSqliteProvider()", source, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPageSorting(query, sortBy, sortDescending)", source, StringComparison.Ordinal);
        Assert.Contains("sortBy == \"overdueDebt\"", source, StringComparison.Ordinal);
        Assert.Contains("dbContext.Accruals", source, StringComparison.Ordinal);
        Assert.Contains("dbContext.FinancialOperations", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 4);
        Assert.True(CountOccurrences(source, ".GroupBy(") >= 2);
        Assert.Contains("GetActiveIdsAsync", source, StringComparison.Ordinal);
        Assert.Contains(".Select(garage => garage.Id)", source, StringComparison.Ordinal);
        var balanceMethod = source[
            source.IndexOf("public async Task<GarageBalanceTotalsData> GetBalanceTotalsAsync", StringComparison.Ordinal)..source.IndexOf("public Task<Garage?> FindActiveWithOwnerAsync", StringComparison.Ordinal)];
        Assert.Contains("accrualQuery", balanceMethod, StringComparison.Ordinal);
        Assert.Contains(".Concat(incomeQuery)", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("allocationQuery", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("OverdueAccrualAmount = group.Sum", balanceMethod, StringComparison.Ordinal);
        Assert.Contains("OverdueAllocatedAmount = allocationGroup.Sum", balanceMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("overdueAccrualQuery", balanceMethod, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(balanceMethod, ".Concat("));
        Assert.Equal(1, CountOccurrences(balanceMethod, ".ToListAsync(cancellationToken)"));
    }

    [Fact]
    public void MissingMeterReadingQuery_BoundsAntiQueriesAndCandidateMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfMissingMeterReadingQuery.cs");

        Assert.Contains("dbContext.Database.IsNpgsql()", source, StringComparison.Ordinal);
        Assert.Contains("GetPostgreSqlCandidatesAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetMissingServiceMetersPostgreSqlAsync", source, StringComparison.Ordinal);
        Assert.Contains("unnest(@meter_kinds::text[]) WITH ORDINALITY", source, StringComparison.Ordinal);
        Assert.Contains("requested.\"MeterKind\"", source, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS (", source, StringComparison.Ordinal);
        Assert.Contains("SqlQuery<MissingMeterCandidateRow>", source, StringComparison.Ordinal);
        Assert.Contains("COUNT(*) FILTER", source, StringComparison.Ordinal);
        Assert.Contains("FROM meter_readings AS reading", source, StringComparison.Ordinal);
        Assert.Contains("GROUP BY reading.\"GarageId\"", source, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN (", source, StringComparison.Ordinal);
        Assert.Contains("LIMIT {{limit}}", source, StringComparison.Ordinal);
        Assert.Contains("!dbContext.MeterReadings.Any", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 2);
        Assert.True(CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 2);
        Assert.Contains(".Where(garage => !garage.IsArchived)", source, StringComparison.Ordinal);
        Assert.Contains("CandidateMatchesSearch", source, StringComparison.Ordinal);
        Assert.Contains("HasWaterReading", source, StringComparison.Ordinal);
        Assert.Contains("HasElectricityReading", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var meterKind in meterKinds)", source, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(source, "foreach (var serviceMeterKind in serviceMeterKinds)"));
    }

    [Fact]
    public void MeterReadingRepository_UsesDatabaseCountOffsetAndLimitWithScopedSqliteFallback()
    {
        var source = ReadApiSource("Infrastructure/Data/EfMeterReadingRepository.cs");

        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 4);
        Assert.Contains("normalizedSearch is not null && IsSqliteProvider()", source, StringComparison.Ordinal);
        Assert.Contains("ReadingMatchesSearch", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetForGaragePeriodAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetActiveByGarageIdsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ToDictionaryAsync(reading => reading.GarageId", source, StringComparison.Ordinal);
        Assert.Contains("ActiveDuplicateExistsAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetPreviousActiveAsync", source, StringComparison.Ordinal);
        Assert.Contains(".OrderByDescending(reading => reading.AccountingMonth)", source, StringComparison.Ordinal);
        Assert.Contains("GetNextActiveAsync", source, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(reading => reading.AccountingMonth)", source, StringComparison.Ordinal);
        Assert.Contains("GetActiveAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetPostgresPageAsync(query, offset, limit, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<int>(\"SELECT 1 AS \\\"Value\\\"\")", source, StringComparison.Ordinal);
        Assert.Contains("TotalCount = query.Count()", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(totalsRow)", source, StringComparison.Ordinal);
        var postgresPageStart = source.IndexOf("private async Task<MeterReadingPageData> GetPostgresPageAsync", StringComparison.Ordinal);
        var postgresPageEnd = source.IndexOf("public async Task<MeterReadingYearPageData> GetYearPageAsync", postgresPageStart, StringComparison.Ordinal);
        var postgresPageMethod = source[postgresPageStart..postgresPageEnd];
        Assert.Equal(1, CountOccurrences(postgresPageMethod, ".ToListAsync(cancellationToken)"));
    }

    [Fact]
    public void MeterReadingYearPage_ProjectsOnlyVisibleGaragesAndCompactValuesBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfMeterReadingRepository.cs");

        Assert.Contains("GetYearPageAsync", source, StringComparison.Ordinal);
        Assert.Contains(".Where(garage => !garage.IsArchived)", source, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(garage => garage.Number.Length)", source, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(garage => garage.Number)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", source, StringComparison.Ordinal);
        Assert.Contains("new MeterReadingYearGarageData(garage.Id, garage.Number)", source, StringComparison.Ordinal);
        Assert.Contains("new MeterReadingYearValueData(", source, StringComparison.Ordinal);
        Assert.Contains("garageIds.Contains(reading.GarageId)", source, StringComparison.Ordinal);
        Assert.Contains("GetPostgresYearPageAsync(year, meterKind, offset, limit, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("SqlQuery<MeterReadingYearPageRow>", source, StringComparison.Ordinal);
        Assert.Contains("COUNT(*) OVER () AS \"TotalCount\"", source, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN meter_readings AS reading", source, StringComparison.Ordinal);
        Assert.Contains("WHERE NOT EXISTS (SELECT 1 FROM paged_garages)", source, StringComparison.Ordinal);
        var postgresYearPageStart = source.IndexOf("private async Task<MeterReadingYearPageData> GetPostgresYearPageAsync", StringComparison.Ordinal);
        var postgresYearPageEnd = source.IndexOf("private sealed class MeterReadingYearPageRow", postgresYearPageStart, StringComparison.Ordinal);
        var postgresYearPageMethod = source[postgresYearPageStart..postgresYearPageEnd];
        Assert.Equal(1, CountOccurrences(postgresYearPageMethod, ".ToListAsync(cancellationToken)"));
    }

    [Fact]
    public void AccrualRepository_UsesDatabaseCountOffsetAndLimitWithScopedSqliteFallback()
    {
        var source = ReadApiSource("Infrastructure/Data/EfAccrualRepository.cs");

        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 4);
        Assert.Contains("normalizedSearch is not null && IsSqliteProvider()", source, StringComparison.Ordinal);
        Assert.Contains("AccrualMatchesSearch", source, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(accrual => accrual.Garage.Number)", source, StringComparison.Ordinal);
        Assert.Contains("GetTotalBeforeMonthAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetTotalThroughMonthAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetActiveGarageIdsAsync", source, StringComparison.Ordinal);
        Assert.Contains("CountActiveAnnualRegularForGenerationAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetActiveAnnualRegularGarageIdsAsync", source, StringComparison.Ordinal);
        Assert.Contains(".Distinct()", source, StringComparison.Ordinal);
        Assert.Contains("ToHashSetAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".SumAsync(accrual => accrual.Amount", source, StringComparison.Ordinal);
        Assert.Contains("GetMonthlyBucketsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetIncomeTypeBucketsAsync", source, StringComparison.Ordinal);
        Assert.Contains(".GroupBy(accrual => accrual.AccountingMonth)", source, StringComparison.Ordinal);
        Assert.Contains("GetPostgresPageAsync(query, offset, limit, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<int>(\"SELECT 1 AS \\\"Value\\\"\")", source, StringComparison.Ordinal);
        Assert.Contains("TotalCount = query.Count()", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(totalsRow)", source, StringComparison.Ordinal);
        var postgresPageStart = source.IndexOf("private async Task<AccrualPageData> GetPostgresPageAsync", StringComparison.Ordinal);
        var postgresPageEnd = source.IndexOf("public async Task<AccrualPageData> GetDueDateReviewPageAsync", postgresPageStart, StringComparison.Ordinal);
        var postgresPageMethod = source[postgresPageStart..postgresPageEnd];
        Assert.Equal(1, CountOccurrences(postgresPageMethod, ".ToListAsync(cancellationToken)"));
    }

    [Fact]
    public void SupplierAccrualRepository_UsesDatabaseCountOffsetAndLimitWithScopedSqliteFallback()
    {
        var source = ReadApiSource("Infrastructure/Data/EfSupplierAccrualRepository.cs");

        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 4);
        Assert.Contains("supplierId.HasValue", source, StringComparison.Ordinal);
        Assert.Contains("normalizedSearch is not null && IsSqliteProvider()", source, StringComparison.Ordinal);
        Assert.Contains("AccrualMatchesSearch", source, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(accrual => accrual.Supplier.Name)", source, StringComparison.Ordinal);
        Assert.Contains("GetTotalThroughMonthAsync", source, StringComparison.Ordinal);
        Assert.Contains(".SumAsync(accrual => accrual.Amount", source, StringComparison.Ordinal);
        Assert.Contains("GetActiveSupplierIdsAsync", source, StringComparison.Ordinal);
        Assert.Contains(".Select(accrual => accrual.SupplierId)", source, StringComparison.Ordinal);
        Assert.Contains(".ToHashSetAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("GetMonthlyBucketsThroughMonthAsync", source, StringComparison.Ordinal);
        Assert.Contains(".GroupBy(accrual => accrual.AccountingMonth)", source, StringComparison.Ordinal);
        Assert.Contains("GetPostgresPageAsync(query, offset, limit, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<int>(\"SELECT 1 AS \\\"Value\\\"\")", source, StringComparison.Ordinal);
        Assert.Contains("TotalCount = query.Count()", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(totalsRow)", source, StringComparison.Ordinal);
        var postgresPageStart = source.IndexOf("private async Task<SupplierAccrualPageData> GetPostgresPageAsync", StringComparison.Ordinal);
        var postgresPageEnd = source.IndexOf("public async Task<int> CountActiveAsync", postgresPageStart, StringComparison.Ordinal);
        var postgresPageMethod = source[postgresPageStart..postgresPageEnd];
        Assert.Equal(1, CountOccurrences(postgresPageMethod, ".ToListAsync(cancellationToken)"));
    }

    [Fact]
    public void SupplierGroupRepository_UsesProductionCountOffsetAndLimitWithScopedSqliteFallback()
    {
        var source = ReadApiSource("Infrastructure/Data/EfSupplierGroupRepository.cs");

        Assert.Contains("IsSqliteProvider()", source, StringComparison.Ordinal);
        Assert.Contains("dbContext.Database.IsNpgsql()", source, StringComparison.Ordinal);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(source, "EF.Functions.ILike("));
        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 3);
    }

    [Fact]
    public void MeasurementUnitRepository_UsesIndexedPostgreSqlSearchAndDatabasePaging()
    {
        var source = ReadApiSource("Infrastructure/Data/EfMeasurementUnitRepository.cs");

        Assert.Contains("dbContext.Database.IsNpgsql()", source, StringComparison.Ordinal);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, "EF.Functions.ILike(") >= 5);
        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 2);

        var migration = ReadApiSource(
            "Infrastructure/Data/Migrations/20260829123636_OptimizeSmallDictionarySearch.cs");
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pg_trgm", migration, StringComparison.Ordinal);
        Assert.Contains("IX_measurement_units_Name_trgm", migration, StringComparison.Ordinal);
        Assert.Contains("gin_trgm_ops", migration, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierRepository_UsesDatabaseCountOffsetAndLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfSupplierRepository.cs");
        var serviceSource = ReadApiSource("Application/Dictionaries/DictionaryService.cs");

        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("IsSqliteProvider() && sortBy is \"debt\" or \"contactPerson\" or \"phone\" or \"email\"", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPageSorting(query, sortBy, sortDescending)", source, StringComparison.Ordinal);
        Assert.Contains("supplierIds.Contains(contact.SupplierId) && !contact.IsArchived", source, StringComparison.Ordinal);
        Assert.Contains(".GroupBy(contact => contact.SupplierId)", source, StringComparison.Ordinal);
        Assert.Contains("contact.Status == \"Работает\"", source, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(contact => contact.Id)", source, StringComparison.Ordinal);
        Assert.Contains(".First())", source, StringComparison.Ordinal);
        Assert.Contains("SupplierPrimaryContactData", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.Contains(".Select(supplier => new SupplierPageDebtRow(", source, StringComparison.Ordinal);
        Assert.Contains("pageRows.ToDictionary(row => row.Supplier.Id, row => row.DebtTotal)", source, StringComparison.Ordinal);
        var supplierPageMethod = serviceSource[
            serviceSource.IndexOf("public async Task<PagedResult<SupplierDto>> GetSuppliersPageAsync", StringComparison.Ordinal)..serviceSource.IndexOf("public async Task<DictionaryResult<SupplierDto>> CreateSupplierAsync", StringComparison.Ordinal)];
        Assert.DoesNotContain("GetDebtTotalsAsync", supplierPageMethod, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 2);
        Assert.True(CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 2);
        Assert.Matches(
            BoundedQueryRegex(@"GetActiveByGroupAsync[\s\S]*?Where\(supplier => !supplier\.IsArchived && supplier\.GroupId == groupId\)[\s\S]*?OrderBy\(supplier => supplier\.Name\)[\s\S]*?ToListAsync\(cancellationToken\)"),
            source);
        Assert.Contains(".Select(supplier => supplier.StartingBalance)", source, StringComparison.Ordinal);
        Assert.Contains(".SingleAsync(cancellationToken)", source, StringComparison.Ordinal);
        var debtMethod = source[
            source.IndexOf("public async Task<IReadOnlyDictionary<Guid, decimal>> GetDebtTotalsAsync", StringComparison.Ordinal)..source.IndexOf("public Task<bool> ActiveDuplicateExistsAsync", StringComparison.Ordinal)];
        Assert.Contains("startingBalanceQuery", debtMethod, StringComparison.Ordinal);
        Assert.Contains("accrualQuery", debtMethod, StringComparison.Ordinal);
        Assert.Contains("paymentQuery", debtMethod, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(debtMethod, ".Concat("));
        Assert.Equal(1, CountOccurrences(debtMethod, ".ToListAsync(cancellationToken)"));
    }

    [Fact]
    public void PerformanceOptimizationRelease_AccumulatesVerifiedQueryImprovements()
    {
        var releaseNotes = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "GarageBalance.Api",
            "AppReleases",
            "releases.json"));

        Assert.Contains("\"version\": \"0.760.0\"", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Docker-установка стала полностью автономной", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("История баланса гаража теперь одновременно считает входящий долг", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Разделы системы загружаются быстрее и стабильнее", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("только один приоритетный контакт для каждой видимой строки", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Сводка фондов считает поступления и выплаты за один проход", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Проверка отсутствующих показаний воды и электричества теперь один раз", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Балансовые показатели гаражей теперь рассчитываются за один проход", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Отчет по взносам теперь за одно обращение к базе", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Сводный месячный отчет теперь за одно обращение к базе", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Таблица гаражей в сводном отчете теперь за одно обращение к базе", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Отчет «Оплаты из кассы» теперь за одно обращение к базе", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Отчет «Сдача кассы в банк» теперь за одно обращение к базе", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Отчет «Изменения фондов» теперь за одно обращение к базе", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Отчет по поступлениям больше не перечитывает видимые платежи", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Режим «Платежи» отчета по поступлениям теперь одновременно получает полные итоги", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Режим «Платежи» отчета по выплатам теперь одновременно получает полные итоги", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Режим «Начисления» отчета по поступлениям теперь одновременно получает итоговую сумму", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Журнал начислений теперь за одно обращение к базе получает количество найденных записей", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Журнал начислений поставщикам теперь за одно обращение к базе получает количество найденных записей", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Журнал поступлений и выплат теперь за одно обращение к базе получает количество найденных операций", releaseNotes, StringComparison.Ordinal);
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
        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("sortBy == \"rate\" && IsSqliteProvider()", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPageSorting(query, sortBy, sortDescending)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", source, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync(cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpenseWorksheetQuery_AggregatesAllSourcesBeforeSingleMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfExpenseWorksheetQuery.cs");
        Assert.Contains("class EfExpenseWorksheetQuery", source, StringComparison.Ordinal);
        Assert.Contains(".Where(member => !member.IsArchived)", source, StringComparison.Ordinal);
        Assert.Contains("availableBalance", source, StringComparison.Ordinal);
        Assert.Contains("bankDeposits", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".GroupBy(") >= 4);
        Assert.True(CountOccurrences(source, ".Concat(") >= 6);
        Assert.Equal(1, CountOccurrences(source, ".ToListAsync(cancellationToken)"));
    }

    [Fact]
    public void IncomeTypeRepository_UsesProviderAwareSearchAndDatabasePaging()
    {
        var source = ReadApiSource("Infrastructure/Data/EfIncomeTypeRepository.cs");
        Assert.Contains("IsSqliteProvider()", source, StringComparison.Ordinal);
        Assert.Contains("dbContext.Database.IsNpgsql()", source, StringComparison.Ordinal);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, "EF.Functions.ILike("));
        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 3);
        Assert.True(CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 4);

        var migration = ReadApiSource(
            "Infrastructure/Data/Migrations/20260829121728_OptimizeIncomeTypeSearch.cs");
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pg_trgm", migration, StringComparison.Ordinal);
        Assert.Contains("IX_income_types_Code_trgm", migration, StringComparison.Ordinal);
        Assert.Contains("gin_trgm_ops", migration, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpenseTypeRepository_UsesProviderAwareSearchAndDatabasePaging()
    {
        var source = ReadApiSource("Infrastructure/Data/EfExpenseTypeRepository.cs");
        Assert.Contains("IsSqliteProvider()", source, StringComparison.Ordinal);
        Assert.Contains("dbContext.Database.IsNpgsql()", source, StringComparison.Ordinal);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, "EF.Functions.ILike("));
        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 3);
        Assert.True(CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 4);

        var migration = ReadApiSource(
            "Infrastructure/Data/Migrations/20260829115326_OptimizeExpenseTypeSearch.cs");
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pg_trgm", migration, StringComparison.Ordinal);
        Assert.Contains("IX_expense_types_Code_trgm", migration, StringComparison.Ordinal);
        Assert.Contains("gin_trgm_ops", migration, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void TariffRepository_UsesDatabaseCountOffsetAndLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfTariffRepository.cs");
        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 2);
        Assert.True(CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 2);
        Assert.Contains("MinAsync", source, StringComparison.Ordinal);
        Assert.Contains("dbContext.Database.IsNpgsql()", source, StringComparison.Ordinal);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, "EF.Functions.ILike("));
    }

    [Fact]
    public void IrregularPaymentRepository_UsesDatabaseLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfIrregularPaymentRepository.cs");
        Assert.Contains(".Take(limit)", source, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("accrual.IrregularPaymentId == id", source, StringComparison.Ordinal);
        Assert.Contains("join accrual in dbContext.Accruals.AsNoTracking() on payment.Id equals accrual.IrregularPaymentId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("incomeType.Name == name", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".AnyAsync(") >= 2);
    }

    [Fact]
    public void ChargeServiceSettingRepository_UsesDatabaseLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfChargeServiceSettingRepository.cs");
        var financeSource = ReadApiSource("Application/Finance/FinanceService.cs");
        Assert.Contains(".Take(limit)", source, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(
            "GetActiveRegularMeteredCoreAsync(calculationBase, accountingMonth, limit, cancellationToken)",
            source,
            StringComparison.Ordinal);
        Assert.Matches(
            BoundedQueryRegex(@"GetListAsync[\s\S]*?Include\(item => item\.TariffVersions\.Where[\s\S]*?version\.EffectiveFrom <= businessDate[\s\S]*?Take\(limit\)[\s\S]*?HasTariffVersions[\s\S]*?ApplyTariffsForMonthAsync\(settings, businessDate, cancellationToken, servicesWithVersions\)"),
            source);
        Assert.Matches(
            BoundedQueryRegex(@"GetActiveRegularAsync[\s\S]*?Include\(setting => setting\.TariffVersions\.Where[\s\S]*?version\.EffectiveFrom <= monthEnd[\s\S]*?HasTariffVersions[\s\S]*?ApplyTariffsForMonthAsync\(settings, accountingMonth, cancellationToken, servicesWithVersions\)"),
            source);
        Assert.Matches(
            BoundedQueryRegex(@"GetActiveRegularMeteredCoreAsync[\s\S]*?Include\(setting => setting\.TariffVersions\.Where[\s\S]*?version\.EffectiveFrom <= monthEnd[\s\S]*?HasTariffVersions[\s\S]*?ApplyTariffsForMonthAsync\(settings, accountingMonth, cancellationToken, servicesWithVersions\)"),
            source);
        Assert.DoesNotContain(
            "(await GetActiveRegularMeteredCoreAsync(accountingMonth, limit, cancellationToken))",
            source,
            StringComparison.Ordinal);
        Assert.Matches(
            BoundedQueryRegex(@"setting\.Tariff\.CalculationBase == calculationBase[\s\S]*?OrderBy\(setting => setting\.Name\)[\s\S]*?Take\(limit\)"),
            source);
        Assert.Contains("MeterKinds.Water => TariffCalculationBases.MeterWater", financeSource, StringComparison.Ordinal);
        Assert.Contains("MeterKinds.Electricity => TariffCalculationBases.MeterElectricity", financeSource, StringComparison.Ordinal);
        Assert.Contains(
            "GetActiveRegularMeteredAsync(\n                calculationBase,",
            financeSource,
            StringComparison.Ordinal);
        Assert.Matches(
            BoundedQueryRegex(@"GetActiveRegularAsync[\s\S]*?Where\(setting => !setting\.IsArchived && setting\.IsRegular\)[\s\S]*?OrderBy\(setting => setting\.Name\)[\s\S]*?ToListAsync\(cancellationToken\)"),
            source);
        Assert.Matches(
            BoundedQueryRegex(@"GetActiveRegularForDueDatesAsync[\s\S]*?Include\(setting => setting\.TariffVersions\.Where[\s\S]*?version\.EffectiveFrom <= monthEnd[\s\S]*?version\.EffectiveTo\.Value >= month[\s\S]*?IncomeTypeId == incomeTypeId[\s\S]*?Take\(2\)[\s\S]*?ToListAsync\(cancellationToken\)"),
            source);
        Assert.Contains(
            "settings\n            .SelectMany(setting => setting.TariffVersions)",
            source,
            StringComparison.Ordinal);
        Assert.Matches(
            BoundedQueryRegex(@"SetTariffVersionAsync[\s\S]*?OrderByDescending\(item => item\.EffectiveFrom\)[\s\S]*?Take\(1\)[\s\S]*?EffectiveFrom == effectiveFrom[\s\S]*?Take\(1\)[\s\S]*?Concat\(activeVersions[\s\S]*?OrderBy\(item => item\.EffectiveFrom\)[\s\S]*?Take\(1\)[\s\S]*?ToListAsync\(cancellationToken\)"),
            source);
        Assert.DoesNotContain(
            "var existing = await dbContext.ChargeServiceTariffVersions.SingleOrDefaultAsync",
            source,
            StringComparison.Ordinal);
        Assert.Matches(
            BoundedQueryRegex(@"GetActiveTariffScheduleAsync[\s\S]*?Where\(item => item\.Id == serviceId && !item\.IsArchived\)[\s\S]*?Include\(item => item\.TariffVersions\.Where\(version => !version\.IsArchived\)\)[\s\S]*?ThenInclude\(version => version\.Tariff\)[\s\S]*?SingleOrDefaultAsync\(cancellationToken\)"),
            source);

        var dictionaryService = ReadApiSource("Application/Dictionaries/DictionaryService.cs");
        Assert.Matches(
            BoundedQueryRegex(@"GetChargeServiceTariffScheduleAsync[\s\S]*?GetActiveTariffScheduleAsync\(id, cancellationToken\)[\s\S]*?schedule\.Periods"),
            dictionaryService);
        Assert.DoesNotMatch(
            BoundedQueryRegex(@"GetChargeServiceTariffScheduleAsync[\s\S]*?FindActiveAsync\(id, cancellationToken\)[\s\S]*?GetTariffPeriodsAsync\(id, false"),
            dictionaryService);
    }

    [Fact]
    public void FeeCampaignRepository_UsesDatabaseLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfFeeCampaignRepository.cs");
        Assert.Contains(".Take(limit)", source, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("dbContext.Database.IsNpgsql()", source, StringComparison.Ordinal);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, "EF.Functions.ILike("));
    }

    [Fact]
    public void FinancePageQueries_UseCountSkipAndTakeBeforeMaterialization()
    {
        var source = ReadApiSource("Application/Finance/FinanceService.cs");
        var financialOperationRepositorySource = ReadApiSource("Infrastructure/Data/EfFinancialOperationRepository.cs");
        var meterReadingRepositorySource = ReadApiSource("Infrastructure/Data/EfMeterReadingRepository.cs");
        var accrualRepositorySource = ReadApiSource("Infrastructure/Data/EfAccrualRepository.cs");
        var supplierAccrualRepositorySource = ReadApiSource("Infrastructure/Data/EfSupplierAccrualRepository.cs");

        Assert.Contains("financialOperationRepository.GetPageAsync", source, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", financialOperationRepositorySource, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", meterReadingRepositorySource, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", accrualRepositorySource, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", supplierAccrualRepositorySource, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", financialOperationRepositorySource, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", meterReadingRepositorySource, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", accrualRepositorySource, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", supplierAccrualRepositorySource, StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", financialOperationRepositorySource, StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", meterReadingRepositorySource, StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", accrualRepositorySource, StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", supplierAccrualRepositorySource, StringComparison.Ordinal);
        Assert.True(CountOccurrences(financialOperationRepositorySource, ".ToListAsync(cancellationToken)") >= 4);
        Assert.True(CountOccurrences(meterReadingRepositorySource, ".ToListAsync(cancellationToken)") >= 4);
        Assert.True(CountOccurrences(accrualRepositorySource, ".ToListAsync(cancellationToken)") >= 4);
        Assert.True(CountOccurrences(supplierAccrualRepositorySource, ".ToListAsync(cancellationToken)") >= 4);
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
        Assert.Contains("monthlyData.IncomeByMonth.ToDictionary", source, StringComparison.Ordinal);
        Assert.Contains("monthlyData.ExpenseByMonth.ToDictionary", source, StringComparison.Ordinal);
        Assert.DoesNotContain("monthlyData.IncomeByMonth.SingleOrDefault", source, StringComparison.Ordinal);
        Assert.Contains("GetRowsWithoutSearchAsync", garageSource, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(incomeSource, "ApplyLimit(") >= 3 &&
            incomeSource.Contains("ApplyPage(", StringComparison.Ordinal) &&
            incomeSource.Contains("GetFetchLimit(offset, limit)", StringComparison.Ordinal) &&
            CountOccurrences(expenseSource, "ApplyLimit(") >= 3 &&
            expenseSource.Contains("ApplyPage(", StringComparison.Ordinal) &&
            expenseSource.Contains("GetFetchLimit(offset, limit)", StringComparison.Ordinal),
            "Remaining report visible rows must be bounded before materialization for income, expense, accrual and starting-balance segments.");
        Assert.True(
            incomeSource.Contains("query.Take(limit.Value)", StringComparison.Ordinal) &&
            expenseSource.Contains("query.Take(limit.Value)", StringComparison.Ordinal),
            "Report visible-row queries must use the normalized server-side limit before ToListAsync.");
        Assert.True(
            CountOccurrences(incomeSource, "Count = group.Count()") >= 3 &&
            CountOccurrences(expenseSource, "group.Count()") >= 3,
            "Report totals must keep total row counts in the combined database aggregate without materializing every visible-row candidate.");
        Assert.True(
            CountOccurrences(incomeSource, "Total = group.Sum(") >= 3 &&
            CountOccurrences(expenseSource, "group.Sum(") >= 3,
            "Report totals and counts must be aggregated together in the database instead of being derived from materialized rows or separate round trips.");
        Assert.True(CountOccurrences(expenseSource, "aggregateQuery = aggregateQuery.Concat(") >= 3);
        Assert.True(CountOccurrences(expenseSource, "aggregateQuery.ToListAsync(cancellationToken)") >= 1);
        Assert.Contains("StartingBalanceTotalCategory", expenseSource, StringComparison.Ordinal);
        Assert.Contains("AccrualTotalCategory", expenseSource, StringComparison.Ordinal);
        Assert.Contains("ExpenseTotalCategory", expenseSource, StringComparison.Ordinal);
        var expenseAllMethod = expenseSource[
            expenseSource.IndexOf("private async Task<ExpenseReportQueryData> GetPostgresAllRowsAsync", StringComparison.Ordinal)..expenseSource.IndexOf("private async Task<ExpenseReportQueryData> GetPostgresAccrualRowsAsync", StringComparison.Ordinal)];
        Assert.Contains("WITH source_rows AS", expenseAllMethod, StringComparison.Ordinal);
        Assert.Contains("GROUP BY accounting_month, counterparty_id, expense_type_id, counterparty_kind", expenseAllMethod, StringComparison.Ordinal);
        Assert.Contains("COALESCE(SUM(accrual_amount), 0)", expenseAllMethod, StringComparison.Ordinal);
        Assert.Contains("COALESCE(SUM(expense_amount), 0)", expenseAllMethod, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<ExpenseAllCombinedQueryRow>", expenseAllMethod, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(expenseAllMethod, ".ToListAsync(cancellationToken)"));
        var expenseAccrualMethod = expenseSource[
            expenseSource.IndexOf("private async Task<ExpenseReportQueryData> GetPostgresAccrualRowsAsync", StringComparison.Ordinal)..expenseSource.IndexOf("private async Task<ExpenseReportQueryData> GetPostgresPaymentRowsAsync", StringComparison.Ordinal)];
        Assert.Contains("WITH filtered_rows AS", expenseAccrualMethod, StringComparison.Ordinal);
        Assert.Contains("COALESCE(SUM(accrual_amount), 0)", expenseAccrualMethod, StringComparison.Ordinal);
        Assert.Contains("generate_series", expenseAccrualMethod, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<ExpenseAccrualCombinedQueryRow>", expenseAccrualMethod, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(expenseAccrualMethod, ".ToListAsync(cancellationToken)"));
        var expensePaymentMethod = expenseSource[
            expenseSource.IndexOf("private async Task<ExpenseReportQueryData> GetPostgresPaymentRowsAsync", StringComparison.Ordinal)..expenseSource.IndexOf("private static IOrderedQueryable<ExpenseReportSortableProjection> ApplyPostgresSort", StringComparison.Ordinal)];
        Assert.Contains("WITH filtered_rows AS", expensePaymentMethod, StringComparison.Ordinal);
        Assert.Contains("COALESCE(SUM(expense_amount), 0)", expensePaymentMethod, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<ExpensePaymentCombinedQueryRow>", expensePaymentMethod, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(expensePaymentMethod, ".ToListAsync(cancellationToken)"));
        Assert.Contains("StartingBalanceTotalCategory", incomeSource, StringComparison.Ordinal);
        Assert.Contains("AccrualTotalCategory", incomeSource, StringComparison.Ordinal);
        Assert.Contains("IncomeTotalCategory", incomeSource, StringComparison.Ordinal);
        var incomeAllMethod = incomeSource[
            incomeSource.IndexOf("private async Task<IncomeReportQueryData> GetPostgresAllRowsAsync", StringComparison.Ordinal)..incomeSource.IndexOf("private async Task<IncomeReportQueryData> GetPostgresAccrualRowsAsync", StringComparison.Ordinal)];
        Assert.Contains("WITH filtered_rows AS", incomeAllMethod, StringComparison.Ordinal);
        Assert.Contains("COALESCE(SUM(accrual_amount), 0)", incomeAllMethod, StringComparison.Ordinal);
        Assert.Contains("COALESCE(SUM(income_amount), 0)", incomeAllMethod, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<IncomeAllCombinedQueryRow>", incomeAllMethod, StringComparison.Ordinal);
        Assert.Contains("visiblePaymentTargets = pageRows", incomeAllMethod, StringComparison.Ordinal);
        Assert.Contains("new IncomeDebtTarget(", incomeAllMethod, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(incomeAllMethod, ".ToListAsync(cancellationToken)"));
        Assert.DoesNotContain("paymentIds.Contains(operation.Id)", incomeAllMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("visiblePayments", incomeAllMethod, StringComparison.Ordinal);
        var incomeAccrualMethod = incomeSource[
            incomeSource.IndexOf("private async Task<IncomeReportQueryData> GetPostgresAccrualRowsAsync", StringComparison.Ordinal)..incomeSource.IndexOf("private async Task<IncomeReportQueryData> GetPostgresPaymentRowsAsync", StringComparison.Ordinal)];
        Assert.Contains("WITH filtered_rows AS", incomeAccrualMethod, StringComparison.Ordinal);
        Assert.Contains("COALESCE(SUM(accrual_amount), 0)", incomeAccrualMethod, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<IncomeAccrualCombinedQueryRow>", incomeAccrualMethod, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(incomeAccrualMethod, ".ToListAsync(cancellationToken)"));
        var incomePaymentMethod = incomeSource[
            incomeSource.IndexOf("private async Task<IncomeReportQueryData> GetPostgresPaymentRowsAsync", StringComparison.Ordinal)..incomeSource.IndexOf("private static IOrderedQueryable<IncomeReportSortableProjection> ApplyPostgresSort", StringComparison.Ordinal)];
        Assert.Contains("WITH filtered_rows AS", incomePaymentMethod, StringComparison.Ordinal);
        Assert.Contains("COALESCE(SUM(income_amount), 0)", incomePaymentMethod, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<IncomePaymentCombinedQueryRow>", incomePaymentMethod, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(incomePaymentMethod, ".ToListAsync(cancellationToken)"));
        var incomeDebtMethod = incomeSource[
            incomeSource.IndexOf("private async Task<IReadOnlyDictionary<Guid, decimal>> CalculateDebtAfterPaymentsAsync", StringComparison.Ordinal)..incomeSource.IndexOf("private static IQueryable<T> ApplyLimit", StringComparison.Ordinal)];
        Assert.Contains("startingBalanceQuery", incomeDebtMethod, StringComparison.Ordinal);
        Assert.Contains("accrualQuery", incomeDebtMethod, StringComparison.Ordinal);
        Assert.Contains("paymentQuery", incomeDebtMethod, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(incomeDebtMethod, ".Concat("));
        Assert.Equal(1, CountOccurrences(incomeDebtMethod, ".ToListAsync(cancellationToken)"));
        Assert.Contains("useClientSearch = hasSearch && !", incomeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("STRPOS", incomeSource, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            CountOccurrences(incomeSource, "ToLower().Contains(normalizedSearch!)") >= 6,
            "PostgreSQL income-report search must be applied to source queries before count, sum and page materialization.");
        Assert.Contains("useClientSearch = hasSearch && !", expenseSource, StringComparison.Ordinal);
        Assert.DoesNotContain("STRPOS", expenseSource, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            CountOccurrences(expenseSource, "ToLower().Contains(normalizedSearch!)") >= 7,
            "PostgreSQL expense-report search must be applied to source queries before count, sum and page materialization.");
        Assert.Matches(
            BoundedQueryRegex(@"ExecuteBoundedRowsAsync[\s\S]*?CountAsync\(cancellationToken\)[\s\S]*?ApplyLimit\(query, limit\)\.ToListAsync\(cancellationToken\)"),
            garageSource);
        Assert.True(
            CountOccurrences(garageSource, ".GroupBy(") >= 3,
            "Search-compatible consolidated garage fallback must aggregate income, accrual and readings by garage.");
        var postgresGarageStart = garageSource.IndexOf("private async Task<ConsolidatedGarageRowsData> GetPostgresRowsAsync", StringComparison.Ordinal);
        var fallbackGarageStart = garageSource.IndexOf("private async Task<ConsolidatedGarageRowsData> GetRowsWithoutSearchAsync", StringComparison.Ordinal);
        var postgresGarageSource = garageSource[postgresGarageStart..fallbackGarageStart];
        Assert.Contains("owner.\"LastName\" ILIKE @search", postgresGarageSource, StringComparison.Ordinal);
        Assert.DoesNotContain("STRPOS", postgresGarageSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("candidate_garages AS MATERIALIZED", postgresGarageSource, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(postgresGarageSource, "INNER JOIN candidate_garages candidate"));
        Assert.Contains("FROM candidate_garages garage", postgresGarageSource, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit", postgresGarageSource, StringComparison.Ordinal);
        Assert.Contains("FROM page_rows", postgresGarageSource, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)::int", postgresGarageSource, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(postgresGarageSource, ".ToListAsync(cancellationToken)"));
        Assert.Contains(".Concat(accrualByGarageQuery)", garageSource, StringComparison.Ordinal);
        Assert.Contains(".Concat(readingsByGarageQuery)", garageSource, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(garageSource, "aggregateRows") >= 4,
            "Consolidated garage search must reuse the single combined aggregate result for income, accrual and reading lookups.");
    }

    [Fact]
    public void GarageReportScreenQuery_AggregatesCountsTotalsAndPageInDatabase()
    {
        var source = ReadApiSource("Infrastructure/Data/EfGarageReportQuery.cs");
        var postgresMethod = source[
            source.IndexOf("private async Task<GarageReportQueryData> GetPostgresRowsAsync", StringComparison.Ordinal)..source.IndexOf("private sealed record GarageReportCombinedQueryRow", StringComparison.Ordinal)];

        Assert.True(CountOccurrences(source, ".Concat(") >= 2, "Garage report sources must remain a SQL UNION ALL pipeline.");
        Assert.True(CountOccurrences(source, ".GroupBy(") >= 2, "Expanded and grouped garage modes must aggregate before paging.");
        Assert.Contains("AccrualTotal = group.Sum(row => row.AccrualAmount)", source, StringComparison.Ordinal);
        Assert.Contains("IncomeTotal = group.Sum(row => row.IncomeAmount)", source, StringComparison.Ordinal);
        Assert.Contains("RowCount = group.Count()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceRows.SumAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("groupedRows.CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("if (summary is null)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.Contains("limit is > 0 ? page.Take(limit.Value) : page", source, StringComparison.Ordinal);
        Assert.Contains("if (IsNpgsql())", source, StringComparison.Ordinal);
        Assert.Contains("matchingGarageIds", source, StringComparison.Ordinal);
        Assert.Contains("WITH filtered_garages AS", postgresMethod, StringComparison.Ordinal);
        Assert.Contains("COALESCE(SUM(accrual_amount), 0)", postgresMethod, StringComparison.Ordinal);
        Assert.Contains("COALESCE(SUM(income_amount), 0)", postgresMethod, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)::int", postgresMethod, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<GarageReportCombinedQueryRow>", postgresMethod, StringComparison.Ordinal);
        Assert.Contains("ILIKE @search", postgresMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("STRPOS", postgresMethod, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(postgresMethod, ".ToListAsync(cancellationToken)"));
        Assert.Equal(1, CountOccurrences(postgresMethod, "FROM financial_operations"));
        Assert.Equal(1, CountOccurrences(postgresMethod, "FROM accruals"));
        Assert.Equal(1, CountOccurrences(postgresMethod, "FROM garages"));
    }

    [Fact]
    public void CashPaymentScreenQuery_UsesOnePostgresCommandForTotalsAndPage()
    {
        var source = ReadApiSource("Infrastructure/Data/EfCashMovementReportQuery.cs");
        var method = source[
            source.IndexOf("private async Task<CashPaymentReportData> GetPostgresCashPaymentsAsync", StringComparison.Ordinal)..source.IndexOf("public async Task<BankDepositReportData> GetBankDepositsAsync", StringComparison.Ordinal)];

        Assert.Equal(1, CountOccurrences(method, "SqlQueryRaw<CashPaymentCombinedQueryRow>"));
        Assert.Equal(1, CountOccurrences(method, ".ToListAsync(cancellationToken)"));
        Assert.Equal(1, CountOccurrences(method, "FROM financial_operations"));
        Assert.Contains("FROM page_rows", method, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)::int", method, StringComparison.Ordinal);
        Assert.Contains("OFFSET @offset", method, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit", method, StringComparison.Ordinal);
        Assert.Contains("COALESCE(supplier.\"Name\", operation.\"CounterpartyName\") ILIKE @search", method, StringComparison.Ordinal);
        Assert.Contains("expense_type.\"Name\" ILIKE @search", method, StringComparison.Ordinal);
    }

    [Fact]
    public void BankDepositScreenQuery_UsesOnePostgresCommandForTotalsAndPage()
    {
        var source = ReadApiSource("Infrastructure/Data/EfCashMovementReportQuery.cs");
        var method = source[
            source.IndexOf("private async Task<BankDepositReportData> GetPostgresBankDepositsAsync", StringComparison.Ordinal)..source.IndexOf("private static IOrderedQueryable<FinancialOperation> ApplyCashPaymentSort", StringComparison.Ordinal)];

        Assert.Equal(1, CountOccurrences(method, "SqlQueryRaw<BankDepositCombinedQueryRow>"));
        Assert.Equal(1, CountOccurrences(method, ".ToListAsync(cancellationToken)"));
        Assert.Equal(1, CountOccurrences(method, "FROM cash_bank_transfers"));
        Assert.Contains("FROM page_rows", method, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)::int", method, StringComparison.Ordinal);
        Assert.Contains("OFFSET @offset", method, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit", method, StringComparison.Ordinal);
        Assert.Contains("transfer.\"Comment\" ILIKE @search", method, StringComparison.Ordinal);
    }

    [Fact]
    public void FundChangeScreenQuery_UsesOnePostgresCommandForTotalsActorsAndPage()
    {
        var source = ReadApiSource("Infrastructure/Data/EfFundChangeReportQuery.cs");
        var method = source[
            source.IndexOf("private async Task<FundChangeReportData> GetPostgresFundChangesAsync", StringComparison.Ordinal)..source.IndexOf("private IQueryable<FundChangeProjectionRow> ProjectRows", StringComparison.Ordinal)];

        Assert.Equal(1, CountOccurrences(method, "SqlQueryRaw<FundChangeCombinedQueryRow>"));
        Assert.Equal(1, CountOccurrences(method, ".ToListAsync(cancellationToken)"));
        Assert.Equal(1, CountOccurrences(method, "FROM fund_operations"));
        Assert.Contains("LEFT JOIN app_users actor", method, StringComparison.Ordinal);
        Assert.Contains("FROM page_rows", method, StringComparison.Ordinal);
        Assert.Contains("SUM(amount) FILTER", method, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)::int", method, StringComparison.Ordinal);
        Assert.Contains("OFFSET @offset", method, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit", method, StringComparison.Ordinal);
        Assert.Contains("fund.\"Name\" ILIKE @search", method, StringComparison.Ordinal);
        Assert.Contains("operation.\"OperationKind\" ILIKE @search", method, StringComparison.Ordinal);
        Assert.Contains("operation.\"Reason\" ILIKE @search", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsolidatedMonthlyQuery_AggregatesMonthlyTotalsAndCountsInDatabase()
    {
        var source = ReadApiSource("Infrastructure/Data/EfConsolidatedMonthlyReportQuery.cs");

        Assert.True(
            CountOccurrences(source, ".GroupBy(") >= 5,
            "Consolidated query must group monthly totals and complete income/expense breakdowns in the database.");
        Assert.True(
            CountOccurrences(source, "group.Sum(") >= 4,
            "Consolidated monetary totals must be aggregated before materialization.");
        Assert.True(
            CountOccurrences(source, "group.Count()") >= 3,
            "Consolidated monthly row counts must be aggregated before materialization.");
        Assert.Contains("new { operation.AccountingMonth, operation.OperationKind }", source, StringComparison.Ordinal);
        Assert.Contains("operation.OperationKind == FinancialOperationKinds.Income", source, StringComparison.Ordinal);
        Assert.Contains("operation.OperationKind == FinancialOperationKinds.Expense", source, StringComparison.Ordinal);
        Assert.Contains("incomeBreakdownQuery", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(expenseBreakdownQuery)", source, StringComparison.Ordinal);
        Assert.Contains("operationMonthlyQuery", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(accrualMonthlyQuery)", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(readingMonthlyQuery)", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(garageStartingBalanceQuery)", source, StringComparison.Ordinal);
        Assert.Equal(6, CountOccurrences(source, ".Concat("));
        Assert.True(CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 1);
        Assert.Contains("transfer.TransferDate < periodEndExclusive", source, StringComparison.Ordinal);
        Assert.Contains("operation.OperationDate < periodEndExclusive", source, StringComparison.Ordinal);
        Assert.Contains("operation.ExpensePaymentType != ExpensePaymentTypes.WithoutReceipt", source, StringComparison.Ordinal);
        Assert.Contains("GroupBy(transfer => transfer.TransferDate)", source, StringComparison.Ordinal);
        Assert.Contains("GroupBy(operation => operation.OperationDate)", source, StringComparison.Ordinal);
        Assert.Contains("group.Sum(garage => garage.StartingBalance)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SumAsync(garage => garage.StartingBalance, cancellationToken)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("incomeByMonth.Count == 0 && expenseByMonth.Count == 0", source, StringComparison.Ordinal);
        var postgresStart = source.IndexOf("private async Task<ConsolidatedMonthlyReportData> GetPostgresDataAsync", StringComparison.Ordinal);
        var fallbackStart = source.IndexOf("private static IReadOnlyList<MonthlyReportQueryRow> GetFallbackMonthlyRows", StringComparison.Ordinal);
        var postgresSource = source[postgresStart..fallbackStart];
        Assert.Equal(1, CountOccurrences(postgresSource, "SqlQueryRaw<ConsolidatedReportCombinedQueryRow>"));
        Assert.Equal(2, CountOccurrences(postgresSource, ".ToListAsync(cancellationToken)"));
        Assert.Equal(4, CountOccurrences(postgresSource, "AS MATERIALIZED"));
        Assert.Equal(1, CountOccurrences(postgresSource, "FROM financial_operations"));
        Assert.Equal(1, CountOccurrences(postgresSource, "FROM accruals"));
        Assert.Equal(1, CountOccurrences(postgresSource, "FROM meter_readings"));
        Assert.Equal(1, CountOccurrences(postgresSource, "FROM garages"));
    }

    [Fact]
    public void GarageIncomeWorksheetQuery_CombinesAllFinancialBucketsIntoOneDatabaseCommand()
    {
        var source = ReadApiSource("Infrastructure/Data/EfGarageIncomeWorksheetQuery.cs");
        var serviceSource = ReadApiSource("Application/Finance/FinanceService.cs");

        Assert.Contains("garageQuery", source, StringComparison.Ordinal);
        Assert.Contains("previousAccrualQuery", source, StringComparison.Ordinal);
        Assert.Contains("previousIncomeQuery", source, StringComparison.Ordinal);
        Assert.Contains("accrualBucketQuery", source, StringComparison.Ordinal);
        Assert.Contains("incomeBucketQuery", source, StringComparison.Ordinal);
        Assert.Contains("meterReadingQuery", source, StringComparison.Ordinal);
        Assert.Contains("meterIncomeTypeQuery", source, StringComparison.Ordinal);
        Assert.Contains("annualAccrualQuery", source, StringComparison.Ordinal);
        Assert.Contains("allocationQuery", source, StringComparison.Ordinal);
        Assert.Contains("advanceQuery", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(meterIncomeTypeQuery)", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(annualAccrualQuery)", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(allocationQuery)", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(advanceQuery)", source, StringComparison.Ordinal);
        Assert.Equal(9, CountOccurrences(source, ".Concat("));
        Assert.Equal(1, CountOccurrences(source, ".ToListAsync(cancellationToken)"));
        Assert.Contains("garageIncomeWorksheetQuery.GetAsync", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void GarageBalanceHistoryQuery_CombinesOpeningAndMonthlyTotalsIntoOneDatabaseCommand()
    {
        var source = ReadApiSource("Infrastructure/Data/EfGarageBalanceHistoryQuery.cs");
        var serviceSource = ReadApiSource("Application/Finance/FinanceService.cs");

        Assert.Contains("garageQuery", source, StringComparison.Ordinal);
        Assert.Contains("accrualQuery", source, StringComparison.Ordinal);
        Assert.Contains("incomeQuery", source, StringComparison.Ordinal);
        Assert.Contains("IsPrevious = accrual.AccountingMonth < monthFrom", source, StringComparison.Ordinal);
        Assert.Contains("IsPrevious = operation.AccountingMonth < monthFrom", source, StringComparison.Ordinal);
        Assert.Contains("accrual.AccountingMonth <= monthTo", source, StringComparison.Ordinal);
        Assert.Contains("operation.AccountingMonth <= monthTo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("previousAccrualQuery", source, StringComparison.Ordinal);
        Assert.DoesNotContain("previousIncomeQuery", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, ".Concat("));
        Assert.Equal(1, CountOccurrences(source, ".ToListAsync(cancellationToken)"));
        Assert.Contains("garageBalanceHistoryQuery.GetAsync", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void GarageReportQueries_CombineRelatedDatabaseAggregates()
    {
        var garageReport = ReadApiSource("Infrastructure/Data/EfGarageReportQuery.cs");
        var consolidatedGarageReport = ReadApiSource("Infrastructure/Data/EfConsolidatedGarageReportQuery.cs");

        Assert.Contains("AccrualTotal = group.Sum(row => row.AccrualAmount)", garageReport, StringComparison.Ordinal);
        Assert.Contains("IncomeTotal = group.Sum(row => row.IncomeAmount)", garageReport, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceRows.SumAsync", garageReport, StringComparison.Ordinal);
        Assert.Contains("incomeByGarageQuery", consolidatedGarageReport, StringComparison.Ordinal);
        Assert.Contains(".Concat(accrualByGarageQuery)", consolidatedGarageReport, StringComparison.Ordinal);
        Assert.Contains(".Concat(readingsByGarageQuery)", consolidatedGarageReport, StringComparison.Ordinal);
        var postgresStart = consolidatedGarageReport.IndexOf("private async Task<ConsolidatedGarageRowsData> GetPostgresRowsAsync", StringComparison.Ordinal);
        var fallbackStart = consolidatedGarageReport.IndexOf("private async Task<ConsolidatedGarageRowsData> GetRowsWithoutSearchAsync", StringComparison.Ordinal);
        var postgresSource = consolidatedGarageReport[postgresStart..fallbackStart];
        Assert.Equal(1, CountOccurrences(postgresSource, "SqlQueryRaw<ConsolidatedGarageCombinedQueryRow>"));
        Assert.Equal(1, CountOccurrences(postgresSource, ".ToListAsync(cancellationToken)"));
        Assert.Equal(1, CountOccurrences(postgresSource, "FROM financial_operations"));
        Assert.Equal(1, CountOccurrences(postgresSource, "FROM accruals"));
        Assert.Equal(1, CountOccurrences(postgresSource, "FROM meter_readings"));
        Assert.Equal(1, CountOccurrences(postgresSource, "FROM garages"));
    }

    [Fact]
    public void FeeReportQuery_AggregatesTotalsAndGarageRowsInDatabase()
    {
        var source = ReadApiSource("Infrastructure/Data/EfFeeReportQuery.cs");

        Assert.True(
            CountOccurrences(source, ".GroupBy(") >= 4,
            "Fee report query must aggregate garage accruals/payments in the database and reuse those bounded groups for report totals.");
        Assert.True(
            CountOccurrences(source, "group.Sum(") >= 4,
            "Fee transaction rows must be summed into garage groups before materialization and those groups must supply final totals.");
        Assert.Contains("GetFeeReportPageAsync", source, StringComparison.Ordinal);
        Assert.Contains("OFFSET @offset {{limitClause}}", source, StringComparison.Ordinal);
        Assert.Contains("summary_rows AS", source, StringComparison.Ordinal);
        Assert.Contains("FROM garage_page", source, StringComparison.Ordinal);
        Assert.Contains("FROM debtor_page", source, StringComparison.Ordinal);
        Assert.Contains("FROM summary_rows", source, StringComparison.Ordinal);
        Assert.Contains("var accrualTotals = accrualsByGarage", source, StringComparison.Ordinal);
        Assert.Contains("var collectedTotals = rows", source, StringComparison.Ordinal);
        Assert.Contains("accrualQuery", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(paymentQuery)", source, StringComparison.Ordinal);
        var feeDataStart = source.IndexOf("GetFeeDataAsync", StringComparison.Ordinal);
        var campaignDataStart = source.IndexOf("GetFeeCampaignDataAsync", StringComparison.Ordinal);
        var feePageStart = source.IndexOf("GetFeeReportPageAsync", StringComparison.Ordinal);
        var feeDataSource = source[feeDataStart..campaignDataStart];
        var campaignDataSource = source[campaignDataStart..feePageStart];
        var feePageSource = source[feePageStart..];
        Assert.Equal(1, CountOccurrences(feeDataSource, ".ToListAsync(cancellationToken)"));
        Assert.Equal(2, CountOccurrences(campaignDataSource, ".ToListAsync(cancellationToken)"));
        Assert.Equal(1, CountOccurrences(feePageSource, ".ToListAsync(cancellationToken)"));
        Assert.Equal(1, CountOccurrences(feePageSource, "SqlQueryRaw<FeeReportCombinedQueryRow>"));
        Assert.Contains("AccrualPaymentAllocations", campaignDataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("missingGarageIds", source, StringComparison.Ordinal);
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
    public void ImportCreatedRecordsQuery_CombinesRunExistenceAndBoundedPostgresMaterialization()
    {
        var serviceSource = ReadApiSource("Application/Import/ImportService.cs");
        var repositorySource = ReadApiSource("Infrastructure/Data/EfImportRepository.cs");

        Assert.Contains("var limit = QueryLimits.NormalizeListSize(request.Limit, 100)", serviceSource, StringComparison.Ordinal);
        Assert.Contains("repository.GetCreatedRecordListDataAsync(runId, limit, cancellationToken)", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("repository.RunExistsAsync(runId", serviceSource[serviceSource.IndexOf("GetAccessImportCreatedRecordsAsync", StringComparison.Ordinal)..serviceSource.IndexOf("ExportAccessImportRunReportAsync", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Matches(
            BoundedQueryRegex(@"GetCreatedRecordListDataAsync[\s\S]*?IsNpgsql\(\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.SelectMany\([\s\S]*?DefaultIfEmpty\(\)[\s\S]*?\.ToListAsync\(cancellationToken\)"),
            repositorySource);
        Assert.Matches(
            BoundedQueryRegex(@"GetCreatedRecordListDataAsync[\s\S]*?RunExistsAsync\(runId[\s\S]*?\(await query\.ToListAsync\(cancellationToken\)\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToList\(\)"),
            repositorySource);
    }

    [Fact]
    public void ImportRunLogQuery_CombinesRunExistenceAndBoundedPostgresMaterialization()
    {
        var serviceSource = ReadApiSource("Application/Import/ImportService.cs");
        var repositorySource = ReadApiSource("Infrastructure/Data/EfImportRepository.cs");
        var methodStart = serviceSource.IndexOf("GetAccessImportRunLogEntriesAsync", StringComparison.Ordinal);
        var methodEnd = serviceSource.IndexOf("GetAccessImportCreatedRecordsAsync", methodStart, StringComparison.Ordinal);
        var methodSource = serviceSource[methodStart..methodEnd];

        Assert.Contains("var limit = QueryLimits.NormalizeListSize(request.Limit, 100)", methodSource, StringComparison.Ordinal);
        Assert.Contains("repository.GetRunLogEntryListDataAsync(runId, limit, cancellationToken)", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("repository.RunExistsAsync(runId", methodSource, StringComparison.Ordinal);
        Assert.Matches(
            BoundedQueryRegex(@"GetRunLogEntryListDataAsync[\s\S]*?IsNpgsql\(\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.SelectMany\([\s\S]*?DefaultIfEmpty\(\)[\s\S]*?\.ToListAsync\(cancellationToken\)"),
            repositorySource);
        Assert.Matches(
            BoundedQueryRegex(@"GetRunLogEntryListDataAsync[\s\S]*?RunExistsAsync\(runId[\s\S]*?\(await query\.ToListAsync\(cancellationToken\)\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToList\(\)"),
            repositorySource);
    }

    [Fact]
    public void ImportAuditQuery_CombinesCountersAndKeepsOnlyTwoDatabaseReads()
    {
        var source = ReadApiSource("Infrastructure/Data/EfImportRepository.cs");
        var methodStart = source.IndexOf("GetAuditDataAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("public void AddRun", methodStart, StringComparison.Ordinal);
        var methodSource = source[methodStart..methodEnd];

        Assert.Contains(".GroupBy(_ => 1)", methodSource, StringComparison.Ordinal);
        Assert.Contains("PendingRollbackRecordCount = group.Count", methodSource, StringComparison.Ordinal);
        Assert.Contains(".Distinct()", methodSource, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(methodSource, ".SingleOrDefaultAsync(cancellationToken)"));
        Assert.Equal(1, CountOccurrences(methodSource, ".ToListAsync(cancellationToken)"));
        Assert.DoesNotContain("await query.CountAsync", methodSource, StringComparison.Ordinal);
        Assert.Matches(
            BoundedQueryRegex(@"Select\(record => record\.SourceRowHash\)[\s\S]*?\.Distinct\(\)[\s\S]*?\.OrderBy\(rowHash => rowHash\)[\s\S]*?\.Take\(5\)[\s\S]*?\.ToListAsync\(cancellationToken\)"),
            methodSource);
        Assert.DoesNotContain(".Take(20)", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Distinct(StringComparer.Ordinal)", methodSource, StringComparison.Ordinal);
        Assert.Contains("targetEntityTypeSamples", methodSource, StringComparison.Ordinal);
        Assert.Contains("sourceRowFingerprintSamples", methodSource, StringComparison.Ordinal);
        Assert.Contains(".Concat(sourceRowFingerprintSamples)", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditHistoryQueries_KeepServerSidePaginationAndStructuredFiltersBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfAuditEventRepository.cs");
        var requiredFilters = new[]
        {
            "auditEvent.CreatedAtUtc >= request.DateFrom.Value",
            "auditEvent.CreatedAtUtc <= request.DateTo.Value",
            "ApplyNonDateFilters(query, request, IsNpgsqlProvider())",
            "auditEvent.Action == action",
            "ApplySectionFilter(query, request.Section)",
            "ApplyActionKindFilter(query, request.ActionKind)",
            "auditEvent.EntityType == entityType",
            "auditEvent.ActorUserId == request.ActorUserId.Value",
            "ApplyQuickFilter(query, request.QuickFilter)",
            "ApplyRelatedFilters(query, request, usePostgresSearch)",
            "auditEvent.RelatedGarageNumber",
            "auditEvent.RelatedAccountingMonth",
            "auditEvent.RelatedCounterpartyName",
            "auditEvent.RelatedDocumentNumber"
        };

        Assert.Contains("GetEventsPageAsync", source, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Matches(
            BoundedQueryRegex(@"ApplyNonDateFilters\(query, request, IsNpgsqlProvider\(\)\)[\s\S]*?CountAsync\(cancellationToken\)[\s\S]*?ProjectPageRows\(query[\s\S]*?OrderByDescending\(auditEvent => auditEvent\.CreatedAtUtc\)[\s\S]*?\.Skip\(offset\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.Matches(
            BoundedQueryRegex(@"OrderByDescending\(auditEvent => auditEvent\.CreatedAtUtc\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.All(requiredFilters, filter => Assert.Contains(filter, source, StringComparison.Ordinal));
        Assert.Contains("join actor in dbContext.Users.AsNoTracking()", source, StringComparison.Ordinal);
        Assert.Contains("from actor in actors.DefaultIfEmpty()", source, StringComparison.Ordinal);
        Assert.Contains("GetPostgresEventsPageAsync(query, offset, limit, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("SqlQueryRaw<int>(\"SELECT 1 AS \\\"Value\\\"\")", source, StringComparison.Ordinal);
        Assert.Contains("TotalCount = query.Count()", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(totalsRow)", source, StringComparison.Ordinal);
        var postgresPageStart = source.IndexOf("private async Task<AuditEventPageData> GetPostgresEventsPageAsync", StringComparison.Ordinal);
        var postgresPageEnd = source.IndexOf("private IQueryable<AuditEventPageProjection> ProjectPageRows", postgresPageStart, StringComparison.Ordinal);
        var postgresPageMethod = source[postgresPageStart..postgresPageEnd];
        Assert.Equal(1, CountOccurrences(postgresPageMethod, ".ToListAsync(cancellationToken)"));
        Assert.Contains("page.ActorsById", ReadApiSource("Application/Audit/AuditService.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("GetActorsAsync(page.Items", ReadApiSource("Application/Audit/AuditService.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void DictionarySearchQueries_KeepExplicitLimitForSearchAndDefaultLists()
    {
        var source = ReadApiSource("Application/Dictionaries/DictionaryService.cs");
        var staffMemberRepositorySource = ReadApiSource("Infrastructure/Data/EfStaffMemberRepository.cs");
        var tariffRepositorySource = ReadApiSource("Infrastructure/Data/EfTariffRepository.cs");
        var irregularPaymentRepositorySource = ReadApiSource("Infrastructure/Data/EfIrregularPaymentRepository.cs");
        var chargeServiceSettingRepositorySource = ReadApiSource("Infrastructure/Data/EfChargeServiceSettingRepository.cs");
        var feeCampaignRepositorySource = ReadApiSource("Infrastructure/Data/EfFeeCampaignRepository.cs");
        var garageRepositorySource = ReadApiSource("Infrastructure/Data/EfGarageRepository.cs");

        Assert.DoesNotContain(".Take(NormalizeListLimit(limit))", source, StringComparison.Ordinal);
        Assert.Contains(
            "staffMemberRepository.GetListAsync(departmentId, normalizedSearch, includeArchived, NormalizeListLimit(limit)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", staffMemberRepositorySource, StringComparison.Ordinal);
        Assert.Contains(
            "tariffRepository.GetListAsync(normalizedSearch, includeArchived, NormalizeListLimit(limit)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", tariffRepositorySource, StringComparison.Ordinal);
        Assert.Contains(
            "irregularPaymentRepository.GetListAsync(normalizedSearch, includeArchived, NormalizeListLimit(limit)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", irregularPaymentRepositorySource, StringComparison.Ordinal);
        Assert.Contains("dbContext.Database.IsNpgsql()", irregularPaymentRepositorySource, StringComparison.Ordinal);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", irregularPaymentRepositorySource, StringComparison.Ordinal);
        Assert.Contains("EF.Functions.ILike(item.Name, pattern", irregularPaymentRepositorySource, StringComparison.Ordinal);
        Assert.Contains(
            "chargeServiceSettingRepository.GetListAsync(\n            normalizedSearch,\n            includeArchived,\n            isRegular,\n            isMetered,\n            NormalizeListLimit(limit),\n            businessDateProvider.Today",
            source,
            StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", chargeServiceSettingRepositorySource, StringComparison.Ordinal);
        Assert.Contains("dbContext.Database.IsNpgsql()", chargeServiceSettingRepositorySource, StringComparison.Ordinal);
        Assert.Contains("PostgresLikeSearch.ContainsPattern(normalizedSearch)", chargeServiceSettingRepositorySource, StringComparison.Ordinal);
        Assert.Contains("EF.Functions.ILike(item.Name, pattern", chargeServiceSettingRepositorySource, StringComparison.Ordinal);
        Assert.Contains(
            "feeCampaignRepository.GetListAsync(normalizedSearch, includeArchived, NormalizeListLimit(limit)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", feeCampaignRepositorySource, StringComparison.Ordinal);
        Assert.Contains(
            "garageRepository.GetListAsync(normalizedSearch, includeArchived, normalizedLimit",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "garageRepository.GetPageAsync(normalizedSearch, filters, includeArchived, debtorsOnly, normalizedOffset, normalizedLimit",
            source,
            StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", garageRepositorySource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(normalizedLimit)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FundOperationsAndReleaseLists_KeepNormalizedOutputBounds()
    {
        var fundSource = ReadApiSource("Application/Funds/FundService.cs");
        var fundRepositorySource = ReadApiSource("Infrastructure/Data/EfFundRepository.cs");
        var releaseSource = ReadApiSource("Application/Releases/AppReleaseService.cs");
        var releaseSynchronizerSource = ReadApiSource("Application/Releases/AppReleaseCatalogSynchronizer.cs");
        var releaseRepositorySource = ReadApiSource("Infrastructure/Data/EfAppReleaseRepository.cs");
        var releaseVersionMigrationSource = ReadApiSource(
            "Infrastructure/Data/Migrations/20260829181340_OptimizeAppReleaseVersionLookup.cs");

        Assert.Contains(
            "var boundedLimit = QueryLimits.NormalizePageSize(limit, defaultSize: 1, maximumSize: 100)",
            fundSource,
            StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(fundRepositorySource, ".Take(limit)") >= 2,
            "Fund operation lists must apply the same bound in PostgreSQL and SQLite branches.");
        Assert.Contains("private const int DefaultLimit = 9", releaseSource, StringComparison.Ordinal);
        Assert.Contains("private const int MaxLimit = 50", releaseSource, StringComparison.Ordinal);
        Assert.Contains("QueryLimits.NormalizeListSize(limit, DefaultLimit, MaxLimit)", releaseSource, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", releaseRepositorySource, StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", releaseRepositorySource, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", releaseRepositorySource, StringComparison.Ordinal);
        Assert.Contains("item.Version.ToLower() == normalizedVersion", releaseRepositorySource, StringComparison.Ordinal);
        Assert.Contains("IX_app_releases_Version_ci", releaseVersionMigrationSource, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX", releaseVersionMigrationSource, StringComparison.Ordinal);
        Assert.Contains("LOWER(\"Version\")", releaseVersionMigrationSource, StringComparison.Ordinal);
        Assert.Contains("FileOptions.Asynchronous | FileOptions.SequentialScan", releaseSource, StringComparison.Ordinal);
        Assert.Contains("FileOptions.Asynchronous | FileOptions.SequentialScan", releaseSynchronizerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("File.OpenRead(", releaseSource, StringComparison.Ordinal);
        Assert.DoesNotContain("File.OpenRead(", releaseSynchronizerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiRuntime_UsesBoundedDbContextPoolAndCommandTimeoutWithoutAutomaticWriteRetry()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "backend", "GarageBalance.Api", "Program.cs"));
        var settings = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "backend", "GarageBalance.Api", "appsettings.json"));

        Assert.Contains("AddDbContextPool<GarageBalanceDbContext>", program, StringComparison.Ordinal);
        Assert.Contains("DatabasePerformance:DbContextPoolSize", program, StringComparison.Ordinal);
        Assert.Contains("DatabasePerformance:CommandTimeoutSeconds", program, StringComparison.Ordinal);
        Assert.Contains("DatabasePerformance:ConnectionTimeoutSeconds", program, StringComparison.Ordinal);
        Assert.Contains("DatabasePerformance:MaximumPoolSize", program, StringComparison.Ordinal);
        Assert.Contains("NpgsqlConnectionStringFactory.Create(", program, StringComparison.Ordinal);
        Assert.Contains("Math.Min(configuredDbContextPoolSize, dbMaximumPoolSize)", program, StringComparison.Ordinal);
        Assert.Contains("AddGarageBalanceResponseCompression()", program, StringComparison.Ordinal);
        Assert.Contains("UseResponseCompression()", program, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(", program, StringComparison.Ordinal);
        Assert.Contains("npgsqlOptions.CommandTimeout(dbCommandTimeoutSeconds)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableRetryOnFailure", program, StringComparison.Ordinal);
        Assert.Contains("\"DbContextPoolSize\": 32", settings, StringComparison.Ordinal);
        Assert.Contains("\"CommandTimeoutSeconds\": 30", settings, StringComparison.Ordinal);
        Assert.Contains("\"MaximumPoolSize\": 32", settings, StringComparison.Ordinal);
        Assert.Contains("\"MinimumPoolSize\": 2", settings, StringComparison.Ordinal);
        Assert.Contains("\"ConnectionIdleLifetimeSeconds\": 300", settings, StringComparison.Ordinal);
        Assert.Contains("\"ConnectionPruningIntervalSeconds\": 10", settings, StringComparison.Ordinal);
        Assert.Contains("\"KeepAliveSeconds\": 0", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiRuntime_AllowsHttpsRedirectionToBeDisabledBehindDocumentedReverseProxy()
    {
        var repositoryRoot = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        var settings = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "appsettings.json"));
        var vpsChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "vps-deployment-checklist.md"));

        Assert.Contains("GetValue(\"HttpsRedirection:Enabled\", true)", program, StringComparison.Ordinal);
        Assert.Contains("if (httpsRedirectionEnabled)", program, StringComparison.Ordinal);
        Assert.Contains("\"HttpsRedirection\"", settings, StringComparison.Ordinal);
        Assert.Contains("HttpsRedirection__Enabled=false", vpsChecklist, StringComparison.Ordinal);
    }

    [Fact]
    public void BankDepositTotal_IsFilteredAndAggregatedByAvailableBalanceQuery()
    {
        var source = ReadApiSource("Infrastructure/Data/EfFinanceAvailableBalanceQuery.cs");

        Assert.Contains("dbContext.CashBankTransfers", source, StringComparison.Ordinal);
        Assert.Contains("!transfer.IsCanceled", source, StringComparison.Ordinal);
        Assert.Contains("BankDepositTotal = group.Sum(transfer => transfer.Amount)", source, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(source, ".ToListAsync(cancellationToken)"));
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
    public void ReportSearchMigration_AddsPostgresTrigramIndexesForRawIlikeExpressions()
    {
        var source = ReadApiSource(
            "Infrastructure/Data/Migrations/20260803224435_OptimizeReportSearchExpressions.cs");
        var expectedIndexNames = new[]
        {
            "IX_funds_Name_trgm",
            "IX_fund_operations_OperationKind_trgm",
            "IX_fund_operations_Reason_trgm",
            "IX_expense_types_Name_trgm",
            "IX_income_types_Name_trgm",
            "IX_supplier_accruals_DocumentNumber_trgm",
            "IX_financial_operations_DocumentNumber_trgm",
            "IX_financial_operations_Comment_trgm",
            "IX_cash_bank_transfers_Comment_trgm"
        };

        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pg_trgm", source, StringComparison.Ordinal);
        Assert.Contains("USING gin", source, StringComparison.Ordinal);
        Assert.Contains("gin_trgm_ops", source, StringComparison.Ordinal);
        Assert.All(expectedIndexNames, indexName => Assert.Contains(indexName, source, StringComparison.Ordinal));
    }

    [Fact]
    public void BackgroundAccrualAutomation_UsesMonthScopedDistributedLock()
    {
        var program = ReadApiSource("Program.cs");
        var runner = ReadApiSource("Application/Finance/RegularAccrualAutomationRunner.cs");
        var lockSource = ReadApiSource("Infrastructure/Data/EfRegularAccrualAutomationLock.cs");

        Assert.Contains(
            "AddScoped<IRegularAccrualAutomationLock, EfRegularAccrualAutomationLock>()",
            program,
            StringComparison.Ordinal);
        Assert.True(
            runner.IndexOf("TryAcquireAsync(accountingMonth", StringComparison.Ordinal) <
            runner.IndexOf("GenerateRegularCatalogAccrualsAsync", StringComparison.Ordinal),
            "The distributed lock must be acquired before any automatic accrual is generated.");
        Assert.Contains("pg_try_advisory_lock", lockSource, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_unlock", lockSource, StringComparison.Ordinal);
        Assert.Contains("accountingMonth.Year * 100 + accountingMonth.Month", lockSource, StringComparison.Ordinal);
    }

    [Fact]
    public void UserAndAuditSearchStayBoundedAndIndexed()
    {
        var users = ReadApiSource("Infrastructure/Data/EfUserManagementRepository.cs");
        var audit = ReadApiSource("Infrastructure/Data/EfAuditEventRepository.cs");
        var migration = ReadApiSource(
            "Infrastructure/Data/Migrations/20260729234018_OptimizeUsersAndAuditSearch.cs");
        var normalizedCodesMigration = ReadApiSource(
            "Infrastructure/Data/Migrations/20260829172142_NormalizeAuditFilterCodes.cs");

        Assert.Contains(".Skip(offset)", users, StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", users, StringComparison.Ordinal);
        Assert.Contains("EF.Functions.ILike(user.DisplayName", users, StringComparison.Ordinal);
        Assert.Contains("ThenBy(user => user.Id)", users, StringComparison.Ordinal);
        Assert.Contains("GetPostgresEventsPageAsync", audit, StringComparison.Ordinal);
        Assert.Contains("EF.Functions.ILike(auditEvent.SearchText", audit, StringComparison.Ordinal);
        Assert.Contains("auditEvent.Section == normalizedSection", audit, StringComparison.Ordinal);
        Assert.Contains("auditEvent.ActionKind == normalizedActionKind", audit, StringComparison.Ordinal);
        Assert.Contains("auditEvent.RelatedAccountingMonth == accountingMonth", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("auditEvent.Section.ToLower() == normalizedSection", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("auditEvent.ActionKind.ToLower() == normalizedActionKind", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("auditEvent.RelatedAccountingMonth.ToLower() == accountingMonth", audit, StringComparison.Ordinal);
        Assert.Contains("IX_app_users_DisplayName_trgm", migration, StringComparison.Ordinal);
        Assert.Contains("IX_app_users_NormalizedEmail_trgm", migration, StringComparison.Ordinal);
        Assert.Contains("IX_audit_events_SearchText_trgm", migration, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(migration, "gin_trgm_ops") >= 9,
            "Users and audit contains-search must keep its PostgreSQL trigram indexes.");
        Assert.Contains("SET \"Section\" = LOWER(\"Section\")", normalizedCodesMigration, StringComparison.Ordinal);
        Assert.Contains("SET \"ActionKind\" = LOWER(\"ActionKind\")", normalizedCodesMigration, StringComparison.Ordinal);
        Assert.Contains("CK_audit_events_Section_lowercase", normalizedCodesMigration, StringComparison.Ordinal);
        Assert.Contains("CK_audit_events_ActionKind_lowercase", normalizedCodesMigration, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationSettingsKeepOneCurrentRowPerKey()
    {
        var model = ReadApiSource("Infrastructure/Data/GarageBalanceDbContext.cs");
        var settingsRepository = ReadApiSource("Infrastructure/Data/EfApplicationSettingRepository.cs");

        Assert.Contains("entity.HasIndex(setting => setting.Key).IsUnique()", model, StringComparison.Ordinal);
        Assert.Contains("SingleOrDefaultAsync(setting => setting.Key == key", settingsRepository, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToList", settingsRepository, StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceGuideCoversAutomatedGatesPostgresQueriesFrontendAndAcceptance()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "final-performance-checklist.md"));

        Assert.Contains("Производительность и стабильность", document, StringComparison.Ordinal);
        Assert.Contains("BackendPerformanceGuardTests", document, StringComparison.Ordinal);
        Assert.Contains("dotnet test GarageBalance.slnx --no-restore --configuration Release", document, StringComparison.Ordinal);
        Assert.Contains("npm run test:coverage", document, StringComparison.Ordinal);
        Assert.Contains("npm run check:bundle", document, StringComparison.Ordinal);
        Assert.Contains("180 KiB", document, StringComparison.Ordinal);
        Assert.Contains("EXPLAIN (ANALYZE, BUFFERS)", document, StringComparison.Ordinal);
        Assert.Contains("GIN trigram indexes", document, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL integration tests", document, StringComparison.Ordinal);
        Assert.Contains("браузер", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("не меняет формулы, округление, права и audit", document, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryAndReportLimitsStayCentralizedAcrossGrowingSections()
    {
        var limits = ReadApiSource("Application/Common/QueryLimits.cs");
        Assert.Contains("MaximumPageSize = 500", limits, StringComparison.Ordinal);
        Assert.Contains("MaximumReportPeriodMonths = 120", limits, StringComparison.Ordinal);

        string[] boundedServices =
        [
            "Application/Audit/AuditService.cs",
            "Application/Dictionaries/DictionaryService.cs",
            "Application/Finance/FinanceService.cs",
            "Application/Funds/FundService.cs",
            "Application/Import/ImportQuarantineService.cs",
            "Application/Import/ImportService.cs",
            "Application/Releases/AppReleaseService.cs",
            "Application/Reports/ReportService.cs",
            "Application/Users/UserManagementService.cs"
        ];
        foreach (var servicePath in boundedServices)
        {
            Assert.Contains("QueryLimits.", ReadApiSource(servicePath), StringComparison.Ordinal);
        }

        var reportService = ReadApiSource("Application/Reports/ReportService.cs");
        Assert.Equal(8, CountOccurrences(reportService, "ValidateReportPeriod<"));
        Assert.DoesNotContain("Math.Clamp(limit, 1, 500)", reportService, StringComparison.Ordinal);
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
