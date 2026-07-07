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

        await controller.GetOwners("ivan", 40, true, CancellationToken.None);
        await controller.GetGarages("12", 41, true, CancellationToken.None);
        await controller.GetSupplierGroups("group", 42, true, CancellationToken.None);
        await controller.GetSuppliers(groupId, "water", 43, true, CancellationToken.None);
        await controller.GetIncomeTypes("income", 44, true, CancellationToken.None);
        await controller.GetExpenseTypes("expense", 45, true, CancellationToken.None);
        await controller.GetTariffs("meter", 46, true, CancellationToken.None);
        await controller.GetIrregularPayments("fine", 47, true, CancellationToken.None);

        Assert.Equal(("ivan", 40, true), service.LastOwnerListRequest);
        Assert.Equal(("12", 41, true), service.LastGarageListRequest);
        Assert.Equal(("group", 42, true), service.LastSupplierGroupListRequest);
        Assert.Equal((groupId, "water", 43, true), service.LastSupplierListRequest);
        Assert.Equal(("income", 44, true), service.LastIncomeTypeListRequest);
        Assert.Equal(("expense", 45, true), service.LastExpenseTypeListRequest);
        Assert.Equal(("meter", 46, true), service.LastTariffListRequest);
        Assert.Equal(("fine", 47, true), service.LastIrregularPaymentListRequest);
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
    public async Task ArchiveIrregularPayment_ReturnsConflictWhenPaymentIsUsed()
    {
        var controller = CreateController(new FakeDictionaryService
        {
            ArchiveIrregularPaymentResult = DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_used", "Удаление недоступно.")
        });

        var result = await controller.ArchiveIrregularPayment(Guid.NewGuid(), new ArchiveDictionaryEntryRequest("Причина"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("irregular_payment_used", problem.Title);
    }

    [Fact]
    public async Task RestoreIrregularPayment_ReturnsOkActiveRecordAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var service = new FakeDictionaryService
        {
            RestoreIrregularPaymentResult = DictionaryResult<IrregularPaymentDto>.Success(new IrregularPaymentDto(paymentId, "Gate repair", 500m, true, false, false))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.RestoreIrregularPayment(paymentId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payment = Assert.IsType<IrregularPaymentDto>(ok.Value);
        Assert.Equal(paymentId, payment.Id);
        Assert.False(payment.IsArchived);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(paymentId, service.LastRestoreId);
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

        var result = await controller.ArchiveOwner(ownerId, new ArchiveDictionaryEntryRequest("Дубликат"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal("Дубликат", service.LastArchiveReason);
    }

    [Fact]
    public async Task ArchiveOwner_ReturnsBadRequestWhenReasonIsEmpty()
    {
        var service = new FakeDictionaryService();
        var controller = CreateController(service);

        var result = await controller.ArchiveOwner(Guid.NewGuid(), new ArchiveDictionaryEntryRequest("   "), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("dictionary_archive_reason_required", problem.Title);
        Assert.Null(service.LastArchiveReason);
    }

    [Fact]
    public async Task RestoreOwner_ReturnsOkAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var service = new FakeDictionaryService
        {
            RestoreOwnerResult = DictionaryResult<OwnerDto>.Success(new OwnerDto(ownerId, "Иванов", "Иван", null, "Иванов Иван", null, null, null, false))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.RestoreOwner(ownerId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var restored = Assert.IsType<OwnerDto>(ok.Value);
        Assert.False(restored.IsArchived);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Theory]
    [InlineData("garage")]
    [InlineData("supplierGroup")]
    [InlineData("supplier")]
    [InlineData("incomeType")]
    [InlineData("expenseType")]
    [InlineData("tariff")]
    public async Task RestoreEndpoints_ReturnOkActiveRecordAndPassActorUserId(string dictionaryKind)
    {
        var actorUserId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var service = CreateRestoreService(dictionaryKind, id);
        var controller = CreateController(service, actorUserId);

        var result = await RestoreDictionaryRecord(controller, dictionaryKind, id);

        var ok = Assert.IsType<OkObjectResult>(result);
        AssertDictionaryRecordIsActive(dictionaryKind, ok.Value);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(id, service.LastRestoreId);
    }

    [Theory]
    [InlineData("owner", "owner_not_found")]
    [InlineData("garage", "garage_not_found")]
    [InlineData("supplierGroup", "supplier_group_not_found")]
    [InlineData("supplier", "supplier_not_found")]
    [InlineData("incomeType", "income_type_not_found")]
    [InlineData("expenseType", "expense_type_not_found")]
    [InlineData("tariff", "tariff_not_found")]
    public async Task RestoreEndpoints_ReturnNotFoundWhenArchivedRecordDoesNotExist(string dictionaryKind, string errorCode)
    {
        var actorUserId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var service = CreateRestoreFailureService(dictionaryKind, errorCode);
        var controller = CreateController(service, actorUserId);

        var result = await RestoreDictionaryRecord(controller, dictionaryKind, id);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(errorCode, problem.Title);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(id, service.LastRestoreId);
    }

    [Fact]
    public async Task RestoreTariff_ReturnsConflictForDuplicateTariff()
    {
        var controller = CreateController(new FakeDictionaryService
        {
            RestoreTariffResult = DictionaryResult<TariffDto>.Failure("tariff_duplicate", "Активный тариф с таким названием и датой действия уже существует.")
        });

        var result = await controller.RestoreTariff(Guid.NewGuid(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("tariff_duplicate", problem.Title);
    }

    [Fact]
    public async Task ArchiveIncomeType_ReturnsConflictForSystemType()
    {
        var controller = CreateController(new FakeDictionaryService
        {
            ArchiveIncomeTypeResult = DictionaryResult<AccountingTypeDto>.Failure("income_type_system", "Системный вид поступления нельзя архивировать.")
        });

        var result = await controller.ArchiveIncomeType(Guid.NewGuid(), new ArchiveDictionaryEntryRequest("Дубль"), CancellationToken.None);

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
    public async Task UpdateTariff_ReturnsConflictWhenEffectiveDateMovesAfterAccruals()
    {
        var controller = CreateController(new FakeDictionaryService
        {
            UpdateTariffResult = DictionaryResult<TariffDto>.Failure("tariff_effective_from_after_accrual", "Дата начала тарифа не может быть позже уже созданного начисления за 06.2026.")
        });

        var result = await controller.UpdateTariff(
            Guid.NewGuid(),
            new UpsertTariffRequest("Вода", "meter_water", 50m, new DateOnly(2026, 7, 1), null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("tariff_effective_from_after_accrual", problem.Title);
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

    [Fact]
    public async Task GetChargeServiceSettings_PassesFiltersToService()
    {
        var service = new FakeDictionaryService();
        var controller = CreateController(service);

        var result = await controller.GetChargeServiceSettings("электро", 25, true, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(("электро", 25, true), service.LastChargeServiceListRequest);
    }

    [Fact]
    public async Task CreateChargeServiceSetting_ReturnsCreatedAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var service = new FakeDictionaryService
        {
            CreateChargeServiceSettingResult = DictionaryResult<ChargeServiceSettingDto>.Success(new ChargeServiceSettingDto(serviceId, "Электроэнергия", true, 1, 1, 30, 6, 30, null, null, true, true, "кВт", false))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CreateChargeServiceSetting(
            new UpsertChargeServiceSettingRequest("Электроэнергия", true, 1, 1, 30, 6, 30, true, true, "кВт"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<ChargeServiceSettingDto>(created.Value);
        Assert.Equal(serviceId, dto.Id);
        Assert.Equal(nameof(DictionariesController.GetChargeServiceSettings), created.ActionName);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Fact]
    public async Task UpdateChargeServiceSetting_ReturnsBadRequestForInvalidPaymentDay()
    {
        var controller = CreateController(new FakeDictionaryService
        {
            UpdateChargeServiceSettingResult = DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_payment_day_invalid", "В выбранном месяце нельзя указать день больше 28.")
        });

        var result = await controller.UpdateChargeServiceSetting(
            Guid.NewGuid(),
            new UpsertChargeServiceSettingRequest("Членский взнос", true, 1, 1, 29, 2, 30, false, false, "руб."),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("charge_service_payment_day_invalid", problem.Title);
    }

    [Fact]
    public async Task ArchiveChargeServiceSetting_ReturnsNoContentAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var service = new FakeDictionaryService
        {
            ArchiveChargeServiceSettingResult = DictionaryResult<ChargeServiceSettingDto>.Success(new ChargeServiceSettingDto(serviceId, "Electricity", true, 1, 1, 30, 6, 30, null, null, true, true, "kWh", true))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.ArchiveChargeServiceSetting(serviceId, new ArchiveDictionaryEntryRequest("No longer used"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal("No longer used", service.LastArchiveReason);
    }

    [Fact]
    public async Task RestoreChargeServiceSetting_ReturnsOkActiveRecordAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var service = new FakeDictionaryService
        {
            RestoreChargeServiceSettingResult = DictionaryResult<ChargeServiceSettingDto>.Success(new ChargeServiceSettingDto(serviceId, "Electricity", true, 1, 1, 30, 6, 30, null, null, true, true, "kWh", false))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.RestoreChargeServiceSetting(serviceId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ChargeServiceSettingDto>(ok.Value);
        Assert.Equal(serviceId, dto.Id);
        Assert.False(dto.IsArchived);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(serviceId, service.LastRestoreId);
    }

    [Fact]
    public async Task SupplierContactsAndStaffEndpoints_PassFiltersAndActorToService()
    {
        var actorId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var service = new FakeDictionaryService
        {
            CreateSupplierContactResult = DictionaryResult<SupplierContactDto>.Success(new SupplierContactDto(contactId, supplierId, "Водоканал", "Петров", "Директор", "+7", "mail@example.com", "Работает", null, false)),
            CreateStaffDepartmentResult = DictionaryResult<StaffDepartmentDto>.Success(new StaffDepartmentDto(departmentId, "Бухгалтерия", false)),
            CreateStaffMemberResult = DictionaryResult<StaffMemberDto>.Success(new StaffMemberDto(staffId, "Петрова Ольга", departmentId, "Бухгалтерия", 40000, false))
        };
        var controller = CreateController(service, actorId);

        await controller.GetSupplierContacts(supplierId, "петр", 11, true, CancellationToken.None);
        await controller.GetStaffDepartments(12, true, CancellationToken.None);
        await controller.GetStaffMembers(departmentId, "ольга", 13, true, CancellationToken.None);
        var contactResult = await controller.CreateSupplierContact(new UpsertSupplierContactRequest(supplierId, "Петров", "Директор", "+7", "mail@example.com", "Работает", null), CancellationToken.None);
        var departmentResult = await controller.CreateStaffDepartment(new UpsertStaffDepartmentRequest("Бухгалтерия"), CancellationToken.None);
        var staffResult = await controller.CreateStaffMember(new UpsertStaffMemberRequest("Петрова Ольга", departmentId, 40000), CancellationToken.None);

        Assert.Equal((supplierId, "петр", 11, true), service.LastSupplierContactListRequest);
        Assert.Equal((12, true), service.LastStaffDepartmentListRequest);
        Assert.Equal((departmentId, "ольга", 13, true), service.LastStaffMemberListRequest);
        Assert.Equal(actorId, service.LastActorUserId);
        Assert.IsType<CreatedAtActionResult>(contactResult.Result);
        Assert.IsType<CreatedAtActionResult>(departmentResult.Result);
        Assert.IsType<CreatedAtActionResult>(staffResult.Result);
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

    private static FakeDictionaryService CreateRestoreService(string dictionaryKind, Guid id)
    {
        var groupId = Guid.NewGuid();
        return dictionaryKind switch
        {
            "garage" => new FakeDictionaryService
            {
                RestoreGarageResult = DictionaryResult<GarageDto>.Success(new GarageDto(id, "12", 1, 1, null, null, 0, null, null, null, false))
            },
            "supplierGroup" => new FakeDictionaryService
            {
                RestoreSupplierGroupResult = DictionaryResult<SupplierGroupDto>.Success(new SupplierGroupDto(id, "Коммунальные услуги", false, false))
            },
            "supplier" => new FakeDictionaryService
            {
                RestoreSupplierResult = DictionaryResult<SupplierDto>.Success(new SupplierDto(id, "Водоканал", groupId, "Коммунальные услуги", null, null, null, null, null, 0, null, false))
            },
            "incomeType" => new FakeDictionaryService
            {
                RestoreIncomeTypeResult = DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(id, "Членский взнос", null, false, false))
            },
            "expenseType" => new FakeDictionaryService
            {
                RestoreExpenseTypeResult = DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(id, "Электроэнергия", null, false, false))
            },
            "tariff" => new FakeDictionaryService
            {
                RestoreTariffResult = DictionaryResult<TariffDto>.Success(new TariffDto(id, "Электроэнергия", "meter_electricity", 3.5m, new DateOnly(2026, 1, 1), null, false))
            },
            _ => throw new ArgumentOutOfRangeException(nameof(dictionaryKind), dictionaryKind, "Unsupported dictionary kind.")
        };
    }

    private static FakeDictionaryService CreateRestoreFailureService(string dictionaryKind, string errorCode)
    {
        var errorMessage = "Архивная запись не найдена.";
        return dictionaryKind switch
        {
            "owner" => new FakeDictionaryService
            {
                RestoreOwnerResult = DictionaryResult<OwnerDto>.Failure(errorCode, errorMessage)
            },
            "garage" => new FakeDictionaryService
            {
                RestoreGarageResult = DictionaryResult<GarageDto>.Failure(errorCode, errorMessage)
            },
            "supplierGroup" => new FakeDictionaryService
            {
                RestoreSupplierGroupResult = DictionaryResult<SupplierGroupDto>.Failure(errorCode, errorMessage)
            },
            "supplier" => new FakeDictionaryService
            {
                RestoreSupplierResult = DictionaryResult<SupplierDto>.Failure(errorCode, errorMessage)
            },
            "incomeType" => new FakeDictionaryService
            {
                RestoreIncomeTypeResult = DictionaryResult<AccountingTypeDto>.Failure(errorCode, errorMessage)
            },
            "expenseType" => new FakeDictionaryService
            {
                RestoreExpenseTypeResult = DictionaryResult<AccountingTypeDto>.Failure(errorCode, errorMessage)
            },
            "tariff" => new FakeDictionaryService
            {
                RestoreTariffResult = DictionaryResult<TariffDto>.Failure(errorCode, errorMessage)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(dictionaryKind), dictionaryKind, "Unsupported dictionary kind.")
        };
    }

    private static async Task<IActionResult> RestoreDictionaryRecord(DictionariesController controller, string dictionaryKind, Guid id)
    {
        return dictionaryKind switch
        {
            "owner" => (await controller.RestoreOwner(id, CancellationToken.None)).Result!,
            "garage" => (await controller.RestoreGarage(id, CancellationToken.None)).Result!,
            "supplierGroup" => (await controller.RestoreSupplierGroup(id, CancellationToken.None)).Result!,
            "supplier" => (await controller.RestoreSupplier(id, CancellationToken.None)).Result!,
            "incomeType" => (await controller.RestoreIncomeType(id, CancellationToken.None)).Result!,
            "expenseType" => (await controller.RestoreExpenseType(id, CancellationToken.None)).Result!,
            "tariff" => (await controller.RestoreTariff(id, CancellationToken.None)).Result!,
            _ => throw new ArgumentOutOfRangeException(nameof(dictionaryKind), dictionaryKind, "Unsupported dictionary kind.")
        };
    }

    private static void AssertDictionaryRecordIsActive(string dictionaryKind, object? value)
    {
        switch (dictionaryKind)
        {
            case "garage":
                Assert.False(Assert.IsType<GarageDto>(value).IsArchived);
                break;
            case "supplierGroup":
                Assert.False(Assert.IsType<SupplierGroupDto>(value).IsArchived);
                break;
            case "supplier":
                Assert.False(Assert.IsType<SupplierDto>(value).IsArchived);
                break;
            case "incomeType":
            case "expenseType":
                Assert.False(Assert.IsType<AccountingTypeDto>(value).IsArchived);
                break;
            case "tariff":
                Assert.False(Assert.IsType<TariffDto>(value).IsArchived);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dictionaryKind), dictionaryKind, "Unsupported dictionary kind.");
        }
    }

    private sealed class FakeDictionaryService : IDictionaryService
    {
        public Guid? LastActorUserId { get; private set; }
        public Guid? LastRestoreId { get; private set; }
        public (string? Search, int? Limit, bool IncludeArchived) LastOwnerListRequest { get; private set; }
        public (string? Search, int? Limit, bool IncludeArchived) LastGarageListRequest { get; private set; }
        public (string? Search, int? Limit, bool IncludeArchived) LastSupplierGroupListRequest { get; private set; }
        public (Guid? GroupId, string? Search, int? Limit, bool IncludeArchived) LastSupplierListRequest { get; private set; }
        public (Guid? SupplierId, string? Search, int? Limit, bool IncludeArchived) LastSupplierContactListRequest { get; private set; }
        public (int? Limit, bool IncludeArchived) LastStaffDepartmentListRequest { get; private set; }
        public (Guid? DepartmentId, string? Search, int? Limit, bool IncludeArchived) LastStaffMemberListRequest { get; private set; }
        public (string? Search, int? Limit, bool IncludeArchived) LastIncomeTypeListRequest { get; private set; }
        public (string? Search, int? Limit, bool IncludeArchived) LastExpenseTypeListRequest { get; private set; }
        public (string? Search, int? Limit, bool IncludeArchived) LastTariffListRequest { get; private set; }
        public (string? Search, int? Limit, bool IncludeArchived) LastChargeServiceListRequest { get; private set; }
        public (string? Search, int? Limit, bool IncludeArchived) LastIrregularPaymentListRequest { get; private set; }
        public string? LastArchiveReason { get; private set; }
        public DictionaryResult<OwnerDto> CreateOwnerResult { get; init; } = DictionaryResult<OwnerDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<OwnerDto> ArchiveOwnerResult { get; init; } = DictionaryResult<OwnerDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<OwnerDto> RestoreOwnerResult { get; init; } = DictionaryResult<OwnerDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<GarageDto> CreateGarageResult { get; init; } = DictionaryResult<GarageDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<GarageDto> RestoreGarageResult { get; init; } = DictionaryResult<GarageDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<SupplierGroupDto> RestoreSupplierGroupResult { get; init; } = DictionaryResult<SupplierGroupDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<SupplierDto> RestoreSupplierResult { get; init; } = DictionaryResult<SupplierDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<SupplierContactDto> CreateSupplierContactResult { get; init; } = DictionaryResult<SupplierContactDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<SupplierContactDto> RestoreSupplierContactResult { get; init; } = DictionaryResult<SupplierContactDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<StaffDepartmentDto> CreateStaffDepartmentResult { get; init; } = DictionaryResult<StaffDepartmentDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<StaffDepartmentDto> RestoreStaffDepartmentResult { get; init; } = DictionaryResult<StaffDepartmentDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<StaffMemberDto> CreateStaffMemberResult { get; init; } = DictionaryResult<StaffMemberDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<StaffMemberDto> RestoreStaffMemberResult { get; init; } = DictionaryResult<StaffMemberDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<AccountingTypeDto> ArchiveIncomeTypeResult { get; init; } = DictionaryResult<AccountingTypeDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<AccountingTypeDto> RestoreIncomeTypeResult { get; init; } = DictionaryResult<AccountingTypeDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<AccountingTypeDto> RestoreExpenseTypeResult { get; init; } = DictionaryResult<AccountingTypeDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<SupplierDto> UpdateSupplierResult { get; init; } = DictionaryResult<SupplierDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<TariffDto> CreateTariffResult { get; init; } = DictionaryResult<TariffDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<TariffDto> UpdateTariffResult { get; init; } = DictionaryResult<TariffDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<TariffDto> RestoreTariffResult { get; init; } = DictionaryResult<TariffDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<ChargeServiceSettingDto> CreateChargeServiceSettingResult { get; init; } = DictionaryResult<ChargeServiceSettingDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<ChargeServiceSettingDto> UpdateChargeServiceSettingResult { get; init; } = DictionaryResult<ChargeServiceSettingDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<ChargeServiceSettingDto> ArchiveChargeServiceSettingResult { get; init; } = DictionaryResult<ChargeServiceSettingDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<ChargeServiceSettingDto> RestoreChargeServiceSettingResult { get; init; } = DictionaryResult<ChargeServiceSettingDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<IrregularPaymentDto> ArchiveIrregularPaymentResult { get; init; } = DictionaryResult<IrregularPaymentDto>.Failure("not_configured", "Not configured.");
        public DictionaryResult<IrregularPaymentDto> RestoreIrregularPaymentResult { get; init; } = DictionaryResult<IrregularPaymentDto>.Failure("not_configured", "Not configured.");

        public Task<IReadOnlyList<OwnerDto>> GetOwnersAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
        {
            LastOwnerListRequest = (search, limit, includeArchived);
            return Task.FromResult<IReadOnlyList<OwnerDto>>([]);
        }

        public Task<PagedResult<OwnerDto>> GetOwnersPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
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

        public Task<DictionaryResult<OwnerDto>> ArchiveOwnerAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastArchiveReason = reason;
            return Task.FromResult(ArchiveOwnerResult);
        }

        public Task<DictionaryResult<OwnerDto>> RestoreOwnerAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastRestoreId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(RestoreOwnerResult);
        }

        public Task<IReadOnlyList<GarageDto>> GetGaragesAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
        {
            LastGarageListRequest = (search, limit, includeArchived);
            return Task.FromResult<IReadOnlyList<GarageDto>>([]);
        }

        public Task<PagedResult<GarageDto>> GetGaragesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
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

        public Task<DictionaryResult<GarageDto>> ArchiveGarageAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastArchiveReason = reason;
            return Task.FromResult(DictionaryResult<GarageDto>.Failure("garage_not_found", "Not found."));
        }

        public Task<DictionaryResult<GarageDto>> RestoreGarageAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastRestoreId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(RestoreGarageResult);
        }

        public Task<IReadOnlyList<SupplierGroupDto>> GetSupplierGroupsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
        {
            LastSupplierGroupListRequest = (search, limit, includeArchived);
            return Task.FromResult<IReadOnlyList<SupplierGroupDto>>([]);
        }

        public Task<PagedResult<SupplierGroupDto>> GetSupplierGroupsPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
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

        public Task<DictionaryResult<SupplierGroupDto>> ArchiveSupplierGroupAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastArchiveReason = reason;
            return Task.FromResult(DictionaryResult<SupplierGroupDto>.Failure("supplier_group_not_found", "Not found."));
        }

        public Task<DictionaryResult<SupplierGroupDto>> RestoreSupplierGroupAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastRestoreId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(RestoreSupplierGroupResult);
        }

        public Task<IReadOnlyList<SupplierDto>> GetSuppliersAsync(Guid? groupId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
        {
            LastSupplierListRequest = (groupId, search, limit, includeArchived);
            return Task.FromResult<IReadOnlyList<SupplierDto>>([]);
        }

        public Task<PagedResult<SupplierDto>> GetSuppliersPageAsync(Guid? groupId, string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
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

        public Task<DictionaryResult<SupplierDto>> ArchiveSupplierAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastArchiveReason = reason;
            return Task.FromResult(DictionaryResult<SupplierDto>.Failure("supplier_not_found", "Not found."));
        }

        public Task<DictionaryResult<SupplierDto>> RestoreSupplierAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastRestoreId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(RestoreSupplierResult);
        }

        public Task<IReadOnlyList<SupplierContactDto>> GetSupplierContactsAsync(Guid? supplierId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
        {
            LastSupplierContactListRequest = (supplierId, search, limit, includeArchived);
            return Task.FromResult<IReadOnlyList<SupplierContactDto>>([]);
        }

        public Task<DictionaryResult<SupplierContactDto>> CreateSupplierContactAsync(UpsertSupplierContactRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateSupplierContactResult);
        }

        public Task<DictionaryResult<SupplierContactDto>> UpdateSupplierContactAsync(Guid id, UpsertSupplierContactRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(DictionaryResult<SupplierContactDto>.Failure("supplier_contact_not_found", "Not found."));
        }

        public Task<DictionaryResult<SupplierContactDto>> ArchiveSupplierContactAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastArchiveReason = reason;
            return Task.FromResult(DictionaryResult<SupplierContactDto>.Failure("supplier_contact_not_found", "Not found."));
        }

        public Task<DictionaryResult<SupplierContactDto>> RestoreSupplierContactAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastRestoreId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(RestoreSupplierContactResult);
        }

        public Task<IReadOnlyList<StaffDepartmentDto>> GetStaffDepartmentsAsync(CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
        {
            LastStaffDepartmentListRequest = (limit, includeArchived);
            return Task.FromResult<IReadOnlyList<StaffDepartmentDto>>([]);
        }

        public Task<DictionaryResult<StaffDepartmentDto>> CreateStaffDepartmentAsync(UpsertStaffDepartmentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateStaffDepartmentResult);
        }

        public Task<DictionaryResult<StaffDepartmentDto>> UpdateStaffDepartmentAsync(Guid id, UpsertStaffDepartmentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(DictionaryResult<StaffDepartmentDto>.Failure("staff_department_not_found", "Not found."));
        }

        public Task<DictionaryResult<StaffDepartmentDto>> ArchiveStaffDepartmentAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastArchiveReason = reason;
            return Task.FromResult(DictionaryResult<StaffDepartmentDto>.Failure("staff_department_not_found", "Not found."));
        }

        public Task<DictionaryResult<StaffDepartmentDto>> RestoreStaffDepartmentAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastRestoreId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(RestoreStaffDepartmentResult);
        }

        public Task<IReadOnlyList<StaffMemberDto>> GetStaffMembersAsync(Guid? departmentId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
        {
            LastStaffMemberListRequest = (departmentId, search, limit, includeArchived);
            return Task.FromResult<IReadOnlyList<StaffMemberDto>>([]);
        }

        public Task<DictionaryResult<StaffMemberDto>> CreateStaffMemberAsync(UpsertStaffMemberRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateStaffMemberResult);
        }

        public Task<DictionaryResult<StaffMemberDto>> UpdateStaffMemberAsync(Guid id, UpsertStaffMemberRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(DictionaryResult<StaffMemberDto>.Failure("staff_member_not_found", "Not found."));
        }

        public Task<DictionaryResult<StaffMemberDto>> ArchiveStaffMemberAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastArchiveReason = reason;
            return Task.FromResult(DictionaryResult<StaffMemberDto>.Failure("staff_member_not_found", "Not found."));
        }

        public Task<DictionaryResult<StaffMemberDto>> RestoreStaffMemberAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastRestoreId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(RestoreStaffMemberResult);
        }

        public Task<IReadOnlyList<AccountingTypeDto>> GetIncomeTypesAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
        {
            LastIncomeTypeListRequest = (search, limit, includeArchived);
            return Task.FromResult<IReadOnlyList<AccountingTypeDto>>([]);
        }

        public Task<PagedResult<AccountingTypeDto>> GetIncomeTypesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
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

        public Task<DictionaryResult<AccountingTypeDto>> ArchiveIncomeTypeAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastArchiveReason = reason;
            return Task.FromResult(ArchiveIncomeTypeResult);
        }

        public Task<DictionaryResult<AccountingTypeDto>> RestoreIncomeTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastRestoreId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(RestoreIncomeTypeResult);
        }

        public Task<IReadOnlyList<AccountingTypeDto>> GetExpenseTypesAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
        {
            LastExpenseTypeListRequest = (search, limit, includeArchived);
            return Task.FromResult<IReadOnlyList<AccountingTypeDto>>([]);
        }

        public Task<PagedResult<AccountingTypeDto>> GetExpenseTypesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
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

        public Task<DictionaryResult<AccountingTypeDto>> ArchiveExpenseTypeAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastArchiveReason = reason;
            return Task.FromResult(DictionaryResult<AccountingTypeDto>.Failure("expense_type_not_found", "Not found."));
        }

        public Task<DictionaryResult<AccountingTypeDto>> RestoreExpenseTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastRestoreId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(RestoreExpenseTypeResult);
        }

        public Task<IReadOnlyList<TariffDto>> GetTariffsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
        {
            LastTariffListRequest = (search, limit, includeArchived);
            return Task.FromResult<IReadOnlyList<TariffDto>>([]);
        }

        public Task<PagedResult<TariffDto>> GetTariffsPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
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

        public Task<DictionaryResult<TariffDto>> ArchiveTariffAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastArchiveReason = reason;
            return Task.FromResult(DictionaryResult<TariffDto>.Failure("tariff_not_found", "Not found."));
        }

        public Task<DictionaryResult<TariffDto>> RestoreTariffAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastRestoreId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(RestoreTariffResult);
        }

        public Task<IReadOnlyList<ChargeServiceSettingDto>> GetChargeServiceSettingsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
        {
            LastChargeServiceListRequest = (search, limit, includeArchived);
            return Task.FromResult<IReadOnlyList<ChargeServiceSettingDto>>([]);
        }

        public Task<DictionaryResult<ChargeServiceSettingDto>> CreateChargeServiceSettingAsync(UpsertChargeServiceSettingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateChargeServiceSettingResult);
        }

        public Task<DictionaryResult<ChargeServiceSettingDto>> UpdateChargeServiceSettingAsync(Guid id, UpsertChargeServiceSettingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(UpdateChargeServiceSettingResult);
        }

        public Task<DictionaryResult<ChargeServiceSettingDto>> ArchiveChargeServiceSettingAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastArchiveReason = reason;
            return Task.FromResult(ArchiveChargeServiceSettingResult);
        }

        public Task<DictionaryResult<ChargeServiceSettingDto>> RestoreChargeServiceSettingAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastRestoreId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(RestoreChargeServiceSettingResult);
        }

        public Task<IReadOnlyList<IrregularPaymentDto>> GetIrregularPaymentsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
        {
            LastIrregularPaymentListRequest = (search, limit, includeArchived);
            return Task.FromResult<IReadOnlyList<IrregularPaymentDto>>([]);
        }

        public Task<DictionaryResult<IrregularPaymentDto>> CreateIrregularPaymentAsync(UpsertIrregularPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(DictionaryResult<IrregularPaymentDto>.Failure("not_configured", "Not configured."));
        }

        public Task<DictionaryResult<IrregularPaymentDto>> UpdateIrregularPaymentAsync(Guid id, UpsertIrregularPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(DictionaryResult<IrregularPaymentDto>.Failure("not_configured", "Not configured."));
        }

        public Task<DictionaryResult<IrregularPaymentDto>> SetIrregularPaymentStatusAsync(Guid id, UpdateIrregularPaymentStatusRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(DictionaryResult<IrregularPaymentDto>.Failure("not_configured", "Not configured."));
        }

        public Task<DictionaryResult<IrregularPaymentDto>> ArchiveIrregularPaymentAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastArchiveReason = reason;
            return Task.FromResult(ArchiveIrregularPaymentResult);
        }

        public Task<DictionaryResult<IrregularPaymentDto>> RestoreIrregularPaymentAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastRestoreId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(RestoreIrregularPaymentResult);
        }
    }
}
