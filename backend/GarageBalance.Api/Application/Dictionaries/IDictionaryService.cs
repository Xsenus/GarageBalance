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

    Task<IReadOnlyList<SupplierGroupDto>> GetSupplierGroupsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<PagedResult<SupplierGroupDto>> GetSupplierGroupsPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false);
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

    Task<IReadOnlyList<SupplierContactDto>> GetSupplierContactsAsync(Guid? supplierId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<DictionaryResult<SupplierContactDto>> CreateSupplierContactAsync(UpsertSupplierContactRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierContactDto>> UpdateSupplierContactAsync(Guid id, UpsertSupplierContactRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierContactDto>> ArchiveSupplierContactAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierContactDto>> RestoreSupplierContactAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StaffDepartmentDto>> GetStaffDepartmentsAsync(CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<DictionaryResult<StaffDepartmentDto>> CreateStaffDepartmentAsync(UpsertStaffDepartmentRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<StaffDepartmentDto>> UpdateStaffDepartmentAsync(Guid id, UpsertStaffDepartmentRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<StaffDepartmentDto>> ArchiveStaffDepartmentAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<StaffDepartmentDto>> RestoreStaffDepartmentAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StaffMemberDto>> GetStaffMembersAsync(Guid? departmentId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<DictionaryResult<StaffMemberDto>> CreateStaffMemberAsync(UpsertStaffMemberRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<StaffMemberDto>> UpdateStaffMemberAsync(Guid id, UpsertStaffMemberRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<StaffMemberDto>> ArchiveStaffMemberAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<StaffMemberDto>> RestoreStaffMemberAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountingTypeDto>> GetIncomeTypesAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<PagedResult<AccountingTypeDto>> GetIncomeTypesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false);
    Task<DictionaryResult<AccountingTypeDto>> CreateIncomeTypeAsync(UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<AccountingTypeDto>> UpdateIncomeTypeAsync(Guid id, UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<AccountingTypeDto>> ArchiveIncomeTypeAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<AccountingTypeDto>> RestoreIncomeTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountingTypeDto>> GetExpenseTypesAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<PagedResult<AccountingTypeDto>> GetExpenseTypesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false);
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

    Task<IReadOnlyList<ChargeServiceSettingDto>> GetChargeServiceSettingsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<DictionaryResult<ChargeServiceSettingDto>> CreateChargeServiceSettingAsync(UpsertChargeServiceSettingRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<ChargeServiceSettingDto>> UpdateChargeServiceSettingAsync(Guid id, UpsertChargeServiceSettingRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<ChargeServiceSettingDto>> ArchiveChargeServiceSettingAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<ChargeServiceSettingDto>> RestoreChargeServiceSettingAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<IrregularPaymentDto>> GetIrregularPaymentsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<DictionaryResult<IrregularPaymentDto>> CreateIrregularPaymentAsync(UpsertIrregularPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<IrregularPaymentDto>> UpdateIrregularPaymentAsync(Guid id, UpsertIrregularPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<IrregularPaymentDto>> SetIrregularPaymentStatusAsync(Guid id, UpdateIrregularPaymentStatusRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<IrregularPaymentDto>> ArchiveIrregularPaymentAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<IrregularPaymentDto>> RestoreIrregularPaymentAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FeeCampaignDto>> GetFeeCampaignsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false);
    Task<DictionaryResult<FeeCampaignDto>> CreateFeeCampaignAsync(UpsertFeeCampaignRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<FeeCampaignDto>> UpdateFeeCampaignAsync(Guid id, UpsertFeeCampaignRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<FeeCampaignDto>> ArchiveFeeCampaignAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<FeeCampaignDto>> RestoreFeeCampaignAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken);
}
