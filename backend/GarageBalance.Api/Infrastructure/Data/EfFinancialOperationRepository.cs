using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFinancialOperationRepository(GarageBalanceDbContext dbContext) : IFinancialOperationRepository
{
    public async Task<IReadOnlyList<FinancialOperation>> GetListAsync(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? operationKind,
        string? normalizedSearch,
        Guid? garageId,
        Guid? supplierId,
        Guid? staffMemberId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), dateFrom, dateTo, operationKind, garageId, supplierId, staffMemberId);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            return (await Order(query).ToListAsync(cancellationToken))
                .Where(operation => OperationMatchesSearch(operation, normalizedSearch))
                .Take(limit)
                .ToList();
        }

        return await Order(ApplySearch(query, normalizedSearch))
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<FinancialOperationPageData> GetPageAsync(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? operationKind,
        string? normalizedSearch,
        Guid? garageId,
        Guid? supplierId,
        Guid? staffMemberId,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), dateFrom, dateTo, operationKind, garageId, supplierId, staffMemberId);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            var filtered = (await Order(query).ToListAsync(cancellationToken))
                .Where(operation => OperationMatchesSearch(operation, normalizedSearch))
                .ToList();
            return new FinancialOperationPageData(filtered.Skip(offset).Take(limit).ToList(), filtered.Count);
        }

        query = ApplySearch(query, normalizedSearch);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await Order(query)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new FinancialOperationPageData(items, totalCount);
    }

    public Task<FinancialOperation?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Aggregate(dbContext.FinancialOperations)
            .SingleOrDefaultAsync(operation => operation.Id == id, cancellationToken);

    public Task<bool> ActiveDocumentDuplicateExistsAsync(
        Guid? ignoredId,
        string operationKind,
        DateOnly operationDate,
        string documentNumber,
        CancellationToken cancellationToken) =>
        dbContext.FinancialOperations.AsNoTracking().AnyAsync(operation =>
            !operation.IsCanceled &&
            (!ignoredId.HasValue || operation.Id != ignoredId.Value) &&
            operation.OperationKind == operationKind &&
            operation.OperationDate == operationDate &&
            operation.DocumentNumber == documentNumber,
            cancellationToken);

    public async Task<decimal> GetIncomeTotalBeforeMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken) =>
        await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.AccountingMonth < accountingMonth)
            .SumAsync(operation => operation.Amount, cancellationToken);

    public async Task<IReadOnlyList<FinancialOperationBucketData>> GetIncomeMonthlyBucketsAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.AccountingMonth >= monthFrom &&
                operation.AccountingMonth <= monthTo)
            .GroupBy(operation => operation.AccountingMonth)
            .Select(group => new { AccountingMonth = group.Key, Amount = group.Sum(operation => operation.Amount) })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new FinancialOperationBucketData(row.AccountingMonth, row.Amount)).ToList();
    }

    public async Task<IReadOnlyList<FinancialOperationIncomeTypeBucketData>> GetIncomeTypeBucketsAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.IncomeTypeId != null &&
                operation.AccountingMonth >= monthFrom &&
                operation.AccountingMonth <= monthTo)
            .GroupBy(operation => new
            {
                operation.AccountingMonth,
                IncomeTypeId = operation.IncomeTypeId!.Value,
                operation.IncomeType!.Name,
                operation.IncomeType.Code
            })
            .Select(group => new
            {
                group.Key.AccountingMonth,
                group.Key.IncomeTypeId,
                IncomeTypeName = group.Key.Name,
                IncomeTypeCode = group.Key.Code,
                Amount = group.Sum(operation => operation.Amount)
            })
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new FinancialOperationIncomeTypeBucketData(row.AccountingMonth, row.IncomeTypeId, row.IncomeTypeName, row.IncomeTypeCode, row.Amount))
            .ToList();
    }

    public async Task<FinancialOperationWorksheetData> GetWorksheetDataAsync(DateOnly accountingMonth, CancellationToken cancellationToken)
    {
        var expenses = await dbContext.FinancialOperations.AsNoTracking()
            .Include(operation => operation.Supplier)
            .Include(operation => operation.StaffMember)
                .ThenInclude(staffMember => staffMember!.Department)
            .Include(operation => operation.ExpenseType)
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.AccountingMonth == accountingMonth)
            .ToListAsync(cancellationToken);
        var incomes = await dbContext.FinancialOperations.AsNoTracking()
            .Include(operation => operation.IncomeType)
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.AccountingMonth == accountingMonth &&
                operation.IncomeTypeId != null)
            .ToListAsync(cancellationToken);
        return new FinancialOperationWorksheetData(expenses, incomes);
    }

    public async Task<decimal> GetOpeningDebtPaymentTotalAsync(
        Guid garageId,
        DateOnly accountingMonth,
        string incomeTypeCode,
        string incomeTypeName,
        CancellationToken cancellationToken) =>
        await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.AccountingMonth == accountingMonth &&
                operation.IncomeType != null &&
                (operation.IncomeType.Code == incomeTypeCode || operation.IncomeType.Name == incomeTypeName))
            .SumAsync(operation => operation.Amount, cancellationToken);

    public async Task<decimal> GetBankExpenseTotalAsync(
        string[] cashExpenseTypeCodes,
        string[] cashExpenseTypeNames,
        CancellationToken cancellationToken) =>
        await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                (operation.ExpenseType == null ||
                    !(
                        (operation.ExpenseType.Code != null && cashExpenseTypeCodes.Contains(operation.ExpenseType.Code)) ||
                        cashExpenseTypeNames.Contains(operation.ExpenseType.Name))))
            .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;

    public async Task<FinancialOperationCashBalanceData> GetCashBalanceDataAsync(
        string[] cashExpenseTypeCodes,
        string[] cashExpenseTypeNames,
        CancellationToken cancellationToken)
    {
        var incomeTotal = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income)
            .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;
        var cashExpenseTotal = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.ExpenseType != null &&
                ((operation.ExpenseType.Code != null && cashExpenseTypeCodes.Contains(operation.ExpenseType.Code)) ||
                    cashExpenseTypeNames.Contains(operation.ExpenseType.Name)))
            .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;
        return new FinancialOperationCashBalanceData(incomeTotal, cashExpenseTotal);
    }

    public async Task<decimal> GetStaffExpenseTotalAsync(Guid staffMemberId, DateOnly accountingMonth, CancellationToken cancellationToken) =>
        await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.StaffMemberId == staffMemberId &&
                operation.AccountingMonth == accountingMonth)
            .SumAsync(operation => operation.Amount, cancellationToken);

    public async Task<decimal> GetPreviousGarageIncomeTotalAsync(Guid ignoredId, Guid garageId, DateOnly operationDate, CancellationToken cancellationToken) =>
        await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.Id != ignoredId &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.OperationDate < operationDate)
            .SumAsync(operation => operation.Amount, cancellationToken);

    public async Task<decimal> GetPreviousSupplierExpenseTotalAsync(Guid ignoredId, Guid supplierId, DateOnly operationDate, CancellationToken cancellationToken) =>
        await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.Id != ignoredId &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.SupplierId == supplierId &&
                operation.OperationDate < operationDate)
            .SumAsync(operation => operation.Amount, cancellationToken);

    public void Add(FinancialOperation operation) => dbContext.FinancialOperations.Add(operation);

    private IQueryable<FinancialOperation> QueryActive() =>
        Aggregate(dbContext.FinancialOperations.AsNoTracking())
            .Where(operation => !operation.IsCanceled);

    private static IQueryable<FinancialOperation> Aggregate(IQueryable<FinancialOperation> query) =>
        query
            .Include(operation => operation.Garage)
            .ThenInclude(garage => garage!.Owner)
            .Include(operation => operation.IncomeType)
            .Include(operation => operation.Supplier)
            .Include(operation => operation.StaffMember)
            .ThenInclude(staffMember => staffMember!.Department)
            .Include(operation => operation.ExpenseType);

    private static IQueryable<FinancialOperation> ApplyFilters(
        IQueryable<FinancialOperation> query,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? operationKind,
        Guid? garageId,
        Guid? supplierId,
        Guid? staffMemberId)
    {
        if (dateFrom.HasValue)
        {
            query = query.Where(operation => operation.OperationDate >= dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            query = query.Where(operation => operation.OperationDate <= dateTo.Value);
        }

        if (operationKind is not null)
        {
            query = query.Where(operation => operation.OperationKind == operationKind);
        }

        if (garageId.HasValue)
        {
            query = query.Where(operation => operation.GarageId == garageId.Value);
        }

        if (supplierId.HasValue)
        {
            query = query.Where(operation => operation.SupplierId == supplierId.Value);
        }

        if (staffMemberId.HasValue)
        {
            query = query.Where(operation => operation.StaffMemberId == staffMemberId.Value);
        }

        return query;
    }

    private static IQueryable<FinancialOperation> ApplySearch(IQueryable<FinancialOperation> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        return query.Where(operation =>
            (operation.DocumentNumber != null && operation.DocumentNumber.ToLower().Contains(normalizedSearch)) ||
            (operation.Comment != null && operation.Comment.ToLower().Contains(normalizedSearch)) ||
            (operation.Garage != null && operation.Garage.Number.ToLower().Contains(normalizedSearch)) ||
            (operation.Supplier != null && operation.Supplier.Name.ToLower().Contains(normalizedSearch)) ||
            (operation.StaffMember != null && operation.StaffMember.FullName.ToLower().Contains(normalizedSearch)));
    }

    private static IOrderedQueryable<FinancialOperation> Order(IQueryable<FinancialOperation> query) =>
        query.OrderByDescending(operation => operation.OperationDate)
            .ThenBy(operation => operation.DocumentNumber);

    private static bool OperationMatchesSearch(FinancialOperation operation, string normalizedSearch) =>
        (operation.DocumentNumber?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.Garage?.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.Supplier?.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.StaffMember?.FullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
