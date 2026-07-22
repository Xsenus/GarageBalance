using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface ISupplierRepository
{
    Task<IReadOnlyList<Supplier>> GetListAsync(Guid? groupId, string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<SupplierPageData> GetPageAsync(Guid? groupId, string? normalizedSearch, bool includeArchived, int offset, int limit, string sortBy, bool sortDescending, CancellationToken cancellationToken);
    Task<Supplier?> FindActiveWithGroupAsync(Guid id, CancellationToken cancellationToken);
    Task<Supplier?> FindArchivedWithGroupAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Supplier>> GetActiveByGroupAsync(Guid groupId, CancellationToken cancellationToken);
    Task<decimal> GetStartingBalanceAsync(Guid id, CancellationToken cancellationToken);
    Task<SupplierOpeningBalanceData?> GetOpeningBalanceAsync(Guid id, DateOnly monthFrom, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, decimal>> GetDebtTotalsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, Guid groupId, string name, CancellationToken cancellationToken);
    void Add(Supplier supplier);
}

public sealed record SupplierPageData(IReadOnlyList<SupplierPageItem> Items, int TotalCount);

public sealed record SupplierPageItem(Supplier Supplier, SupplierPrimaryContactData? PrimaryContact);

public sealed record SupplierPrimaryContactData(string FullName, string? Phone, string? Email);

public sealed record SupplierOpeningBalanceData(
    decimal StartingBalance,
    decimal PriorAccrualTotal,
    decimal PriorPaymentTotal);
