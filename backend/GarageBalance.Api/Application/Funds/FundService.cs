using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Funds;

public sealed class FundService(
    IFundRepository repository,
    IAuditEventWriter auditEventWriter) : IFundService
{
    private static readonly IReadOnlyList<DefaultFundDefinition> DefaultFunds =
    [
        new("Электроэнергия", 10, true),
        new("Водоснабжение", 20, true),
        new("Вывоз мусора", 30, true),
        new("Наружное освещение", 40, true),
        new("Членские взносы", 50, true),
        new("Целевые взносы", 60, true),
        new("Прочее", 70, true)
    ];

    public async Task<IReadOnlyList<FundDto>> GetFundsAsync(CancellationToken cancellationToken)
    {
        var funds = (await repository.GetFundsAsync(cancellationToken)).ToList();
        await EnsureDefaultFundsAsync(funds, cancellationToken);
        var availableToDistribute = await CalculateAvailableToDistributeAsync(cancellationToken);
        var linkedServicesByFundId = (await repository.GetLinkedServicesAsync(
            funds.Select(fund => fund.Id).ToArray(),
            cancellationToken)).ToLookup(service => service.FundId);

        return funds
            .OrderBy(fund => fund.SortOrder)
            .ThenBy(fund => fund.Name)
            .Select(fund => ToDto(fund, availableToDistribute, linkedServicesByFundId[fund.Id]))
            .ToList();
    }

    public async Task<FundResult<FundDto>> CreateFundAsync(
        UpsertFundRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var nameValidation = ValidateFundName(request.Name);
        if (nameValidation.ErrorCode is not null)
        {
            return FundResult<FundDto>.Failure(nameValidation.ErrorCode, nameValidation.ErrorMessage!);
        }

        await using var allocationLock = await repository.AcquireAllocationLockAsync(cancellationToken);
        var name = nameValidation.Name!;
        var normalizedName = NormalizeName(name);
        if (await repository.FundNameExistsAsync(null, normalizedName, cancellationToken))
        {
            return FundResult<FundDto>.Failure("fund_duplicate", "Фонд с таким названием уже существует.");
        }

        var funds = await repository.GetFundsAsync(cancellationToken);
        var fund = new Fund
        {
            Name = name,
            NormalizedName = normalizedName,
            SortOrder = funds.Count == 0 ? 10 : funds.Max(item => item.SortOrder) + 10,
            AllowOperations = true,
            IsSystem = false
        };

        repository.AddFund(fund);
        AddFundCreatedAudit(fund, actorUserId);
        await repository.SaveChangesAsync(cancellationToken);

        var availableToDistribute = await CalculateAvailableToDistributeAsync(cancellationToken);
        return FundResult<FundDto>.Success(ToDto(fund, availableToDistribute, []));
    }

    public async Task<FundResult<FundDto>> UpdateFundAsync(
        Guid fundId,
        UpsertFundRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var nameValidation = ValidateFundName(request.Name);
        if (nameValidation.ErrorCode is not null)
        {
            return FundResult<FundDto>.Failure(nameValidation.ErrorCode, nameValidation.ErrorMessage!);
        }

        await using var allocationLock = await repository.AcquireAllocationLockAsync(cancellationToken);
        var fund = await repository.FindFundForUpdateAsync(fundId, cancellationToken);
        if (fund is null)
        {
            return FundResult<FundDto>.Failure("fund_not_found", "Фонд не найден.");
        }

        var name = nameValidation.Name!;
        var normalizedName = NormalizeName(name);
        if (await repository.FundNameExistsAsync(fundId, normalizedName, cancellationToken))
        {
            return FundResult<FundDto>.Failure("fund_duplicate", "Фонд с таким названием уже существует.");
        }

        if (string.Equals(fund.Name, name, StringComparison.Ordinal))
        {
            var currentAvailableToDistribute = await CalculateAvailableToDistributeAsync(cancellationToken);
            var currentLinkedServices = await repository.GetLinkedServicesAsync([fund.Id], cancellationToken);
            return FundResult<FundDto>.Success(ToDto(fund, currentAvailableToDistribute, currentLinkedServices));
        }

        var oldName = fund.Name;
        fund.Name = name;
        fund.NormalizedName = normalizedName;
        fund.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddFundUpdatedAudit(fund, oldName, actorUserId);
        await repository.SaveChangesAsync(cancellationToken);

        var availableToDistribute = await CalculateAvailableToDistributeAsync(cancellationToken);
        var linkedServices = await repository.GetLinkedServicesAsync([fund.Id], cancellationToken);
        return FundResult<FundDto>.Success(ToDto(fund, availableToDistribute, linkedServices));
    }

    public async Task<FundResult<bool>> DeleteFundAsync(
        Guid fundId,
        DeleteFundRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            return FundResult<bool>.Failure(
                "fund_delete_reason_required",
                "Укажите причину удаления фонда.");
        }

        if (reason.Length > 1000)
        {
            return FundResult<bool>.Failure(
                "fund_delete_reason_too_long",
                "Причина удаления фонда не должна превышать 1000 символов.");
        }

        await using var allocationLock = await repository.AcquireAllocationLockAsync(cancellationToken);
        var fund = await repository.FindFundForUpdateAsync(fundId, cancellationToken);
        if (fund is null)
        {
            return FundResult<bool>.Failure("fund_not_found", "Фонд не найден.");
        }

        var linkedServices = await repository.GetLinkedServicesAsync([fund.Id], cancellationToken);
        if (linkedServices.Count > 0)
        {
            return FundResult<bool>.Failure(
                "fund_has_linked_services",
                $"Сначала переназначьте услуги фонда: {string.Join(", ", linkedServices.Select(service => service.ServiceName))}.");
        }

        var transferredAmount = fund.Balance;
        if (transferredAmount > 0m)
        {
            var transferOperation = new FundOperation
            {
                FundId = fund.Id,
                OperationKind = FundOperationKinds.Withdraw,
                Amount = transferredAmount,
                BalanceBefore = transferredAmount,
                BalanceAfter = 0m,
                Reason = $"Возврат остатка при удалении фонда: {reason}",
                ActorUserId = actorUserId
            };
            fund.Balance = 0m;
            repository.AddOperation(transferOperation);
            AddAudit(fund, transferOperation, actorUserId);
        }

        var incomeTypes = await repository.GetIncomeTypesForFundUpdateAsync(fund.Id, cancellationToken);
        foreach (var incomeType in incomeTypes)
        {
            incomeType.DestinationFundId = null;
            incomeType.DestinationFund = null;
            incomeType.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        fund.IsArchived = true;
        fund.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddFundDeletedAudit(fund, incomeTypes.Count, transferredAmount, actorUserId, reason);
        await repository.SaveChangesAsync(cancellationToken);

        return FundResult<bool>.Success(true);
    }

    public async Task<IReadOnlyList<FundOperationDto>> GetOperationsAsync(int limit, bool includeCanceled, CancellationToken cancellationToken)
    {
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var operations = await repository.GetRecentOperationsAsync(boundedLimit, includeCanceled, cancellationToken);

        return operations.Select(ToDto).ToList();
    }

    public async Task<FundOperationPageDto> GetOperationsPageAsync(int offset, int limit, bool includeCanceled, CancellationToken cancellationToken)
    {
        var boundedOffset = Math.Max(0, offset);
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var page = await repository.GetOperationsPageAsync(boundedOffset, boundedLimit, includeCanceled, cancellationToken);

        return new FundOperationPageDto(page.Items.Select(ToDto).ToList(), page.TotalCount, boundedOffset, boundedLimit);
    }

    public async Task<FundResult<FundOperationDto>> CreateOperationAsync(Guid fundId, CreateFundOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        await using var allocationLock = await repository.AcquireAllocationLockAsync(cancellationToken);
        var fund = await repository.FindFundForUpdateAsync(fundId, cancellationToken);
        if (fund is null)
        {
            return FundResult<FundOperationDto>.Failure("fund_not_found", "Фонд не найден.");
        }

        if (!fund.AllowOperations)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_not_allowed", "По этому фонду операции недоступны.");
        }

        var operationKind = request.OperationKind.Trim();
        if (!FundOperationKinds.IsSupported(operationKind))
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_kind_invalid", "Операция фонда должна быть deposit или withdraw.");
        }

        operationKind = FundOperationKinds.Normalize(operationKind);
        var reason = request.Reason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_reason_required", "Укажите причину операции фонда.");
        }

        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var balanceBefore = fund.Balance;
        var balanceAfter = operationKind == FundOperationKinds.Deposit
            ? balanceBefore + amount
            : balanceBefore - amount;
        if (operationKind == FundOperationKinds.Deposit)
        {
            var availableToDistribute = await CalculateAvailableToDistributeAsync(cancellationToken);
            if (amount > availableToDistribute)
            {
                return FundResult<FundOperationDto>.Failure(
                    "fund_distribution_amount_exceeded",
                    $"Сумма пополнения не может превышать доступную к распределению сумму {MoneyFormatting.Format(availableToDistribute)} руб.");
            }

        }

        if (balanceAfter < 0)
        {
            return FundResult<FundOperationDto>.Failure("fund_balance_insufficient", "Нельзя изъять из фонда больше собранной суммы.");
        }

        var operation = new FundOperation
        {
            FundId = fund.Id,
            OperationKind = operationKind,
            Amount = amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            Reason = reason,
            ActorUserId = actorUserId
        };

        fund.Balance = balanceAfter;
        fund.UpdatedAtUtc = DateTimeOffset.UtcNow;
        repository.AddOperation(operation);
        AddAudit(fund, operation, actorUserId);
        await repository.SaveChangesAsync(cancellationToken);

        operation.Fund = fund;
        return FundResult<FundOperationDto>.Success(ToDto(operation));
    }

    public async Task<FundResult<FundOperationDto>> UpdateOperationAsync(Guid operationId, UpdateFundOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var reason = request.Reason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_reason_required", "Укажите причину операции фонда.");
        }

        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0m)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_amount_invalid", "Сумма операции фонда должна быть больше нуля.");
        }

        await using var allocationLock = await repository.AcquireAllocationLockAsync(cancellationToken);
        var operation = await repository.FindOperationForUpdateAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_not_found", "Операция фонда не найдена.");
        }

        if (operation.IsCanceled)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_canceled", "Нельзя изменить отмененную операцию фонда.");
        }

        if (operation.SourceFinancialOperationId.HasValue)
        {
            return FundResult<FundOperationDto>.Failure(
                "fund_operation_managed_by_income",
                "Автоматическая операция фонда управляется связанным поступлением.");
        }

        var oldValues = ToOperationAuditValues(operation);
        var oldAmount = operation.Amount;
        var oldReason = operation.Reason;
        if (oldAmount == amount && string.Equals(oldReason, reason, StringComparison.Ordinal))
        {
            return FundResult<FundOperationDto>.Success(ToDto(operation));
        }

        var additionalAllocatedAmount = CalculateAdditionalAllocatedAmount(
            operation.OperationKind,
            oldAmount,
            amount);
        if (additionalAllocatedAmount > 0m)
        {
            var availableToDistribute = await CalculateAvailableToDistributeAsync(cancellationToken);
            if (additionalAllocatedAmount > availableToDistribute)
            {
                return FundResult<FundOperationDto>.Failure(
                    "fund_distribution_amount_exceeded",
                    $"Изменение операции не может увеличить распределенную сумму больше доступной к распределению суммы {MoneyFormatting.Format(availableToDistribute)} руб.");
            }

        }

        if (!await CanUpdateWithoutNegativeBalanceAsync(operation, amount, cancellationToken))
        {
            return FundResult<FundOperationDto>.Failure("fund_balance_insufficient", "Операцию фонда нельзя изменить: после изменения остаток фонда станет отрицательным.");
        }

        operation.Amount = amount;
        operation.Reason = reason;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await RecalculateFundBalancesAsync(operation.Fund, cancellationToken);
        AddUpdateAudit(operation.Fund, operation, actorUserId, oldValues);
        await repository.SaveChangesAsync(cancellationToken);
        return FundResult<FundOperationDto>.Success(ToDto(operation));
    }

    public async Task<FundResult<FundOperationDto>> CancelOperationAsync(Guid operationId, CancelFundOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var reason = request.Reason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_cancel_reason_required", "Для отмены операции фонда нужна причина.");
        }

        await using var allocationLock = await repository.AcquireAllocationLockAsync(cancellationToken);
        var operation = await repository.FindOperationForUpdateAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_not_found", "Операция фонда не найдена.");
        }

        if (operation.IsCanceled)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_already_canceled", "Операция фонда уже отменена.");
        }

        if (operation.SourceFinancialOperationId.HasValue)
        {
            return FundResult<FundOperationDto>.Failure(
                "fund_operation_managed_by_income",
                "Автоматическая операция фонда управляется связанным поступлением.");
        }

        if (operation.OperationKind == FundOperationKinds.Withdraw)
        {
            var availableToDistribute = await CalculateAvailableToDistributeAsync(cancellationToken);
            if (operation.Amount > availableToDistribute)
            {
                return FundResult<FundOperationDto>.Failure(
                    "fund_distribution_amount_exceeded",
                    $"Отмена изъятия не может превышать доступную к распределению сумму {MoneyFormatting.Format(availableToDistribute)} руб.");
            }
        }

        if (!await CanCancelWithoutNegativeBalanceAsync(operation, cancellationToken))
        {
            return FundResult<FundOperationDto>.Failure("fund_balance_insufficient", "Операцию фонда нельзя отменить: после отмены остаток фонда станет отрицательным.");
        }

        operation.IsCanceled = true;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        operation.Reason = AppendCancelReason(operation.Reason, reason);
        await RecalculateFundBalancesAsync(operation.Fund, cancellationToken);
        AddCancelAudit(operation.Fund, operation, actorUserId, reason);
        await repository.SaveChangesAsync(cancellationToken);
        return FundResult<FundOperationDto>.Success(ToDto(operation));
    }

    public async Task<FundResult<FundOperationDto>> RestoreOperationAsync(Guid operationId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        await using var allocationLock = await repository.AcquireAllocationLockAsync(cancellationToken);
        var operation = await repository.FindOperationForUpdateAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_not_found", "Операция фонда не найдена.");
        }

        if (!operation.IsCanceled)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_not_canceled", "Операция фонда уже активна.");
        }

        if (operation.SourceFinancialOperationId.HasValue)
        {
            return FundResult<FundOperationDto>.Failure(
                "fund_operation_managed_by_income",
                "Автоматическая операция фонда управляется связанным поступлением.");
        }

        if (operation.OperationKind == FundOperationKinds.Deposit)
        {
            var availableToDistribute = await CalculateAvailableToDistributeAsync(cancellationToken);
            if (operation.Amount > availableToDistribute)
            {
                return FundResult<FundOperationDto>.Failure(
                    "fund_distribution_amount_exceeded",
                    $"Сумма восстановления не может превышать доступную к распределению сумму {MoneyFormatting.Format(availableToDistribute)} руб.");
            }

        }

        if (!await CanRestoreWithoutNegativeBalanceAsync(operation, cancellationToken))
        {
            return FundResult<FundOperationDto>.Failure("fund_balance_insufficient", "Нельзя восстановить изъятие, если в фонде недостаточно собранной суммы.");
        }

        operation.IsCanceled = false;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await RecalculateFundBalancesAsync(operation.Fund, cancellationToken);
        AddRestoreAudit(operation.Fund, operation, actorUserId);
        await repository.SaveChangesAsync(cancellationToken);
        return FundResult<FundOperationDto>.Success(ToDto(operation));
    }

    private async Task EnsureDefaultFundsAsync(List<Fund> funds, CancellationToken cancellationToken)
    {
        var added = false;

        foreach (var definition in DefaultFunds)
        {
            var normalizedName = NormalizeName(definition.Name);
            if (funds.Any(fund =>
                (fund.IsSystem && fund.SortOrder == definition.SortOrder) ||
                fund.NormalizedName == normalizedName))
            {
                continue;
            }

            if (await repository.SystemFundSlotExistsAsync(definition.SortOrder, cancellationToken))
            {
                continue;
            }

            var fund = new Fund
            {
                Name = definition.Name,
                NormalizedName = normalizedName,
                SortOrder = definition.SortOrder,
                AllowOperations = definition.AllowOperations,
                IsSystem = true
            };
            repository.AddFund(fund);
            funds.Add(fund);
            added = true;
        }

        if (added)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<decimal> CalculateAvailableToDistributeAsync(CancellationToken cancellationToken)
    {
        var totals = await repository.GetTotalsAsync(cancellationToken);

        return MoneyMath.RoundMoney(Math.Max(totals.IncomeTotal - totals.ExpenseTotal - totals.AllocatedFundTotal, 0m));
    }

    private async Task RecalculateFundBalancesAsync(Fund fund, CancellationToken cancellationToken)
    {
        var operations = await GetOperationsOrderedByBusinessTimeAsync(fund.Id, trackChanges: true, cancellationToken);

        var balance = 0m;
        foreach (var operation in operations)
        {
            operation.BalanceBefore = balance;
            if (operation.IsCanceled || operation.SourceFinancialOperationId.HasValue)
            {
                operation.BalanceAfter = balance;
                continue;
            }

            balance = operation.OperationKind == FundOperationKinds.Deposit
                ? balance + operation.Amount
                : balance - operation.Amount;
            operation.BalanceAfter = balance;
        }

        fund.Balance = MoneyMath.RoundMoney(balance);
        fund.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private async Task<bool> CanCancelWithoutNegativeBalanceAsync(FundOperation cancelingOperation, CancellationToken cancellationToken)
    {
        var operations = await GetOperationsOrderedByBusinessTimeAsync(cancelingOperation.FundId, trackChanges: false, cancellationToken);

        var balance = 0m;
        foreach (var operation in operations)
        {
            if (operation.Id == cancelingOperation.Id || operation.IsCanceled)
            {
                continue;
            }

            balance = operation.OperationKind == FundOperationKinds.Deposit
                ? balance + operation.Amount
                : balance - operation.Amount;

            if (balance < 0m)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> CanUpdateWithoutNegativeBalanceAsync(FundOperation updatingOperation, decimal newAmount, CancellationToken cancellationToken)
    {
        var operations = await GetOperationsOrderedByBusinessTimeAsync(updatingOperation.FundId, trackChanges: false, cancellationToken);

        var balance = 0m;
        foreach (var operation in operations)
        {
            if (operation.IsCanceled)
            {
                continue;
            }

            var amount = operation.Id == updatingOperation.Id ? newAmount : operation.Amount;
            balance = operation.OperationKind == FundOperationKinds.Deposit
                ? balance + amount
                : balance - amount;

            if (balance < 0m)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> CanRestoreWithoutNegativeBalanceAsync(FundOperation restoringOperation, CancellationToken cancellationToken)
    {
        var operations = await GetOperationsOrderedByBusinessTimeAsync(restoringOperation.FundId, trackChanges: false, cancellationToken);

        var balance = 0m;
        foreach (var operation in operations)
        {
            var isCanceled = operation.Id == restoringOperation.Id ? false : operation.IsCanceled;
            if (isCanceled)
            {
                continue;
            }

            balance = operation.OperationKind == FundOperationKinds.Deposit
                ? balance + operation.Amount
                : balance - operation.Amount;
            if (balance < 0)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<List<FundOperation>> GetOperationsOrderedByBusinessTimeAsync(Guid fundId, bool trackChanges, CancellationToken cancellationToken)
    {
        return (await repository.GetOperationsOrderedAsync(fundId, trackChanges, cancellationToken)).ToList();
    }

    private void AddAudit(Fund fund, FundOperation operation, Guid? actorUserId)
    {
        var actionLabel = operation.OperationKind == FundOperationKinds.Deposit ? "Пополнен" : "Выполнено изъятие из";
        auditEventWriter.Add(new AuditEventWriteRequest(
            ActorUserId: actorUserId,
            Action: operation.OperationKind == FundOperationKinds.Deposit ? "fund.operation_deposited" : "fund.operation_withdrawn",
            EntityType: "fund_operation",
            EntityId: operation.Id.ToString(),
            Summary: $"{actionLabel} фонда {fund.Name} на сумму {MoneyFormatting.Format(operation.Amount)} руб.",
            Section: "funds",
            ActionKind: "create",
            EntityDisplayName: fund.Name,
            Reason: operation.Reason,
            OldValues: new Dictionary<string, object?>
            {
                ["balance"] = operation.BalanceBefore
            },
            NewValues: new Dictionary<string, object?>
            {
                ["balance"] = operation.BalanceAfter
            },
            FieldLabels: new Dictionary<string, string>
            {
                ["balance"] = "Собранная сумма"
            },
            Metadata: new Dictionary<string, object?>
            {
                ["fundId"] = fund.Id,
                ["fundName"] = fund.Name,
                ["operationKind"] = operation.OperationKind,
                ["amount"] = operation.Amount
            }));
    }

    private void AddFundCreatedAudit(Fund fund, Guid? actorUserId)
    {
        auditEventWriter.Add(new AuditEventWriteRequest(
            ActorUserId: actorUserId,
            Action: "fund.created",
            EntityType: "fund",
            EntityId: fund.Id.ToString(),
            Summary: $"Создан фонд {fund.Name}.",
            Section: "funds",
            ActionKind: "create",
            EntityDisplayName: fund.Name,
            NewValues: new Dictionary<string, object?>
            {
                ["name"] = fund.Name
            },
            FieldLabels: FundFieldLabels));
    }

    private void AddFundUpdatedAudit(Fund fund, string oldName, Guid? actorUserId)
    {
        auditEventWriter.Add(new AuditEventWriteRequest(
            ActorUserId: actorUserId,
            Action: "fund.updated",
            EntityType: "fund",
            EntityId: fund.Id.ToString(),
            Summary: $"Переименован фонд {oldName} в {fund.Name}.",
            Section: "funds",
            ActionKind: "update",
            EntityDisplayName: fund.Name,
            OldValues: new Dictionary<string, object?>
            {
                ["name"] = oldName
            },
            NewValues: new Dictionary<string, object?>
            {
                ["name"] = fund.Name
            },
            FieldLabels: FundFieldLabels));
    }

    private void AddFundDeletedAudit(
        Fund fund,
        int detachedIncomeTypeCount,
        decimal transferredAmount,
        Guid? actorUserId,
        string reason)
    {
        auditEventWriter.Add(new AuditEventWriteRequest(
            ActorUserId: actorUserId,
            Action: "fund.archived",
            EntityType: "fund",
            EntityId: fund.Id.ToString(),
            Summary: transferredAmount > 0m
                ? $"Удален фонд {fund.Name}; остаток {MoneyFormatting.Format(transferredAmount)} руб. возвращен в нераспределенную сумму."
                : $"Удален фонд {fund.Name}.",
            Section: "funds",
            ActionKind: "archive",
            EntityDisplayName: fund.Name,
            Reason: reason,
            OldValues: new Dictionary<string, object?>
            {
                ["balance"] = transferredAmount,
                ["isArchived"] = false
            },
            NewValues: new Dictionary<string, object?>
            {
                ["balance"] = 0m,
                ["isArchived"] = true
            },
            FieldLabels: FundFieldLabels,
            Metadata: new Dictionary<string, object?>
            {
                ["detachedIncomeTypeCount"] = detachedIncomeTypeCount,
                ["returnedToUnallocatedAmount"] = transferredAmount
            }));
    }

    private void AddCancelAudit(Fund fund, FundOperation operation, Guid? actorUserId, string reason)
    {
        AddOperationStatusAudit(
            fund,
            operation,
            actorUserId,
            "fund.operation_canceled",
            "cancel",
            $"Отменена операция фонда {fund.Name}: {FormatOperationKind(operation.OperationKind)} на сумму {MoneyFormatting.Format(operation.Amount)} руб.",
            reason);
    }

    private void AddRestoreAudit(Fund fund, FundOperation operation, Guid? actorUserId)
    {
        AddOperationStatusAudit(
            fund,
            operation,
            actorUserId,
            "fund.operation_restored",
            "restore",
            $"Восстановлена операция фонда {fund.Name}: {FormatOperationKind(operation.OperationKind)} на сумму {MoneyFormatting.Format(operation.Amount)} руб.",
            null);
    }

    private void AddUpdateAudit(Fund fund, FundOperation operation, Guid? actorUserId, IReadOnlyDictionary<string, object?> oldValues)
    {
        auditEventWriter.Add(new AuditEventWriteRequest(
            ActorUserId: actorUserId,
            Action: "fund.operation_updated",
            EntityType: "fund_operation",
            EntityId: operation.Id.ToString(),
            Summary: $"Изменена операция фонда {fund.Name}: {FormatOperationKind(operation.OperationKind)} на сумму {MoneyFormatting.Format(operation.Amount)} руб.",
            Section: "funds",
            ActionKind: "update",
            EntityDisplayName: fund.Name,
            Reason: operation.Reason,
            OldValues: oldValues,
            NewValues: ToOperationAuditValues(operation),
            FieldLabels: OperationFieldLabels,
            Metadata: new Dictionary<string, object?>
            {
                ["fundId"] = fund.Id,
                ["fundName"] = fund.Name,
                ["operationKind"] = operation.OperationKind,
                ["amount"] = operation.Amount
            }));
    }

    private void AddOperationStatusAudit(Fund fund, FundOperation operation, Guid? actorUserId, string action, string actionKind, string summary, string? reason)
    {
        auditEventWriter.Add(new AuditEventWriteRequest(
            ActorUserId: actorUserId,
            Action: action,
            EntityType: "fund_operation",
            EntityId: operation.Id.ToString(),
            Summary: summary,
            Section: "funds",
            ActionKind: actionKind,
            EntityDisplayName: fund.Name,
            Reason: reason,
            Metadata: new Dictionary<string, object?>
            {
                ["fundId"] = fund.Id,
                ["fundName"] = fund.Name,
                ["operationKind"] = operation.OperationKind,
                ["amount"] = operation.Amount,
                ["isCanceled"] = operation.IsCanceled
            }));
    }

    private static FundDto ToDto(
        Fund fund,
        decimal availableToDistribute,
        IEnumerable<FundLinkedServiceData> linkedServices)
    {
        return new FundDto(
            fund.Id,
            fund.Name,
            fund.Balance,
            availableToDistribute,
            fund.SortOrder,
            fund.AllowOperations,
            fund.IsSystem,
            linkedServices
                .Select(service => new FundLinkedServiceDto(service.ServiceId, service.ServiceName))
                .ToList());
    }

    private static FundOperationDto ToDto(FundOperation operation)
    {
        return new FundOperationDto(
            operation.Id,
            operation.FundId,
            operation.Fund.Name,
            operation.OperationKind,
            operation.Amount,
            operation.BalanceBefore,
            operation.BalanceAfter,
            operation.Reason,
            operation.CreatedAtUtc,
            operation.IsCanceled,
            operation.SourceFinancialOperationId.HasValue);
    }

    private static IReadOnlyDictionary<string, object?> ToOperationAuditValues(FundOperation operation)
    {
        return new Dictionary<string, object?>
        {
            ["amount"] = operation.Amount,
            ["reason"] = operation.Reason,
            ["balanceBefore"] = operation.BalanceBefore,
            ["balanceAfter"] = operation.BalanceAfter
        };
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().ToUpperInvariant();
    }

    private static (string? Name, string? ErrorCode, string? ErrorMessage) ValidateFundName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, "fund_name_required", "Укажите название фонда.");
        }

        return name.Length > 200
            ? (null, "fund_name_too_long", "Название фонда не должно превышать 200 символов.")
            : (name, null, null);
    }

    private static string AppendCancelReason(string reason, string cancelReason)
    {
        var cancelComment = $"Отменено: {cancelReason}";
        return string.IsNullOrWhiteSpace(reason) ? cancelComment : $"{reason}{Environment.NewLine}{cancelComment}";
    }

    private static string FormatOperationKind(string operationKind)
    {
        return operationKind == FundOperationKinds.Deposit ? "пополнение" : "изъятие";
    }

    private static decimal CalculateAdditionalAllocatedAmount(
        string operationKind,
        decimal oldAmount,
        decimal newAmount)
    {
        var difference = operationKind == FundOperationKinds.Deposit
            ? newAmount - oldAmount
            : oldAmount - newAmount;
        return MoneyMath.RoundMoney(Math.Max(difference, 0m));
    }

    private sealed record DefaultFundDefinition(string Name, int SortOrder, bool AllowOperations);

    private static readonly IReadOnlyDictionary<string, string> OperationFieldLabels = new Dictionary<string, string>
    {
        ["amount"] = "Сумма",
        ["reason"] = "Основание",
        ["balanceBefore"] = "Собранная сумма до",
        ["balanceAfter"] = "Собранная сумма после"
    };

    private static readonly IReadOnlyDictionary<string, string> FundFieldLabels = new Dictionary<string, string>
    {
        ["balance"] = "Собранная сумма",
        ["name"] = "Название фонда",
        ["isArchived"] = "Удален"
    };
}
