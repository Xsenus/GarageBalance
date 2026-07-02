namespace GarageBalance.Api.Application.Dictionaries;

public interface IDictionaryService
{
    Task<IReadOnlyList<OwnerDto>> GetOwnersAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<PagedResult<OwnerDto>> GetOwnersPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false);
    Task<DictionaryResult<OwnerDto>> CreateOwnerAsync(UpsertOwnerRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<OwnerDto>> UpdateOwnerAsync(Guid id, UpsertOwnerRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<OwnerDto>> ArchiveOwnerAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<OwnerDto>> RestoreOwnerAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GarageDto>> GetGaragesAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<PagedResult<GarageDto>> GetGaragesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false);
    Task<DictionaryResult<GarageDto>> CreateGarageAsync(UpsertGarageRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<GarageDto>> UpdateGarageAsync(Guid id, UpsertGarageRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<GarageDto>> ArchiveGarageAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<GarageDto>> RestoreGarageAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SupplierGroupDto>> GetSupplierGroupsAsync(CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<PagedResult<SupplierGroupDto>> GetSupplierGroupsPageAsync(int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false);
    Task<DictionaryResult<SupplierGroupDto>> CreateSupplierGroupAsync(UpsertSupplierGroupRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierGroupDto>> UpdateSupplierGroupAsync(Guid id, UpsertSupplierGroupRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierGroupDto>> ArchiveSupplierGroupAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierGroupDto>> RestoreSupplierGroupAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SupplierDto>> GetSuppliersAsync(Guid? groupId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<PagedResult<SupplierDto>> GetSuppliersPageAsync(Guid? groupId, string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false);
    Task<DictionaryResult<SupplierDto>> CreateSupplierAsync(UpsertSupplierRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierDto>> UpdateSupplierAsync(Guid id, UpsertSupplierRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierDto>> ArchiveSupplierAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierDto>> RestoreSupplierAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountingTypeDto>> GetIncomeTypesAsync(CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<PagedResult<AccountingTypeDto>> GetIncomeTypesPageAsync(int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false);
    Task<DictionaryResult<AccountingTypeDto>> CreateIncomeTypeAsync(UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<AccountingTypeDto>> UpdateIncomeTypeAsync(Guid id, UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<AccountingTypeDto>> ArchiveIncomeTypeAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<AccountingTypeDto>> RestoreIncomeTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountingTypeDto>> GetExpenseTypesAsync(CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<PagedResult<AccountingTypeDto>> GetExpenseTypesPageAsync(int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false);
    Task<DictionaryResult<AccountingTypeDto>> CreateExpenseTypeAsync(UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<AccountingTypeDto>> UpdateExpenseTypeAsync(Guid id, UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<AccountingTypeDto>> ArchiveExpenseTypeAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<AccountingTypeDto>> RestoreExpenseTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TariffDto>> GetTariffsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<PagedResult<TariffDto>> GetTariffsPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false);
    Task<DictionaryResult<TariffDto>> CreateTariffAsync(UpsertTariffRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<TariffDto>> UpdateTariffAsync(Guid id, UpsertTariffRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<TariffDto>> ArchiveTariffAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<TariffDto>> RestoreTariffAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);
}
