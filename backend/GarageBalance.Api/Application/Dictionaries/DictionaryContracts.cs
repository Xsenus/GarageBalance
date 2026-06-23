using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed record OwnerDto(
    Guid Id,
    string LastName,
    string FirstName,
    string? MiddleName,
    string FullName,
    string? Phone,
    string? Address,
    string? MeterNotes,
    bool IsArchived);

public sealed record UpsertOwnerRequest(
    [Required, MaxLength(120)] string LastName,
    [Required, MaxLength(120)] string FirstName,
    [MaxLength(120)] string? MiddleName,
    [MaxLength(80)] string? Phone,
    [MaxLength(500)] string? Address,
    [MaxLength(1000)] string? MeterNotes);

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
    bool IsArchived);

public sealed record UpsertTariffRequest(
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(80)] string CalculationBase,
    [Range(0.0001, 999999999)] decimal Rate,
    DateOnly EffectiveFrom,
    [MaxLength(1000)] string? Comment);
