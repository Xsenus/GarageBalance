using System.Security.Claims;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class DictionariesControllerTests
{
    [Fact]
    public async Task CreateGarage_ReturnsConflictForDuplicateNumber()
    {
        var controller = CreateController(new FakeDictionaryService
        {
            CreateGarageResult = DictionaryResult<GarageDto>.Failure("garage_number_duplicate", "Гараж с таким номером уже существует.")
        });

        var result = await controller.CreateGarage(new UpsertGarageRequest("12", 1, 1, null, null, null, null), CancellationToken.None);

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
        public DictionaryResult<OwnerDto> CreateOwnerResult { get; init; } = DictionaryResult<OwnerDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<GarageDto> CreateGarageResult { get; init; } = DictionaryResult<GarageDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<SupplierDto> UpdateSupplierResult { get; init; } = DictionaryResult<SupplierDto>.Failure("not_configured", "Not configured.");

        public Task<IReadOnlyList<OwnerDto>> GetOwnersAsync(string? search, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<OwnerDto>>([]);
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

        public Task<IReadOnlyList<GarageDto>> GetGaragesAsync(string? search, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<GarageDto>>([]);
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

        public Task<IReadOnlyList<SupplierGroupDto>> GetSupplierGroupsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SupplierGroupDto>>([]);
        }

        public Task<DictionaryResult<SupplierGroupDto>> CreateSupplierGroupAsync(UpsertSupplierGroupRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(DictionaryResult<SupplierGroupDto>.Failure("supplier_group_duplicate", "Duplicate."));
        }

        public Task<IReadOnlyList<SupplierDto>> GetSuppliersAsync(Guid? groupId, string? search, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SupplierDto>>([]);
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
    }
}
