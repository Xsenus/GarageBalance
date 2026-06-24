namespace GarageBalance.Api.Application.Dictionaries;

public interface IDictionaryService
{
    Task<IReadOnlyList<OwnerDto>> GetOwnersAsync(string? search, CancellationToken cancellationToken, int? limit = null);
    Task<DictionaryResult<OwnerDto>> CreateOwnerAsync(UpsertOwnerRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<OwnerDto>> UpdateOwnerAsync(Guid id, UpsertOwnerRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<OwnerDto>> ArchiveOwnerAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GarageDto>> GetGaragesAsync(string? search, CancellationToken cancellationToken, int? limit = null);
    Task<DictionaryResult<GarageDto>> CreateGarageAsync(UpsertGarageRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<GarageDto>> UpdateGarageAsync(Guid id, UpsertGarageRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<GarageDto>> ArchiveGarageAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SupplierGroupDto>> GetSupplierGroupsAsync(CancellationToken cancellationToken, int? limit = null);
    Task<DictionaryResult<SupplierGroupDto>> CreateSupplierGroupAsync(UpsertSupplierGroupRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierGroupDto>> ArchiveSupplierGroupAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SupplierDto>> GetSuppliersAsync(Guid? groupId, string? search, CancellationToken cancellationToken, int? limit = null);
    Task<DictionaryResult<SupplierDto>> CreateSupplierAsync(UpsertSupplierRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierDto>> UpdateSupplierAsync(Guid id, UpsertSupplierRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierDto>> ArchiveSupplierAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountingTypeDto>> GetIncomeTypesAsync(CancellationToken cancellationToken, int? limit = null);
    Task<DictionaryResult<AccountingTypeDto>> CreateIncomeTypeAsync(UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<AccountingTypeDto>> ArchiveIncomeTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountingTypeDto>> GetExpenseTypesAsync(CancellationToken cancellationToken, int? limit = null);
    Task<DictionaryResult<AccountingTypeDto>> CreateExpenseTypeAsync(UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<AccountingTypeDto>> ArchiveExpenseTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TariffDto>> GetTariffsAsync(string? search, CancellationToken cancellationToken, int? limit = null);
    Task<DictionaryResult<TariffDto>> CreateTariffAsync(UpsertTariffRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<TariffDto>> UpdateTariffAsync(Guid id, UpsertTariffRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<TariffDto>> ArchiveTariffAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);
}
