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
    [property: Required, MaxLength(120)] string LastName,
    [property: Required, MaxLength(120)] string FirstName,
    [property: MaxLength(120)] string? MiddleName,
    [property: MaxLength(80)] string? Phone,
    [property: MaxLength(500)] string? Address,
    [property: MaxLength(1000)] string? MeterNotes);

public sealed record GarageDto(
    Guid Id,
    string Number,
    int PeopleCount,
    int FloorCount,
    Guid? OwnerId,
    string? OwnerName,
    decimal? InitialWaterMeterValue,
    decimal? InitialElectricityMeterValue,
    string? Comment,
    bool IsArchived);

public sealed record UpsertGarageRequest(
    [property: Required, MaxLength(80)] string Number,
    [property: Range(0, 1000)] int PeopleCount,
    [property: Range(0, 100)] int FloorCount,
    Guid? OwnerId,
    [property: Range(0, 999999999)] decimal? InitialWaterMeterValue,
    [property: Range(0, 999999999)] decimal? InitialElectricityMeterValue,
    [property: MaxLength(1000)] string? Comment);

public sealed record SupplierGroupDto(Guid Id, string Name, bool IsSystem, bool IsArchived);

public sealed record UpsertSupplierGroupRequest([property: Required, MaxLength(200)] string Name);

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
    [property: Required, MaxLength(240)] string Name,
    Guid GroupId,
    [property: MaxLength(20)] string? Inn,
    [property: MaxLength(500)] string? LegalAddress,
    [property: MaxLength(200)] string? ContactPerson,
    [property: MaxLength(80)] string? Phone,
    [property: EmailAddress, MaxLength(320)] string? Email,
    decimal StartingBalance,
    [property: MaxLength(1000)] string? Comment);

public sealed record AccountingTypeDto(Guid Id, string Name, string? Code, bool IsSystem, bool IsArchived);

public sealed record UpsertAccountingTypeRequest(
    [property: Required, MaxLength(200)] string Name,
    [property: MaxLength(80)] string? Code);

public sealed record TariffDto(
    Guid Id,
    string Name,
    string CalculationBase,
    decimal Rate,
    DateOnly EffectiveFrom,
    string? Comment,
    bool IsArchived);

public sealed record UpsertTariffRequest(
    [property: Required, MaxLength(200)] string Name,
    [property: Required, MaxLength(80)] string CalculationBase,
    [property: Range(0.0001, 999999999)] decimal Rate,
    DateOnly EffectiveFrom,
    [property: MaxLength(1000)] string? Comment);
