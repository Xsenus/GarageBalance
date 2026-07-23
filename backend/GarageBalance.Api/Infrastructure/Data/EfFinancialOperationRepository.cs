using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
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
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresPageAsync(query, offset, limit, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await Order(query)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new FinancialOperationPageData(items, totalCount);
    }

    private async Task<FinancialOperationPageData> GetPostgresPageAsync(
        IQueryable<FinancialOperation> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = Order(query)
            .Skip(offset)
            .Take(limit)
            .Select(operation => new
            {
                Category = PageCategory,
                Id = (Guid?)operation.Id,
                OperationKind = (string?)operation.OperationKind,
                OperationDate = (DateOnly?)operation.OperationDate,
                AccountingMonth = (DateOnly?)operation.AccountingMonth,
                Amount = (decimal?)operation.Amount,
                operation.ReceiptBatchId,
                operation.DocumentNumber,
                operation.Comment,
                operation.GarageId,
                GarageNumber = operation.Garage == null ? null : operation.Garage.Number,
                OwnerId = operation.Garage == null ? null : operation.Garage.OwnerId,
                OwnerLastName = operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.LastName,
                OwnerFirstName = operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.FirstName,
                OwnerMiddleName = operation.Garage == null || operation.Garage.Owner == null ? null : operation.Garage.Owner.MiddleName,
                operation.IncomeTypeId,
                IncomeTypeName = operation.IncomeType == null ? null : operation.IncomeType.Name,
                operation.SupplierId,
                SupplierName = operation.Supplier == null ? null : operation.Supplier.Name,
                operation.StaffMemberId,
                StaffMemberName = operation.StaffMember == null ? null : operation.StaffMember.FullName,
                StaffDepartmentId = operation.StaffMember == null ? null : (Guid?)operation.StaffMember.DepartmentId,
                StaffDepartmentName = operation.StaffMember == null ? null : operation.StaffMember.Department.Name,
                operation.ExpenseTypeId,
                ExpenseTypeName = operation.ExpenseType == null ? null : operation.ExpenseType.Name,
                IsCanceled = (bool?)operation.IsCanceled,
                CreatedAtUtc = (DateTimeOffset?)operation.CreatedAtUtc,
                UpdatedAtUtc = (DateTimeOffset?)operation.UpdatedAtUtc,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new
            {
                Category = TotalsCategory,
                Id = (Guid?)null,
                OperationKind = (string?)null,
                OperationDate = (DateOnly?)null,
                AccountingMonth = (DateOnly?)null,
                Amount = (decimal?)null,
                ReceiptBatchId = (Guid?)null,
                DocumentNumber = (string?)null,
                Comment = (string?)null,
                GarageId = (Guid?)null,
                GarageNumber = (string?)null,
                OwnerId = (Guid?)null,
                OwnerLastName = (string?)null,
                OwnerFirstName = (string?)null,
                OwnerMiddleName = (string?)null,
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = (string?)null,
                SupplierId = (Guid?)null,
                SupplierName = (string?)null,
                StaffMemberId = (Guid?)null,
                StaffMemberName = (string?)null,
                StaffDepartmentId = (Guid?)null,
                StaffDepartmentName = (string?)null,
                ExpenseTypeId = (Guid?)null,
                ExpenseTypeName = (string?)null,
                IsCanceled = (bool?)null,
                CreatedAtUtc = (DateTimeOffset?)null,
                UpdatedAtUtc = (DateTimeOffset?)null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenByDescending(row => row.OperationDate)
            .ThenBy(row => row.DocumentNumber)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new FinancialOperation
            {
                Id = row.Id!.Value,
                OperationKind = row.OperationKind!,
                OperationDate = row.OperationDate!.Value,
                AccountingMonth = row.AccountingMonth!.Value,
                Amount = row.Amount!.Value,
                ReceiptBatchId = row.ReceiptBatchId,
                DocumentNumber = row.DocumentNumber,
                Comment = row.Comment,
                GarageId = row.GarageId,
                Garage = row.GarageId is null
                    ? null
                    : new Garage
                    {
                        Id = row.GarageId.Value,
                        Number = row.GarageNumber!,
                        OwnerId = row.OwnerId,
                        Owner = row.OwnerId is null
                            ? null
                            : new Owner
                            {
                                Id = row.OwnerId.Value,
                                LastName = row.OwnerLastName!,
                                FirstName = row.OwnerFirstName!,
                                MiddleName = row.OwnerMiddleName
                            }
                    },
                IncomeTypeId = row.IncomeTypeId,
                IncomeType = row.IncomeTypeId is null
                    ? null
                    : new IncomeType { Id = row.IncomeTypeId.Value, Name = row.IncomeTypeName! },
                SupplierId = row.SupplierId,
                Supplier = row.SupplierId is null
                    ? null
                    : new Supplier { Id = row.SupplierId.Value, Name = row.SupplierName! },
                StaffMemberId = row.StaffMemberId,
                StaffMember = row.StaffMemberId is null
                    ? null
                    : new StaffMember
                    {
                        Id = row.StaffMemberId.Value,
                        FullName = row.StaffMemberName!,
                        DepartmentId = row.StaffDepartmentId!.Value,
                        Department = new StaffDepartment
                        {
                            Id = row.StaffDepartmentId.Value,
                            Name = row.StaffDepartmentName!
                        }
                    },
                ExpenseTypeId = row.ExpenseTypeId,
                ExpenseType = row.ExpenseTypeId is null
                    ? null
                    : new ExpenseType { Id = row.ExpenseTypeId.Value, Name = row.ExpenseTypeName! },
                IsCanceled = row.IsCanceled!.Value,
                CreatedAtUtc = row.CreatedAtUtc!.Value,
                UpdatedAtUtc = row.UpdatedAtUtc!.Value
            })
            .ToList();
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

    public Task<bool> ReceiptBatchConflictExistsAsync(
        Guid receiptBatchId,
        Guid garageId,
        DateOnly operationDate,
        CancellationToken cancellationToken) =>
        dbContext.FinancialOperations.AsNoTracking().AnyAsync(operation =>
            operation.ReceiptBatchId == receiptBatchId &&
            (operation.GarageId != garageId || operation.OperationDate != operationDate),
            cancellationToken);

    public async Task<decimal> GetIncomeTotalBeforeMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken) =>
        await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.AccountingMonth < accountingMonth)
            .SumAsync(operation => operation.Amount, cancellationToken);

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

    public async Task<decimal> GetPreviousSupplierExpenseTotalAsync(
        Guid ignoredId,
        Guid supplierId,
        Guid expenseTypeId,
        DateOnly operationDate,
        CancellationToken cancellationToken) =>
        await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.Id != ignoredId &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.SupplierId == supplierId &&
                operation.ExpenseTypeId == expenseTypeId &&
                operation.OperationDate < operationDate)
            .SumAsync(operation => operation.Amount, cancellationToken);

    public Task<DateOnly?> GetPreviousActiveIncomeDateAsync(
        Guid garageId,
        Guid incomeTypeId,
        DateOnly operationDate,
        Guid? ignoredOperationId,
        CancellationToken cancellationToken) =>
        dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.IncomeTypeId == incomeTypeId &&
                operation.OperationDate <= operationDate &&
                (!ignoredOperationId.HasValue || operation.Id != ignoredOperationId.Value))
            .OrderByDescending(operation => operation.OperationDate)
            .ThenByDescending(operation => operation.Id)
            .Select(operation => (DateOnly?)operation.OperationDate)
            .FirstOrDefaultAsync(cancellationToken);

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
