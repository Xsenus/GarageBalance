using System.Security.Claims;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class DictionariesControllerTests
{
    [Fact]
    public async Task ListEndpoints_PassLimitToService()
    {
        var service = new FakeDictionaryService();
        var controller = CreateController(service);
        var groupId = Guid.NewGuid();

        await controller.GetOwners("ivan", 40, CancellationToken.None);
        await controller.GetGarages("12", 41, CancellationToken.None);
        await controller.GetSupplierGroups(42, CancellationToken.None);
        await controller.GetSuppliers(groupId, "water", 43, CancellationToken.None);
        await controller.GetIncomeTypes(44, CancellationToken.None);
        await controller.GetExpenseTypes(45, CancellationToken.None);
        await controller.GetTariffs("meter", 46, CancellationToken.None);

        Assert.Equal(("ivan", 40), service.LastOwnerListRequest);
        Assert.Equal(("12", 41), service.LastGarageListRequest);
        Assert.Equal(42, service.LastSupplierGroupLimit);
        Assert.Equal((groupId, "water", 43), service.LastSupplierListRequest);
        Assert.Equal(44, service.LastIncomeTypeLimit);
        Assert.Equal(45, service.LastExpenseTypeLimit);
        Assert.Equal(("meter", 46), service.LastTariffListRequest);
    }

    [Fact]
    public async Task CreateGarage_ReturnsConflictForDuplicateNumber()
    {
        var controller = CreateController(new FakeDictionaryService
        {
            CreateGarageResult = DictionaryResult<GarageDto>.Failure("garage_number_duplicate", "Гараж с таким номером уже существует.")
        });

        var result = await controller.CreateGarage(new UpsertGarageRequest("12", 1, 1, null, 0, null, null, null), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("garage_number_duplicate", problem.Title);
    }

    [Fact]
    public async Task UpdateSupplier_ReturnsNotFoundForMissingSupplier()
    {
        var controller = CreateController(new FakeDictionaryService
        {
            UpdateSupplierResult = DictionaryResult<SupplierDto>.Failure("supplier_not_found", "Поставщик не найден.")
        });

        var result = await controller.UpdateSupplier(
            Guid.NewGuid(),
            new UpsertSupplierRequest("Поставщик", Guid.NewGuid(), null, null, null, null, null, 0, null),
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("supplier_not_found", problem.Title);
    }

    [Fact]
    public async Task CreateOwner_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var owner = new OwnerDto(Guid.NewGuid(), "Иванов", "Иван", null, "Иванов Иван", null, null, null, false);
        var service = new FakeDictionaryService
        {
            CreateOwnerResult = DictionaryResult<OwnerDto>.Success(owner)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CreateOwner(new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null), CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Fact]
    public async Task ArchiveOwner_ReturnsNoContentAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var service = new FakeDictionaryService
        {
            ArchiveOwnerResult = DictionaryResult<OwnerDto>.Success(new OwnerDto(ownerId, "Иванов", "Иван", null, "Иванов Иван", null, null, null, true))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.ArchiveOwner(ownerId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Fact]
    public async Task ArchiveIncomeType_ReturnsConflictForSystemType()
    {
        var controller = CreateController(new FakeDictionaryService
        {
            ArchiveIncomeTypeResult = DictionaryResult<AccountingTypeDto>.Failure("income_type_system", "Системный вид поступления нельзя архивировать.")
        });

        var result = await controller.ArchiveIncomeType(Guid.NewGuid(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("income_type_system", problem.Title);
    }

    [Fact]
    public async Task CreateTariff_ReturnsConflictForDuplicateTariff()
    {
        var controller = CreateController(new FakeDictionaryService
        {
            CreateTariffResult = DictionaryResult<TariffDto>.Failure("tariff_duplicate", "Тариф с таким названием и датой действия уже существует.")
        });

        var result = await controller.CreateTariff(
            new UpsertTariffRequest("Вода", "meter_water", 50m, new DateOnly(2026, 7, 1), null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("tariff_duplicate", problem.Title);
    }

    [Fact]
    public async Task UpdateTariff_ReturnsConflictForDuplicateTariff()
    {
        var controller = CreateController(new FakeDictionaryService
        {
            UpdateTariffResult = DictionaryResult<TariffDto>.Failure("tariff_duplicate", "Тариф с таким названием и датой действия уже существует.")
        });

        var result = await controller.UpdateTariff(
            Guid.NewGuid(),
            new UpsertTariffRequest("Вода", "meter_water", 50m, new DateOnly(2026, 7, 1), null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("tariff_duplicate", problem.Title);
    }

    [Fact]
    public async Task CreateTariff_ReturnsBadRequestForUnsupportedCalculationBase()
    {
        var controller = CreateController(new FakeDictionaryService
        {
            CreateTariffResult = DictionaryResult<TariffDto>.Failure("tariff_calculation_base_invalid", "База расчета тарифа должна быть fixed, people, meter_water или meter_electricity.")
        });

        var result = await controller.CreateTariff(
            new UpsertTariffRequest("Непонятный тариф", "unknown_base", 50m, new DateOnly(2026, 7, 1), null),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("tariff_calculation_base_invalid", problem.Title);
    }

    private static DictionariesController CreateController(FakeDictionaryService service, Guid? actorUserId = null)
    {
        var controller = new DictionariesController(service);
        var claims = actorUserId is null ? [] : new[] { new Claim(ClaimTypes.NameIdentifier, actorUserId.Value.ToString()) };
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        return controller;
    }

    private sealed class FakeDictionaryService : IDictionaryService
    {
        public Guid? LastActorUserId { get; private set; }
        public (string? Search, int? Limit) LastOwnerListRequest { get; private set; }
        public (string? Search, int? Limit) LastGarageListRequest { get; private set; }
        public int? LastSupplierGroupLimit { get; private set; }
        public (Guid? GroupId, string? Search, int? Limit) LastSupplierListRequest { get; private set; }
        public int? LastIncomeTypeLimit { get; private set; }
        public int? LastExpenseTypeLimit { get; private set; }
        public (string? Search, int? Limit) LastTariffListRequest { get; private set; }
        public DictionaryResult<OwnerDto> CreateOwnerResult { get; init; } = DictionaryResult<OwnerDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<OwnerDto> ArchiveOwnerResult { get; init; } = DictionaryResult<OwnerDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<GarageDto> CreateGarageResult { get; init; } = DictionaryResult<GarageDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<AccountingTypeDto> ArchiveIncomeTypeResult { get; init; } = DictionaryResult<AccountingTypeDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<SupplierDto> UpdateSupplierResult { get; init; } = DictionaryResult<SupplierDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<TariffDto> CreateTariffResult { get; init; } = DictionaryResult<TariffDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<TariffDto> UpdateTariffResult { get; init; } = DictionaryResult<TariffDto>.Failure("not_configured", "Not configured.");

        public Task<IReadOnlyList<OwnerDto>> GetOwnersAsync(string? search, CancellationToken cancellationToken, int? limit = null)
        {
            LastOwnerListRequest = (search, limit);
            return Task.FromResult<IReadOnlyList<OwnerDto>>([]);
        }

        public Task<PagedResult<OwnerDto>> GetOwnersPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PagedResult<OwnerDto>([], 0, offset ?? 0, limit ?? 100));
        }

        public Task<DictionaryResult<OwnerDto>> CreateOwnerAsync(UpsertOwnerRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateOwnerResult);
        }

        public Task<DictionaryResult<OwnerDto>> UpdateOwnerAsync(Guid id, UpsertOwnerRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<OwnerDto>.Failure("owner_not_found", "Not found."));
        }

        public Task<DictionaryResult<OwnerDto>> ArchiveOwnerAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(ArchiveOwnerResult);
        }

        public Task<IReadOnlyList<GarageDto>> GetGaragesAsync(string? search, CancellationToken cancellationToken, int? limit = null)
        {
            LastGarageListRequest = (search, limit);
            return Task.FromResult<IReadOnlyList<GarageDto>>([]);
        }

        public Task<PagedResult<GarageDto>> GetGaragesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PagedResult<GarageDto>([], 0, offset ?? 0, limit ?? 100));
        }

        public Task<DictionaryResult<GarageDto>> CreateGarageAsync(UpsertGarageRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateGarageResult);
        }

        public Task<DictionaryResult<GarageDto>> UpdateGarageAsync(Guid id, UpsertGarageRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<GarageDto>.Failure("garage_not_found", "Not found."));
        }

        public Task<DictionaryResult<GarageDto>> ArchiveGarageAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<GarageDto>.Failure("garage_not_found", "Not found."));
        }

        public Task<IReadOnlyList<SupplierGroupDto>> GetSupplierGroupsAsync(CancellationToken cancellationToken, int? limit = null)
        {
            LastSupplierGroupLimit = limit;
            return Task.FromResult<IReadOnlyList<SupplierGroupDto>>([]);
        }

        public Task<PagedResult<SupplierGroupDto>> GetSupplierGroupsPageAsync(int? offset, int? limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PagedResult<SupplierGroupDto>([], 0, offset ?? 0, limit ?? 100));
        }

        public Task<DictionaryResult<SupplierGroupDto>> CreateSupplierGroupAsync(UpsertSupplierGroupRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<SupplierGroupDto>.Failure("supplier_group_duplicate", "Duplicate."));
        }

        public Task<DictionaryResult<SupplierGroupDto>> UpdateSupplierGroupAsync(Guid id, UpsertSupplierGroupRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<SupplierGroupDto>.Failure("supplier_group_not_found", "Not found."));
        }

        public Task<DictionaryResult<SupplierGroupDto>> ArchiveSupplierGroupAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<SupplierGroupDto>.Failure("supplier_group_not_found", "Not found."));
        }

        public Task<IReadOnlyList<SupplierDto>> GetSuppliersAsync(Guid? groupId, string? search, CancellationToken cancellationToken, int? limit = null)
        {
            LastSupplierListRequest = (groupId, search, limit);
            return Task.FromResult<IReadOnlyList<SupplierDto>>([]);
        }

        public Task<PagedResult<SupplierDto>> GetSuppliersPageAsync(Guid? groupId, string? search, int? offset, int? limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PagedResult<SupplierDto>([], 0, offset ?? 0, limit ?? 100));
        }

        public Task<DictionaryResult<SupplierDto>> CreateSupplierAsync(UpsertSupplierRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<SupplierDto>.Failure("supplier_group_not_found", "Not found."));
        }

        public Task<DictionaryResult<SupplierDto>> UpdateSupplierAsync(Guid id, UpsertSupplierRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(UpdateSupplierResult);
        }

        public Task<DictionaryResult<SupplierDto>> ArchiveSupplierAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<SupplierDto>.Failure("supplier_not_found", "Not found."));
        }

        public Task<IReadOnlyList<AccountingTypeDto>> GetIncomeTypesAsync(CancellationToken cancellationToken, int? limit = null)
        {
            LastIncomeTypeLimit = limit;
            return Task.FromResult<IReadOnlyList<AccountingTypeDto>>([]);
        }

        public Task<PagedResult<AccountingTypeDto>> GetIncomeTypesPageAsync(int? offset, int? limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PagedResult<AccountingTypeDto>([], 0, offset ?? 0, limit ?? 100));
        }

        public Task<DictionaryResult<AccountingTypeDto>> CreateIncomeTypeAsync(UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<AccountingTypeDto>.Failure("income_type_duplicate", "Duplicate."));
        }

        public Task<DictionaryResult<AccountingTypeDto>> UpdateIncomeTypeAsync(Guid id, UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<AccountingTypeDto>.Failure("income_type_not_found", "Not found."));
        }

        public Task<DictionaryResult<AccountingTypeDto>> ArchiveIncomeTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ArchiveIncomeTypeResult);
        }

        public Task<IReadOnlyList<AccountingTypeDto>> GetExpenseTypesAsync(CancellationToken cancellationToken, int? limit = null)
        {
            LastExpenseTypeLimit = limit;
            return Task.FromResult<IReadOnlyList<AccountingTypeDto>>([]);
        }

        public Task<PagedResult<AccountingTypeDto>> GetExpenseTypesPageAsync(int? offset, int? limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PagedResult<AccountingTypeDto>([], 0, offset ?? 0, limit ?? 100));
        }

        public Task<DictionaryResult<AccountingTypeDto>> CreateExpenseTypeAsync(UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<AccountingTypeDto>.Failure("expense_type_duplicate", "Duplicate."));
        }

        public Task<DictionaryResult<AccountingTypeDto>> UpdateExpenseTypeAsync(Guid id, UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<AccountingTypeDto>.Failure("expense_type_not_found", "Not found."));
        }

        public Task<DictionaryResult<AccountingTypeDto>> ArchiveExpenseTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<AccountingTypeDto>.Failure("expense_type_not_found", "Not found."));
        }

        public Task<IReadOnlyList<TariffDto>> GetTariffsAsync(string? search, CancellationToken cancellationToken, int? limit = null)
        {
            LastTariffListRequest = (search, limit);
            return Task.FromResult<IReadOnlyList<TariffDto>>([]);
        }

        public Task<PagedResult<TariffDto>> GetTariffsPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PagedResult<TariffDto>([], 0, offset ?? 0, limit ?? 100));
        }

        public Task<DictionaryResult<TariffDto>> CreateTariffAsync(UpsertTariffRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateTariffResult);
        }

        public Task<DictionaryResult<TariffDto>> UpdateTariffAsync(Guid id, UpsertTariffRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(UpdateTariffResult);
        }

        public Task<DictionaryResult<TariffDto>> ArchiveTariffAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<TariffDto>.Failure("tariff_not_found", "Not found."));
        }
    }
}
