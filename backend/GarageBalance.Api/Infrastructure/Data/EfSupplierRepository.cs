using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfSupplierRepository(GarageBalanceDbContext dbContext) : ISupplierRepository
{
    private const int StartingBalanceDebtCategory = 1;
    private const int AccrualDebtCategory = 2;
    private const int PaymentDebtCategory = 3;

    public async Task<IReadOnlyList<Supplier>> GetListAsync(
        Guid? groupId,
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        return await ApplyFilters(groupId, normalizedSearch, includeArchived)
            .OrderBy(supplier => supplier.Group.Name)
            .ThenBy(supplier => supplier.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<SupplierPageData> GetPageAsync(
        Guid? groupId,
        string? normalizedSearch,
        bool includeArchived,
        int offset,
        int limit,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(groupId, normalizedSearch, includeArchived);
        var totalCount = await query.CountAsync(cancellationToken);
        if (IsSqliteProvider() && sortBy is "debt" or "contactPerson" or "phone" or "email")
        {
            var filteredItems = await query.ToListAsync(cancellationToken);
            var primaryContacts = await GetPrimaryContactsAsync(filteredItems.Select(supplier => supplier.Id).ToArray(), cancellationToken);
            var debtTotals = sortBy == "debt"
                ? await GetDebtTotalsAsync(filteredItems.Select(supplier => supplier.Id).ToArray(), cancellationToken)
                : new Dictionary<Guid, decimal>();
            var sortedItems = ApplySqlitePageSorting(filteredItems, primaryContacts, debtTotals, sortBy, sortDescending)
                .Skip(offset)
                .Take(limit)
                .ToList();
            return CreatePageData(sortedItems, primaryContacts, totalCount);
        }

        var items = await ApplyPageSorting(query, sortBy, sortDescending)
            .ThenBy(supplier => supplier.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        var pageContacts = await GetPrimaryContactsAsync(items.Select(supplier => supplier.Id).ToArray(), cancellationToken);
        return CreatePageData(items, pageContacts, totalCount);
    }

    public Task<Supplier?> FindActiveWithGroupAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Suppliers.Include(supplier => supplier.Group).Include(supplier => supplier.ChargeServiceSetting)
            .SingleOrDefaultAsync(supplier => supplier.Id == id && !supplier.IsArchived, cancellationToken);
    }

    public Task<Supplier?> FindArchivedWithGroupAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Suppliers.Include(supplier => supplier.Group).Include(supplier => supplier.ChargeServiceSetting)
            .SingleOrDefaultAsync(supplier => supplier.Id == id && supplier.IsArchived, cancellationToken);
    }

    public async Task<IReadOnlyList<Supplier>> GetActiveByGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        await dbContext.Suppliers
            .Where(supplier => !supplier.IsArchived && supplier.GroupId == groupId)
            .OrderBy(supplier => supplier.Name)
            .ToListAsync(cancellationToken);

    public Task<decimal> GetStartingBalanceAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Suppliers.AsNoTracking()
            .Where(supplier => supplier.Id == id)
            .Select(supplier => supplier.StartingBalance)
            .SingleAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetDebtTotalsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var startingBalanceQuery = dbContext.Suppliers.AsNoTracking()
            .Where(supplier => ids.Contains(supplier.Id))
            .Select(supplier => new
            {
                Category = StartingBalanceDebtCategory,
                SupplierId = supplier.Id,
                Amount = supplier.StartingBalance
            });
        var accrualQuery = dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual => ids.Contains(accrual.SupplierId) && !accrual.IsCanceled)
            .GroupBy(accrual => accrual.SupplierId)
            .Select(group => new
            {
                Category = AccrualDebtCategory,
                SupplierId = group.Key,
                Amount = group.Sum(item => item.Amount)
            });
        var paymentQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => operation.SupplierId != null && ids.Contains(operation.SupplierId.Value) && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Expense)
            .GroupBy(operation => operation.SupplierId!.Value)
            .Select(group => new
            {
                Category = PaymentDebtCategory,
                SupplierId = group.Key,
                Amount = group.Sum(item => item.Amount)
            });
        var rows = await startingBalanceQuery
            .Concat(accrualQuery)
            .Concat(paymentQuery)
            .ToListAsync(cancellationToken);
        var startingBalances = rows
            .Where(row => row.Category == StartingBalanceDebtCategory)
            .ToDictionary(row => row.SupplierId, row => row.Amount);
        var accruals = rows
            .Where(row => row.Category == AccrualDebtCategory)
            .ToDictionary(row => row.SupplierId, row => row.Amount);
        var payments = rows
            .Where(row => row.Category == PaymentDebtCategory)
            .ToDictionary(row => row.SupplierId, row => row.Amount);

        return startingBalances.ToDictionary(
            item => item.Key,
            item => item.Value + accruals.GetValueOrDefault(item.Key) - payments.GetValueOrDefault(item.Key));
    }

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, Guid groupId, string name, CancellationToken cancellationToken)
    {
        return dbContext.Suppliers.AsNoTracking().AnyAsync(
            supplier =>
                supplier.GroupId == groupId &&
                !supplier.IsArchived &&
                supplier.Name == name &&
                (!ignoredId.HasValue || supplier.Id != ignoredId.Value),
            cancellationToken);
    }

    public void Add(Supplier supplier)
    {
        dbContext.Suppliers.Add(supplier);
    }

    private IQueryable<Supplier> ApplyFilters(Guid? groupId, string? normalizedSearch, bool includeArchived)
    {
        var query = dbContext.Suppliers.AsNoTracking()
            .Include(supplier => supplier.Group)
            .Include(supplier => supplier.ChargeServiceSetting)
            .Where(supplier => includeArchived || !supplier.IsArchived);
        if (groupId is not null)
        {
            query = query.Where(supplier => supplier.GroupId == groupId);
        }

        if (normalizedSearch is not null)
        {
            query = query.Where(supplier =>
                supplier.Name.ToLower().Contains(normalizedSearch) ||
                (supplier.ChargeServiceSetting != null && supplier.ChargeServiceSetting.Name.ToLower().Contains(normalizedSearch)) ||
                (supplier.Inn != null && supplier.Inn.ToLower().Contains(normalizedSearch)) ||
                (supplier.ContactPerson != null && supplier.ContactPerson.ToLower().Contains(normalizedSearch)));
        }

        return query;
    }

    private IOrderedQueryable<Supplier> ApplyPageSorting(IQueryable<Supplier> query, string sortBy, bool descending)
    {
        return (sortBy, descending) switch
        {
            ("name", true) => query.OrderByDescending(supplier => supplier.Name),
            ("name", false) => query.OrderBy(supplier => supplier.Name),
            ("debt", true) => query.OrderByDescending(supplier =>
                supplier.StartingBalance
                + (dbContext.SupplierAccruals.Where(accrual => accrual.SupplierId == supplier.Id && !accrual.IsCanceled).Sum(accrual => (decimal?)accrual.Amount) ?? 0m)
                - (dbContext.FinancialOperations.Where(operation => operation.SupplierId == supplier.Id && !operation.IsCanceled && operation.OperationKind == "expense").Sum(operation => (decimal?)operation.Amount) ?? 0m)),
            ("debt", false) => query.OrderBy(supplier =>
                supplier.StartingBalance
                + (dbContext.SupplierAccruals.Where(accrual => accrual.SupplierId == supplier.Id && !accrual.IsCanceled).Sum(accrual => (decimal?)accrual.Amount) ?? 0m)
                - (dbContext.FinancialOperations.Where(operation => operation.SupplierId == supplier.Id && !operation.IsCanceled && operation.OperationKind == "expense").Sum(operation => (decimal?)operation.Amount) ?? 0m)),
            ("contactPerson", true) => query.OrderByDescending(supplier => dbContext.SupplierContacts
                .Where(contact => contact.SupplierId == supplier.Id && !contact.IsArchived)
                .OrderByDescending(contact => contact.Status == "Работает")
                .ThenBy(contact => contact.FullName)
                .Select(contact => contact.FullName)
                .FirstOrDefault()),
            ("contactPerson", false) => query.OrderBy(supplier => dbContext.SupplierContacts
                .Where(contact => contact.SupplierId == supplier.Id && !contact.IsArchived)
                .OrderByDescending(contact => contact.Status == "Работает")
                .ThenBy(contact => contact.FullName)
                .Select(contact => contact.FullName)
                .FirstOrDefault()),
            ("phone", true) => query.OrderByDescending(supplier => dbContext.SupplierContacts
                .Where(contact => contact.SupplierId == supplier.Id && !contact.IsArchived)
                .OrderByDescending(contact => contact.Status == "Работает")
                .ThenBy(contact => contact.FullName)
                .Select(contact => contact.Phone)
                .FirstOrDefault()),
            ("phone", false) => query.OrderBy(supplier => dbContext.SupplierContacts
                .Where(contact => contact.SupplierId == supplier.Id && !contact.IsArchived)
                .OrderByDescending(contact => contact.Status == "Работает")
                .ThenBy(contact => contact.FullName)
                .Select(contact => contact.Phone)
                .FirstOrDefault()),
            ("email", true) => query.OrderByDescending(supplier => dbContext.SupplierContacts
                .Where(contact => contact.SupplierId == supplier.Id && !contact.IsArchived)
                .OrderByDescending(contact => contact.Status == "Работает")
                .ThenBy(contact => contact.FullName)
                .Select(contact => contact.Email)
                .FirstOrDefault()),
            ("email", false) => query.OrderBy(supplier => dbContext.SupplierContacts
                .Where(contact => contact.SupplierId == supplier.Id && !contact.IsArchived)
                .OrderByDescending(contact => contact.Status == "Работает")
                .ThenBy(contact => contact.FullName)
                .Select(contact => contact.Email)
                .FirstOrDefault()),
            (_, true) => query.OrderByDescending(supplier => supplier.ChargeServiceSetting != null ? supplier.ChargeServiceSetting.Name : supplier.Group.Name),
            _ => query.OrderBy(supplier => supplier.ChargeServiceSetting != null ? supplier.ChargeServiceSetting.Name : supplier.Group.Name)
        };
    }

    private async Task<IReadOnlyDictionary<Guid, SupplierPrimaryContactData>> GetPrimaryContactsAsync(IReadOnlyCollection<Guid> supplierIds, CancellationToken cancellationToken)
    {
        if (supplierIds.Count == 0)
        {
            return new Dictionary<Guid, SupplierPrimaryContactData>();
        }

        var contacts = await dbContext.SupplierContacts.AsNoTracking()
            .Where(contact => supplierIds.Contains(contact.SupplierId) && !contact.IsArchived)
            .GroupBy(contact => contact.SupplierId)
            .Select(group => group
                .OrderByDescending(contact => contact.Status == "Работает")
                .ThenBy(contact => contact.FullName)
                .ThenBy(contact => contact.Id)
                .Select(contact => new
                {
                    contact.SupplierId,
                    contact.FullName,
                    contact.Phone,
                    contact.Email
                })
                .First())
            .ToListAsync(cancellationToken);
        return contacts
            .ToDictionary(
                contact => contact.SupplierId,
                contact => new SupplierPrimaryContactData(contact.FullName, contact.Phone, contact.Email));
    }

    private static IOrderedEnumerable<Supplier> ApplySqlitePageSorting(
        IReadOnlyList<Supplier> suppliers,
        IReadOnlyDictionary<Guid, SupplierPrimaryContactData> primaryContacts,
        IReadOnlyDictionary<Guid, decimal> debtTotals,
        string sortBy,
        bool descending)
    {
        string? ContactValue(Supplier supplier) => primaryContacts.GetValueOrDefault(supplier.Id) is { } contact
            ? sortBy switch { "phone" => contact.Phone, "email" => contact.Email, _ => contact.FullName }
            : null;

        if (sortBy == "debt")
        {
            return descending
                ? suppliers.OrderByDescending(supplier => debtTotals.GetValueOrDefault(supplier.Id, supplier.StartingBalance)).ThenBy(supplier => supplier.Id)
                : suppliers.OrderBy(supplier => debtTotals.GetValueOrDefault(supplier.Id, supplier.StartingBalance)).ThenBy(supplier => supplier.Id);
        }

        return descending
            ? suppliers.OrderByDescending(ContactValue).ThenBy(supplier => supplier.Id)
            : suppliers.OrderBy(ContactValue).ThenBy(supplier => supplier.Id);
    }

    private static SupplierPageData CreatePageData(
        IReadOnlyList<Supplier> suppliers,
        IReadOnlyDictionary<Guid, SupplierPrimaryContactData> primaryContacts,
        int totalCount) =>
        new(suppliers.Select(supplier => new SupplierPageItem(supplier, primaryContacts.GetValueOrDefault(supplier.Id))).ToList(), totalCount);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
