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
        return await IncludeDetails(ApplyFilters(groupId, normalizedSearch, includeArchived))
            .OrderBy(supplier => supplier.Group.Name)
            .ThenBy(supplier => supplier.Name)
            .ThenBy(supplier => supplier.Id)
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
        if (IsNpgsqlProvider())
        {
            return await GetPostgresPageAsync(query, offset, limit, sortBy, sortDescending, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var queryWithDetails = IncludeDetails(query);
        if (IsSqliteProvider() && sortBy is "debt" or "contactPerson" or "phone" or "email")
        {
            var filteredItems = await queryWithDetails.ToListAsync(cancellationToken);
            var primaryContacts = await GetPrimaryContactsAsync(filteredItems.Select(supplier => supplier.Id).ToArray(), cancellationToken);
            var debtTotals = sortBy == "debt"
                ? await GetDebtTotalsAsync(filteredItems.Select(supplier => supplier.Id).ToArray(), cancellationToken)
                : new Dictionary<Guid, decimal>();
            var sortedItems = ApplySqlitePageSorting(filteredItems, primaryContacts, debtTotals, sortBy, sortDescending)
                .Skip(offset)
                .Take(limit)
                .ToList();
            if (sortBy != "debt")
            {
                debtTotals = (await GetDebtTotalsAsync(sortedItems.Select(supplier => supplier.Id).ToArray(), cancellationToken))
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            }

            return CreatePageData(sortedItems, primaryContacts, debtTotals, totalCount);
        }

        var pageRows = await ApplyPageSorting(queryWithDetails, sortBy, sortDescending)
            .ThenBy(supplier => supplier.Id)
            .Skip(offset)
            .Take(limit)
            .Select(supplier => new SupplierPageDebtRow(
                supplier,
                supplier.StartingBalance
                + (dbContext.SupplierAccruals
                    .Where(accrual => accrual.SupplierId == supplier.Id && !accrual.IsCanceled)
                    .Sum(accrual => (decimal?)accrual.Amount) ?? 0m)
                - (dbContext.FinancialOperations
                    .Where(operation =>
                        operation.SupplierId == supplier.Id
                        && !operation.IsCanceled
                        && operation.OperationKind == "expense")
                    .Sum(operation => (decimal?)operation.Amount) ?? 0m)))
            .ToListAsync(cancellationToken);
        var pageContacts = await GetPrimaryContactsAsync(pageRows.Select(row => row.Supplier.Id).ToArray(), cancellationToken);
        return CreatePageData(
            pageRows.Select(row => row.Supplier).ToList(),
            pageContacts,
            pageRows.ToDictionary(row => row.Supplier.Id, row => row.DebtTotal),
            totalCount);
    }

    private async Task<SupplierPageData> GetPostgresPageAsync(
        IQueryable<Supplier> query,
        int offset,
        int limit,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        IQueryable<SupplierListRow> pageRows;
        if (sortBy is "debt" or "contactPerson" or "phone" or "email")
        {
            pageRows = ApplyPostgresSorting(BuildPostgresRows(query, PageCategory), sortBy, sortDescending)
                .ThenBy(row => row.SupplierId)
                .Skip(offset)
                .Take(limit);
        }
        else
        {
            var pageSuppliers = ApplyPageSorting(query, sortBy, sortDescending)
                .ThenBy(supplier => supplier.Id)
                .Skip(offset)
                .Take(limit);
            pageRows = BuildPostgresRows(pageSuppliers, PageCategory);
        }
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new SupplierListRow
            {
                Category = TotalsCategory,
                SupplierId = null,
                Name = null,
                GroupId = null,
                GroupName = null,
                Inn = null,
                LegalAddress = null,
                ContactPerson = null,
                Phone = null,
                Email = null,
                StartingBalance = null,
                Comment = null,
                IsArchived = null,
                Version = null,
                ChargeServiceSettingId = null,
                ChargeServiceSettingName = null,
                ExpenseTypeId = null,
                ExpenseTypeName = null,
                ExpenseFundId = null,
                ExpenseFundName = null,
                ExpenseFundBalance = null,
                PrimaryContactFullName = null,
                PrimaryContactPhone = null,
                PrimaryContactEmail = null,
                DebtTotal = null,
                TotalCount = query.Count()
            });
        var combinedRows = pageRows.Concat(totalsRow);
        var rows = await ApplyPostgresSortingByCategory(combinedRows, sortBy, sortDescending)
            .ThenBy(row => row.SupplierId)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(MaterializePostgresPageItem)
            .ToList();
        return new SupplierPageData(items, totalCount);
    }

    private IQueryable<SupplierListRow> BuildPostgresRows(IQueryable<Supplier> query, int category) =>
        from row in query.Select(supplier => new
        {
            Supplier = supplier,
            PrimaryContactId = dbContext.SupplierContacts
                .Where(contact => contact.SupplierId == supplier.Id && !contact.IsArchived)
                .OrderByDescending(contact => contact.Status == "Работает")
                .ThenBy(contact => contact.FullName)
                .ThenBy(contact => contact.Id)
                .Select(contact => (Guid?)contact.Id)
                .FirstOrDefault()
        })
        join contact in dbContext.SupplierContacts.AsNoTracking()
            on row.PrimaryContactId equals (Guid?)contact.Id into contacts
        from primaryContact in contacts.DefaultIfEmpty()
        let supplier = row.Supplier
        select new SupplierListRow
        {
            Category = category,
            SupplierId = supplier.Id,
            Name = supplier.Name,
            GroupId = supplier.GroupId,
            GroupName = supplier.Group.Name,
            Inn = supplier.Inn,
            LegalAddress = supplier.LegalAddress,
            ContactPerson = supplier.ContactPerson,
            Phone = supplier.Phone,
            Email = supplier.Email,
            StartingBalance = supplier.StartingBalance,
            Comment = supplier.Comment,
            IsArchived = supplier.IsArchived,
            Version = supplier.Version,
            ChargeServiceSettingId = supplier.ChargeServiceSettingId,
            ChargeServiceSettingName = supplier.ChargeServiceSetting == null ? null : supplier.ChargeServiceSetting.Name,
            ExpenseTypeId = supplier.ExpenseTypeId,
            ExpenseTypeName = supplier.ExpenseType == null ? null : supplier.ExpenseType.Name,
            ExpenseFundId = supplier.ExpenseFundId,
            ExpenseFundName = supplier.ExpenseFund == null ? null : supplier.ExpenseFund.Name,
            ExpenseFundBalance = supplier.ExpenseFund == null ? null : supplier.ExpenseFund.Balance,
            PrimaryContactFullName = primaryContact == null ? null : primaryContact.FullName,
            PrimaryContactPhone = primaryContact == null ? null : primaryContact.Phone,
            PrimaryContactEmail = primaryContact == null ? null : primaryContact.Email,
            DebtTotal = supplier.StartingBalance
                + (dbContext.SupplierAccruals
                    .Where(accrual => accrual.SupplierId == supplier.Id && !accrual.IsCanceled)
                    .Sum(accrual => (decimal?)accrual.Amount) ?? 0m)
                - (dbContext.FinancialOperations
                    .Where(operation =>
                        operation.SupplierId == supplier.Id
                        && !operation.IsCanceled
                        && operation.OperationKind == "expense")
                    .Sum(operation => (decimal?)operation.Amount) ?? 0m),
            TotalCount = 0
        };

    private static IOrderedQueryable<SupplierListRow> ApplyPostgresSorting(
        IQueryable<SupplierListRow> query,
        string sortBy,
        bool descending) =>
        (sortBy, descending) switch
        {
            ("name", true) => query.OrderByDescending(row => row.Name),
            ("name", false) => query.OrderBy(row => row.Name),
            ("debt", true) => query.OrderByDescending(row => row.DebtTotal),
            ("debt", false) => query.OrderBy(row => row.DebtTotal),
            ("contactPerson", true) => query.OrderByDescending(row => row.PrimaryContactFullName),
            ("contactPerson", false) => query.OrderBy(row => row.PrimaryContactFullName),
            ("phone", true) => query.OrderByDescending(row => row.PrimaryContactPhone),
            ("phone", false) => query.OrderBy(row => row.PrimaryContactPhone),
            ("email", true) => query.OrderByDescending(row => row.PrimaryContactEmail),
            ("email", false) => query.OrderBy(row => row.PrimaryContactEmail),
            (_, true) => query.OrderByDescending(row => row.ChargeServiceSettingName ?? row.GroupName),
            _ => query.OrderBy(row => row.ChargeServiceSettingName ?? row.GroupName)
        };

    private static IOrderedQueryable<SupplierListRow> ApplyPostgresSortingByCategory(
        IQueryable<SupplierListRow> query,
        string sortBy,
        bool descending) =>
        (sortBy, descending) switch
        {
            ("name", true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.Name),
            ("name", false) => query.OrderBy(row => row.Category).ThenBy(row => row.Name),
            ("debt", true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.DebtTotal),
            ("debt", false) => query.OrderBy(row => row.Category).ThenBy(row => row.DebtTotal),
            ("contactPerson", true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.PrimaryContactFullName),
            ("contactPerson", false) => query.OrderBy(row => row.Category).ThenBy(row => row.PrimaryContactFullName),
            ("phone", true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.PrimaryContactPhone),
            ("phone", false) => query.OrderBy(row => row.Category).ThenBy(row => row.PrimaryContactPhone),
            ("email", true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.PrimaryContactEmail),
            ("email", false) => query.OrderBy(row => row.Category).ThenBy(row => row.PrimaryContactEmail),
            (_, true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.ChargeServiceSettingName ?? row.GroupName),
            _ => query.OrderBy(row => row.Category).ThenBy(row => row.ChargeServiceSettingName ?? row.GroupName)
        };

    private static SupplierPageItem MaterializePostgresPageItem(SupplierListRow row)
    {
        var supplier = new Supplier
        {
            Id = row.SupplierId!.Value,
            Name = row.Name!,
            GroupId = row.GroupId!.Value,
            Group = new SupplierGroup { Id = row.GroupId.Value, Name = row.GroupName! },
            Inn = row.Inn,
            LegalAddress = row.LegalAddress,
            ContactPerson = row.ContactPerson,
            Phone = row.Phone,
            Email = row.Email,
            StartingBalance = row.StartingBalance!.Value,
            Comment = row.Comment,
            IsArchived = row.IsArchived!.Value,
            Version = row.Version!.Value,
            ChargeServiceSettingId = row.ChargeServiceSettingId,
            ChargeServiceSetting = row.ChargeServiceSettingId is null
                ? null
                : new ChargeServiceSetting { Id = row.ChargeServiceSettingId.Value, Name = row.ChargeServiceSettingName! },
            ExpenseTypeId = row.ExpenseTypeId,
            ExpenseType = row.ExpenseTypeId is null
                ? null
                : new ExpenseType { Id = row.ExpenseTypeId.Value, Name = row.ExpenseTypeName! },
            ExpenseFundId = row.ExpenseFundId,
            ExpenseFund = row.ExpenseFundId is null
                ? null
                : new Fund
                {
                    Id = row.ExpenseFundId.Value,
                    Name = row.ExpenseFundName!,
                    NormalizedName = row.ExpenseFundName!,
                    Balance = row.ExpenseFundBalance!.Value
                }
        };
        var primaryContact = row.PrimaryContactFullName is null
            ? null
            : new SupplierPrimaryContactData(
                row.PrimaryContactFullName,
                row.PrimaryContactPhone,
                row.PrimaryContactEmail);
        return new SupplierPageItem(supplier, primaryContact, row.DebtTotal!.Value);
    }

    public Task<Supplier?> FindActiveWithGroupAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Suppliers
            .Include(supplier => supplier.Group)
            .Include(supplier => supplier.ExpenseType)
            .Include(supplier => supplier.ExpenseFund)
            .Include(supplier => supplier.ChargeServiceSetting)
            .SingleOrDefaultAsync(supplier => supplier.Id == id && !supplier.IsArchived, cancellationToken);
    }

    public Task<Supplier?> FindArchivedWithGroupAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Suppliers
            .Include(supplier => supplier.Group)
            .Include(supplier => supplier.ExpenseType)
            .Include(supplier => supplier.ExpenseFund)
            .Include(supplier => supplier.ChargeServiceSetting)
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

    public Task<bool> HasFinancialHistoryAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.Id == id)
            .Select(supplier =>
                dbContext.SupplierAccruals.Any(accrual => accrual.SupplierId == supplier.Id)
                || dbContext.FinancialOperations.Any(operation => operation.SupplierId == supplier.Id))
            .SingleAsync(cancellationToken);

    public async Task<SupplierOpeningBalanceData?> GetOpeningBalanceAsync(
        Guid id,
        DateOnly monthFrom,
        CancellationToken cancellationToken)
    {
        var startingBalanceQuery = dbContext.Suppliers.AsNoTracking()
            .Where(supplier => supplier.Id == id)
            .Select(supplier => new
            {
                Category = StartingBalanceDebtCategory,
                Amount = supplier.StartingBalance
            });
        var accrualQuery = dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual => accrual.SupplierId == id && !accrual.IsCanceled && accrual.AccountingMonth < monthFrom)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = AccrualDebtCategory,
                Amount = group.Sum(item => item.Amount)
            });
        var paymentQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                operation.SupplierId == id &&
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.AccountingMonth < monthFrom)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = PaymentDebtCategory,
                Amount = group.Sum(item => item.Amount)
            });
        var rows = await startingBalanceQuery
            .Concat(accrualQuery)
            .Concat(paymentQuery)
            .ToListAsync(cancellationToken);
        var startingBalance = rows.SingleOrDefault(row => row.Category == StartingBalanceDebtCategory);
        if (startingBalance is null)
        {
            return null;
        }

        return new SupplierOpeningBalanceData(
            startingBalance.Amount,
            rows.SingleOrDefault(row => row.Category == AccrualDebtCategory)?.Amount ?? 0m,
            rows.SingleOrDefault(row => row.Category == PaymentDebtCategory)?.Amount ?? 0m);
    }

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

    public Task<bool> HasActiveContactsAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.SupplierContacts.AsNoTracking()
            .AnyAsync(contact => contact.SupplierId == id && !contact.IsArchived, cancellationToken);

    public Task<bool> HasActiveServiceAssignmentsAsync(Guid chargeServiceSettingId, CancellationToken cancellationToken) =>
        dbContext.Suppliers.AsNoTracking()
            .AnyAsync(
                supplier => supplier.ChargeServiceSettingId == chargeServiceSettingId && !supplier.IsArchived,
                cancellationToken);

    public void Add(Supplier supplier)
    {
        dbContext.Suppliers.Add(supplier);
    }

    private IQueryable<Supplier> ApplyFilters(Guid? groupId, string? normalizedSearch, bool includeArchived)
    {
        var query = dbContext.Suppliers.AsNoTracking()
            .Where(supplier => includeArchived || !supplier.IsArchived);
        if (groupId is not null)
        {
            query = query.Where(supplier => supplier.GroupId == groupId);
        }

        if (normalizedSearch is not null)
        {
            if (IsNpgsqlProvider())
            {
                var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
                query = query.Where(supplier =>
                    EF.Functions.ILike(supplier.Name, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\") ||
                    EF.Functions.ILike(supplier.Group.Name, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\") ||
                    (supplier.ChargeServiceSetting != null && EF.Functions.ILike(supplier.ChargeServiceSetting.Name, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\")) ||
                    (supplier.Inn != null && EF.Functions.ILike(supplier.Inn, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\")) ||
                    (supplier.ContactPerson != null && EF.Functions.ILike(supplier.ContactPerson, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\")));
            }
            else
            {
                query = query.Where(supplier =>
                    supplier.Name.ToLower().Contains(normalizedSearch) ||
                    supplier.Group.Name.ToLower().Contains(normalizedSearch) ||
                    (supplier.ChargeServiceSetting != null && supplier.ChargeServiceSetting.Name.ToLower().Contains(normalizedSearch)) ||
                    (supplier.Inn != null && supplier.Inn.ToLower().Contains(normalizedSearch)) ||
                    (supplier.ContactPerson != null && supplier.ContactPerson.ToLower().Contains(normalizedSearch)));
            }
        }

        return query;
    }

    private static IQueryable<Supplier> IncludeDetails(IQueryable<Supplier> query) =>
        query.Include(supplier => supplier.Group)
            .Include(supplier => supplier.ExpenseType)
            .Include(supplier => supplier.ExpenseFund)
            .Include(supplier => supplier.ChargeServiceSetting);

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
        IReadOnlyDictionary<Guid, decimal> debtTotals,
        int totalCount) =>
        new(
            suppliers.Select(supplier => new SupplierPageItem(
                supplier,
                primaryContacts.GetValueOrDefault(supplier.Id),
                debtTotals.GetValueOrDefault(supplier.Id, supplier.StartingBalance))).ToList(),
            totalCount);

    private sealed record SupplierPageDebtRow(Supplier Supplier, decimal DebtTotal);

    private sealed class SupplierListRow
    {
        public int Category { get; init; }
        public Guid? SupplierId { get; init; }
        public string? Name { get; init; }
        public Guid? GroupId { get; init; }
        public string? GroupName { get; init; }
        public string? Inn { get; init; }
        public string? LegalAddress { get; init; }
        public string? ContactPerson { get; init; }
        public string? Phone { get; init; }
        public string? Email { get; init; }
        public decimal? StartingBalance { get; init; }
        public string? Comment { get; init; }
        public bool? IsArchived { get; init; }
        public Guid? Version { get; init; }
        public Guid? ChargeServiceSettingId { get; init; }
        public string? ChargeServiceSettingName { get; init; }
        public Guid? ExpenseTypeId { get; init; }
        public string? ExpenseTypeName { get; init; }
        public Guid? ExpenseFundId { get; init; }
        public string? ExpenseFundName { get; init; }
        public decimal? ExpenseFundBalance { get; init; }
        public string? PrimaryContactFullName { get; init; }
        public string? PrimaryContactPhone { get; init; }
        public string? PrimaryContactEmail { get; init; }
        public decimal? DebtTotal { get; init; }
        public int TotalCount { get; init; }
    }

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsNpgsqlProvider() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
}
