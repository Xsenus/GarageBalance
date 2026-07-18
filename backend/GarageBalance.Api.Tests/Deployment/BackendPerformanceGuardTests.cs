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
        Assert.Contains("meterReadingRepository.GetListAsync", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(meterReadingRepositorySource, ".Take(limit)") >= 4);
        Assert.Contains("accrualRepository.GetListAsync", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(accrualRepositorySource, ".Take(limit)") >= 4);
        Assert.Contains("supplierAccrualRepository.GetListAsync", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(supplierAccrualRepositorySource, ".Take(limit)") >= 4);
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
        Assert.Contains("operation.OperationKind == FundOperationKinds.Deposit", source, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(source, ".Concat("));
        Assert.Equal(1, CountOccurrences(source, ".ToListAsync(cancellationToken)"));
        Assert.Contains("var availableAmounts = CalculateAvailableAmounts(worksheetData.AvailableBalance);", serviceSource, StringComparison.Ordinal);
        Assert.Contains("var balance = await financeAvailableBalanceQuery.GetAsync", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FundDashboard_ReusesLoadedFundsAndAggregatesTotalsInOneDatabaseCommand()
    {
        var serviceSource = ReadApiSource("Application/Funds/FundService.cs");
        var repositorySource = ReadApiSource("Infrastructure/Data/EfFundRepository.cs");
        var totalsMethod = repositorySource[
            repositorySource.IndexOf("public async Task<FundTotalsData> GetTotalsAsync", StringComparison.Ordinal)..repositorySource.IndexOf("public async Task<IReadOnlyList<FundOperation>> GetOperationsOrderedAsync", StringComparison.Ordinal)];

        Assert.Contains("var funds = (await repository.GetFundsAsync(cancellationToken)).ToList();", serviceSource, StringComparison.Ordinal);
        Assert.Contains("EnsureDefaultFundsAsync(funds, cancellationToken)", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetNormalizedFundNamesAsync", serviceSource, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(totalsMethod, ".Sum("));
        Assert.Equal(1, CountOccurrences(totalsMethod, ".FirstOrDefaultAsync(cancellationToken)"));
        Assert.DoesNotContain("SumAsync", totalsMethod, StringComparison.Ordinal);
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
        Assert.Equal(1, CountOccurrences(balanceMethod, ".ToListAsync(cancellationToken)"));
    }

    [Fact]
    public void MissingMeterReadingQuery_BoundsAntiQueriesAndCandidateMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfMissingMeterReadingQuery.cs");

        Assert.Contains("!dbContext.MeterReadings.Any", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 3);
        Assert.True(CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 2);
        Assert.Contains(".Where(garage => !garage.IsArchived)", source, StringComparison.Ordinal);
        Assert.Contains("normalizedSearch is not null && IsSqliteProvider()", source, StringComparison.Ordinal);
        Assert.Contains("CandidateMatchesSearch", source, StringComparison.Ordinal);
        Assert.Contains("HasWaterReading", source, StringComparison.Ordinal);
        Assert.Contains("HasElectricityReading", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var meterKind in meterKinds)", source, StringComparison.Ordinal);
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
        Assert.Contains("IsSqliteProvider() && sortBy is \"debt\" or \"contactPerson\" or \"phone\" or \"email\"", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPageSorting(query, sortBy, sortDescending)", source, StringComparison.Ordinal);
        Assert.Contains("supplierIds.Contains(contact.SupplierId) && !contact.IsArchived", source, StringComparison.Ordinal);
        Assert.Contains("contact.Status == \"Работает\"", source, StringComparison.Ordinal);
        Assert.Contains("SupplierPrimaryContactData", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
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
        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 3);
        Assert.True(CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 4);
    }

    [Fact]
    public void ExpenseTypeRepository_UsesProviderAwareSearchAndDatabasePaging()
    {
        var source = ReadApiSource("Infrastructure/Data/EfExpenseTypeRepository.cs");
        Assert.Contains("IsSqliteProvider()", source, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".Take(limit)") >= 3);
        Assert.True(CountOccurrences(source, ".ToListAsync(cancellationToken)") >= 4);
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
        Assert.Contains(".Take(limit)", source, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Matches(
            BoundedQueryRegex(@"GetActiveRegularAsync[\s\S]*?Where\(setting => !setting\.IsArchived && setting\.IsRegular\)[\s\S]*?OrderBy\(setting => setting\.Name\)[\s\S]*?ToListAsync\(cancellationToken\)"),
            source);
        Assert.Matches(
            BoundedQueryRegex(@"GetActiveRegularForDueDatesAsync[\s\S]*?IncomeTypeId == incomeTypeId[\s\S]*?Take\(2\)[\s\S]*?ToListAsync\(cancellationToken\)"),
            source);
    }

    [Fact]
    public void FeeCampaignRepository_UsesDatabaseLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfFeeCampaignRepository.cs");
        Assert.Contains(".Take(limit)", source, StringComparison.Ordinal);
        Assert.Contains(".ToListAsync(cancellationToken)", source, StringComparison.Ordinal);
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
        Assert.Equal(3, CountOccurrences(expenseSource, "aggregateQuery = aggregateQuery.Concat("));
        Assert.Equal(1, CountOccurrences(expenseSource, "aggregateQuery.ToListAsync(cancellationToken)"));
        Assert.Contains("StartingBalanceTotalCategory", expenseSource, StringComparison.Ordinal);
        Assert.Contains("AccrualTotalCategory", expenseSource, StringComparison.Ordinal);
        Assert.Contains("ExpenseTotalCategory", expenseSource, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(incomeSource, "aggregateQuery = aggregateQuery.Concat("));
        Assert.Equal(1, CountOccurrences(incomeSource, "aggregateQuery.ToListAsync(cancellationToken)"));
        Assert.Contains("StartingBalanceTotalCategory", incomeSource, StringComparison.Ordinal);
        Assert.Contains("AccrualTotalCategory", incomeSource, StringComparison.Ordinal);
        Assert.Contains("IncomeTotalCategory", incomeSource, StringComparison.Ordinal);
        var incomeDebtMethod = incomeSource[
            incomeSource.IndexOf("private async Task<IReadOnlyDictionary<Guid, decimal>> CalculateDebtAfterPaymentsAsync", StringComparison.Ordinal)..incomeSource.IndexOf("private static IQueryable<T> ApplyLimit", StringComparison.Ordinal)];
        Assert.Contains("startingBalanceQuery", incomeDebtMethod, StringComparison.Ordinal);
        Assert.Contains("accrualQuery", incomeDebtMethod, StringComparison.Ordinal);
        Assert.Contains("paymentQuery", incomeDebtMethod, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(incomeDebtMethod, ".Concat("));
        Assert.Equal(1, CountOccurrences(incomeDebtMethod, ".ToListAsync(cancellationToken)"));
        Assert.Contains("useClientSearch = hasSearch && !", incomeSource, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(incomeSource, "ToLower().Contains(normalizedSearch!)") >= 6,
            "PostgreSQL income-report search must be applied to source queries before count, sum and page materialization.");
        Assert.Contains("useClientSearch = hasSearch && !", expenseSource, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(expenseSource, "ToLower().Contains(normalizedSearch!)") >= 7,
            "PostgreSQL expense-report search must be applied to source queries before count, sum and page materialization.");
        Assert.Matches(
            BoundedQueryRegex(@"ExecuteBoundedRowsAsync[\s\S]*?CountAsync\(cancellationToken\)[\s\S]*?ApplyLimit\(query, limit\)\.ToListAsync\(cancellationToken\)"),
            garageSource);
        Assert.True(
            CountOccurrences(garageSource, ".GroupBy(") >= 3,
            "Search-compatible consolidated garage fallback must aggregate income, accrual and readings by garage.");
        Assert.Matches(
            BoundedQueryRegex(@"GetRowsWithServerSearchAsync[\s\S]*?\.Where\(garage =>[\s\S]*?BuildRowsQuery\(garages, periodFrom, periodTo\)[\s\S]*?ExecuteBoundedRowsAsync"),
            garageSource);
        var serverSearchMethod = garageSource[
            garageSource.IndexOf("private Task<ConsolidatedGarageRowsData> GetRowsWithServerSearchAsync", StringComparison.Ordinal)..garageSource.IndexOf("private IQueryable<ConsolidatedGarageProjectionRow> BuildRowsQuery", StringComparison.Ordinal)];
        Assert.DoesNotContain("ToListAsync", serverSearchMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("ToList()", serverSearchMethod, StringComparison.Ordinal);
        Assert.Contains("garage.Owner.LastName.ToLower().Contains(normalizedServerSearch)", garageSource, StringComparison.Ordinal);
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

        Assert.True(CountOccurrences(source, ".Concat(") >= 2, "Garage report sources must remain a SQL UNION ALL pipeline.");
        Assert.True(CountOccurrences(source, ".GroupBy(") >= 2, "Expanded and grouped garage modes must aggregate before paging.");
        Assert.Contains("AccrualTotal = group.Sum(row => row.AccrualAmount)", source, StringComparison.Ordinal);
        Assert.Contains("IncomeTotal = group.Sum(row => row.IncomeAmount)", source, StringComparison.Ordinal);
        Assert.Contains("RowCount = group.Count()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceRows.SumAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("groupedRows.CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("if (summary is null)", source, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", source, StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", source, StringComparison.Ordinal);
        Assert.Contains("if (IsNpgsql())", source, StringComparison.Ordinal);
        Assert.Contains("matchingGarageIds", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CashPaymentScreenQuery_UsesDatabaseCountSumAndPageBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfCashMovementReportQuery.cs");

        Assert.Matches(
            BoundedQueryRegex(@"GetCashPaymentsAsync[\s\S]*?IsNpgsql\(\)[\s\S]*?RowCount = group\.Count\(\)[\s\S]*?Total = group\.Sum\(operation => operation\.Amount\)[\s\S]*?ApplyPage\(ordered, offset, limit\)\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.DoesNotContain("query.CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("query.Skip(offset)", source, StringComparison.Ordinal);
        Assert.Contains("operation.Supplier.Name.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Contains("operation.ExpenseType.Name.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BankDepositScreenQuery_UsesDatabaseCountSumAndPageBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfCashMovementReportQuery.cs");

        Assert.Matches(
            BoundedQueryRegex(@"GetBankDepositsAsync[\s\S]*?operation\.Fund\.Name\.ToLower\(\)\.Contains\(normalizedSearch\)[\s\S]*?RowCount = group\.Count\(\)[\s\S]*?Total = group\.Sum\(operation => operation\.Amount\)[\s\S]*?ApplyPage\(orderedQuery, offset, limit\)\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.Contains("query.Skip(offset)", source, StringComparison.Ordinal);
        Assert.Contains("operation.Reason.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FundChangeScreenQuery_UsesDatabaseCountSumsAndLimitBeforeMaterialization()
    {
        var source = ReadApiSource("Infrastructure/Data/EfFundChangeReportQuery.cs");

        Assert.Matches(
            BoundedQueryRegex(@"GetFundChangesAsync[\s\S]*?GroupBy\(operation => operation\.OperationKind\)[\s\S]*?RowCount = group\.Count\(\)[\s\S]*?Total = group\.Sum\(operation => operation\.Amount\)[\s\S]*?ProjectRows\(ApplyPage\(ordered, offset, limit\)\)\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.DoesNotContain("query.CountAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("query.Skip(offset)", source, StringComparison.Ordinal);
        Assert.Contains("operation.Fund.Name.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Contains("operation.OperationKind.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Contains("operation.Reason.ToLower().Contains(normalizedSearch)", source, StringComparison.Ordinal);
        Assert.Contains("join actor in dbContext.Users.AsNoTracking()", source, StringComparison.Ordinal);
        Assert.Contains("from actor in actors.DefaultIfEmpty()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToDictionaryAsync", source, StringComparison.Ordinal);
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
        Assert.Equal(5, CountOccurrences(source, ".Concat("));
        Assert.Equal(1, CountOccurrences(source, ".ToListAsync(cancellationToken)"));
        Assert.Contains("group.Sum(garage => garage.StartingBalance)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SumAsync(garage => garage.StartingBalance, cancellationToken)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("incomeByMonth.Count == 0 && expenseByMonth.Count == 0", source, StringComparison.Ordinal);
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
        Assert.Contains("annualAllocationQuery", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(meterIncomeTypeQuery)", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(annualAccrualQuery)", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(annualAllocationQuery)", source, StringComparison.Ordinal);
        Assert.Equal(8, CountOccurrences(source, ".Concat("));
        Assert.Equal(1, CountOccurrences(source, ".ToListAsync(cancellationToken)"));
        Assert.Contains("garageIncomeWorksheetQuery.GetAsync", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void GarageBalanceHistoryQuery_CombinesOpeningAndMonthlyTotalsIntoOneDatabaseCommand()
    {
        var source = ReadApiSource("Infrastructure/Data/EfGarageBalanceHistoryQuery.cs");
        var serviceSource = ReadApiSource("Application/Finance/FinanceService.cs");

        Assert.Contains("garageQuery", source, StringComparison.Ordinal);
        Assert.Contains("previousAccrualQuery", source, StringComparison.Ordinal);
        Assert.Contains("previousIncomeQuery", source, StringComparison.Ordinal);
        Assert.Contains("accrualBucketQuery", source, StringComparison.Ordinal);
        Assert.Contains("incomeBucketQuery", source, StringComparison.Ordinal);
        Assert.Contains(".GroupBy(accrual => accrual.AccountingMonth)", source, StringComparison.Ordinal);
        Assert.Contains(".GroupBy(operation => operation.AccountingMonth)", source, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(source, ".Concat("));
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
        Assert.DoesNotContain("ToDictionaryAsync(", source, StringComparison.Ordinal);
        Assert.Contains("var accrualTotals = accrualsByGarage", source, StringComparison.Ordinal);
        Assert.Contains("var collectedTotals = rows", source, StringComparison.Ordinal);
        Assert.Contains("accrualQuery", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(paymentQuery)", source, StringComparison.Ordinal);
        var feeDataStart = source.IndexOf("GetFeeDataAsync", StringComparison.Ordinal);
        var campaignDataStart = source.IndexOf("GetFeeCampaignDataAsync", StringComparison.Ordinal);
        var feeDataSource = source[feeDataStart..campaignDataStart];
        var campaignDataSource = source[campaignDataStart..];
        Assert.Equal(1, CountOccurrences(feeDataSource, ".ToListAsync(cancellationToken)"));
        Assert.Equal(2, CountOccurrences(campaignDataSource, ".ToListAsync(cancellationToken)"));
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
            BoundedQueryRegex(@"ApplyNonDateFilters\(query, request\)[\s\S]*?CountAsync\(cancellationToken\)[\s\S]*?ProjectPageRows\(query[\s\S]*?OrderByDescending\(auditEvent => auditEvent\.CreatedAtUtc\)[\s\S]*?\.Skip\(offset\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.Matches(
            BoundedQueryRegex(@"OrderByDescending\(auditEvent => auditEvent\.CreatedAtUtc\)[\s\S]*?\.Take\(limit\)[\s\S]*?\.ToListAsync\(cancellationToken\)"),
            source);
        Assert.All(requiredFilters, filter => Assert.Contains(filter, source, StringComparison.Ordinal));
        Assert.Contains("join actor in dbContext.Users.AsNoTracking()", source, StringComparison.Ordinal);
        Assert.Contains("from actor in actors.DefaultIfEmpty()", source, StringComparison.Ordinal);
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
        Assert.Contains(
            "chargeServiceSettingRepository.GetListAsync(normalizedSearch, includeArchived, NormalizeListLimit(limit)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", chargeServiceSettingRepositorySource, StringComparison.Ordinal);
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
            "garageRepository.GetPageAsync(normalizedSearch, includeArchived, debtorsOnly, normalizedOffset, normalizedLimit",
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
        var releaseRepositorySource = ReadApiSource("Infrastructure/Data/EfAppReleaseRepository.cs");

        Assert.Contains("var boundedLimit = Math.Clamp(limit, 1, 100)", fundSource, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(fundRepositorySource, ".Take(limit)") >= 2,
            "Fund operation lists must apply the same bound in PostgreSQL and SQLite branches.");
        Assert.Contains("private const int DefaultLimit = 9", releaseSource, StringComparison.Ordinal);
        Assert.Contains("private const int MaxLimit = 50", releaseSource, StringComparison.Ordinal);
        Assert.Contains(".Skip(offset)", releaseRepositorySource, StringComparison.Ordinal);
        Assert.Contains(".Take(limit)", releaseRepositorySource, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", releaseRepositorySource, StringComparison.Ordinal);
    }

    [Fact]
    public void FundDepositTotal_IsFilteredAndAggregatedByAvailableBalanceQuery()
    {
        var source = ReadApiSource("Infrastructure/Data/EfFinanceAvailableBalanceQuery.cs");

        Assert.Contains("!operation.IsCanceled", source, StringComparison.Ordinal);
        Assert.Contains("operation.OperationKind == FundOperationKinds.Deposit", source, StringComparison.Ordinal);
        Assert.Contains("operation.IsCashToBankTransfer", source, StringComparison.Ordinal);
        Assert.Contains("BankDepositTotal = group.Sum(operation => operation.Amount)", source, StringComparison.Ordinal);
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
        Assert.Contains("psql` and Docker tooling", document, StringComparison.Ordinal);
        Assert.Contains("authorized VPS", document, StringComparison.Ordinal);
        Assert.Contains("Post-cleanup service checks passed", document, StringComparison.Ordinal);
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
