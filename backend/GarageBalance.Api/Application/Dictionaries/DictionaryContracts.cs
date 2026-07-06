using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Offset, int Limit);

public sealed record OwnerDto(
    Guid Id,
    string LastName,
    string FirstName,
    string? MiddleName,
    string FullName,
    string? Phone,
    string? Address,
    string? MeterNotes,
    bool IsArchived)
{
    public IReadOnlyList<string> GarageNumbers { get; init; } = [];
}

public sealed record UpsertOwnerRequest(
    [Required, MaxLength(120)] string LastName,
    [Required, MaxLength(120)] string FirstName,
    [MaxLength(120)] string? MiddleName,
    [MaxLength(80)] string? Phone,
    [MaxLength(500)] string? Address,
    [MaxLength(1000)] string? MeterNotes);

public sealed record ArchiveDictionaryEntryRequest(
    [Required, MaxLength(1000)] string Reason);

public sealed record GarageDto(
    Guid Id,
    string Number,
    int PeopleCount,
    int FloorCount,
    Guid? OwnerId,
    string? OwnerName,
    decimal StartingBalance,
    decimal? InitialWaterMeterValue,
    decimal? InitialElectricityMeterValue,
    string? Comment,
    bool IsArchived);

public sealed record UpsertGarageRequest(
    [Required, MaxLength(80)] string Number,
    [Range(0, 1000)] int PeopleCount,
    [Range(0, 100)] int FloorCount,
    Guid? OwnerId,
    decimal StartingBalance,
    [Range(0, 999999999)] decimal? InitialWaterMeterValue,
    [Range(0, 999999999)] decimal? InitialElectricityMeterValue,
    [MaxLength(1000)] string? Comment);

public sealed record SupplierGroupDto(Guid Id, string Name, bool IsSystem, bool IsArchived);

public sealed record UpsertSupplierGroupRequest([Required, MaxLength(200)] string Name);

public sealed record SupplierDto(
    Guid Id,
    string Name,
    Guid GroupId,
    string GroupName,
    string? Inn,
    string? LegalAddress,
    string? ContactPerson,
    string? Phone,
    string? Email,
    decimal StartingBalance,
    string? Comment,
    bool IsArchived);

public sealed record UpsertSupplierRequest(
    [Required, MaxLength(240)] string Name,
    Guid GroupId,
    [MaxLength(20)] string? Inn,
    [MaxLength(500)] string? LegalAddress,
    [MaxLength(200)] string? ContactPerson,
    [MaxLength(80)] string? Phone,
    [EmailAddress, MaxLength(320)] string? Email,
    decimal StartingBalance,
    [MaxLength(1000)] string? Comment);

public sealed record SupplierContactDto(
    Guid Id,
    Guid SupplierId,
    string SupplierName,
    string FullName,
    string? Position,
    string? Phone,
    string? Email,
    string Status,
    string? Comment,
    bool IsArchived);

public sealed record UpsertSupplierContactRequest(
    Guid SupplierId,
    [Required, MaxLength(200)] string FullName,
    [MaxLength(160)] string? Position,
    [MaxLength(80)] string? Phone,
    [EmailAddress, MaxLength(320)] string? Email,
    [Required, MaxLength(40)] string Status,
    [MaxLength(1000)] string? Comment);

public sealed record StaffDepartmentDto(Guid Id, string Name, bool IsArchived);

public sealed record UpsertStaffDepartmentRequest([Required, MaxLength(200)] string Name);

public sealed record StaffMemberDto(
    Guid Id,
    string FullName,
    Guid DepartmentId,
    string DepartmentName,
    decimal Rate,
    bool IsArchived);

public sealed record UpsertStaffMemberRequest(
    [Required, MaxLength(200)] string FullName,
    Guid DepartmentId,
    [Range(0, 999999999)] decimal Rate);

public sealed record AccountingTypeDto(Guid Id, string Name, string? Code, bool IsSystem, bool IsArchived);

public sealed record UpsertAccountingTypeRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(80)] string? Code);

public sealed record TariffDto(
    Guid Id,
    string Name,
    string CalculationBase,
    decimal Rate,
    DateOnly EffectiveFrom,
    string? Comment,
    bool IsArchived,
    decimal? ElectricityFirstThreshold = null,
    decimal? ElectricitySecondThreshold = null,
    string? ElectricityFirstTierName = null,
    string? ElectricitySecondTierName = null,
    string? ElectricityThirdTierName = null,
    decimal? ElectricityFirstRate = null,
    decimal? ElectricitySecondRate = null,
    decimal? ElectricityThirdRate = null);

public sealed record UpsertTariffRequest(
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(80)] string CalculationBase,
    [Range(0.0001, 999999999)] decimal Rate,
    DateOnly EffectiveFrom,
    [MaxLength(1000)] string? Comment,
    [Range(0.0001, 999999999)] decimal? ElectricityFirstThreshold = null,
    [Range(0.0001, 999999999)] decimal? ElectricitySecondThreshold = null,
    [MaxLength(120)] string? ElectricityFirstTierName = null,
    [MaxLength(120)] string? ElectricitySecondTierName = null,
    [MaxLength(120)] string? ElectricityThirdTierName = null,
    [Range(0.0001, 999999999)] decimal? ElectricityFirstRate = null,
    [Range(0.0001, 999999999)] decimal? ElectricitySecondRate = null,
    [Range(0.0001, 999999999)] decimal? ElectricityThirdRate = null);

public sealed record ChargeServiceSettingDto(
    Guid Id,
    string Name,
    bool IsRegular,
    int? PeriodicityMonths,
    int? AccrualStartMonth,
    int? PaymentDueDay,
    int? PaymentDueMonth,
    int OverdueGraceDays,
    bool IsMetered,
    bool HasTieredTariff,
    string? UnitName,
    bool IsArchived);

public sealed record UpsertChargeServiceSettingRequest(
    [Required, MaxLength(200)] string Name,
    bool IsRegular,
    [Range(1, 120)] int? PeriodicityMonths,
    [Range(1, 12)] int? AccrualStartMonth,
    [Range(1, 31)] int? PaymentDueDay,
    [Range(1, 12)] int? PaymentDueMonth,
    [Range(0, 366)] int OverdueGraceDays,
    bool IsMetered,
    bool HasTieredTariff,
    [MaxLength(40)] string? UnitName);

public sealed record IrregularPaymentDto(
    Guid Id,
    string Name,
    decimal Amount,
    bool IsActive,
    bool IsArchived,
    bool IsUsed);

public sealed record UpsertIrregularPaymentRequest(
    [Required, MaxLength(200)] string Name,
    [Range(0, 999999999)] decimal Amount,
    bool IsActive = true);

public sealed record UpdateIrregularPaymentStatusRequest(
    bool IsActive,
    [MaxLength(1000)] string? Reason);
