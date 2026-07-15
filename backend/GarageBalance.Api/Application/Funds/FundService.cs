using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Funds;

public sealed class FundService(IFundRepository repository, IAuditEventWriter auditEventWriter) : IFundService
{
    private static readonly IReadOnlyList<DefaultFundDefinition> DefaultFunds =
    [
        new("Электроэнергия", 10, true),
        new("Водоснабжение", 20, true),
        new("Вывоз мусора", 30, true),
        new("Наружное освещение", 40, true),
        new("Членские взносы", 50, false),
        new("Целевые взносы", 60, true),
        new("Прочее", 70, false)
    ];

    public async Task<IReadOnlyList<FundDto>> GetFundsAsync(CancellationToken cancellationToken)
    {
        var funds = (await repository.GetFundsAsync(cancellationToken)).ToList();
        await EnsureDefaultFundsAsync(funds, cancellationToken);
        var availableToDistribute = await CalculateAvailableToDistributeAsync(cancellationToken);

        return funds
            .OrderBy(fund => fund.SortOrder)
            .ThenBy(fund => fund.Name)
            .Select(fund => ToDto(fund, availableToDistribute))
            .ToList();
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

        var operation = await repository.FindOperationForUpdateAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_not_found", "Операция фонда не найдена.");
        }

        if (operation.IsCanceled)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_canceled", "Нельзя изменить отмененную операцию фонда.");
        }

        var oldValues = ToOperationAuditValues(operation);
        var oldAmount = operation.Amount;
        var oldReason = operation.Reason;
        if (oldAmount == amount && string.Equals(oldReason, reason, StringComparison.Ordinal))
        {
            return FundResult<FundOperationDto>.Success(ToDto(operation));
        }

        if (operation.OperationKind == FundOperationKinds.Deposit && amount > oldAmount)
        {
            var availableToDistribute = await CalculateAvailableToDistributeAsync(cancellationToken);
            var additionalAmount = amount - oldAmount;
            if (additionalAmount > availableToDistribute)
            {
                return FundResult<FundOperationDto>.Failure(
                    "fund_distribution_amount_exceeded",
                    $"Сумма увеличения пополнения не может превышать доступную к распределению сумму {MoneyFormatting.Format(availableToDistribute)} руб.");
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

        var operation = await repository.FindOperationForUpdateAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_not_found", "Операция фонда не найдена.");
        }

        if (operation.IsCanceled)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_already_canceled", "Операция фонда уже отменена.");
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
        var operation = await repository.FindOperationForUpdateAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_not_found", "Операция фонда не найдена.");
        }

        if (!operation.IsCanceled)
        {
            return FundResult<FundOperationDto>.Failure("fund_operation_not_canceled", "Операция фонда уже активна.");
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
        var existing = funds.Select(fund => fund.NormalizedName).ToHashSet(StringComparer.Ordinal);
        var added = false;

        foreach (var definition in DefaultFunds)
        {
            var normalizedName = NormalizeName(definition.Name);
            if (existing.Contains(normalizedName))
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
            if (operation.IsCanceled)
            {
                continue;
            }

            operation.BalanceBefore = balance;
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

    private static FundDto ToDto(Fund fund, decimal availableToDistribute)
    {
        return new FundDto(fund.Id, fund.Name, fund.Balance, availableToDistribute, fund.SortOrder, fund.AllowOperations, fund.IsSystem);
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
            operation.IsCanceled);
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

    private static string AppendCancelReason(string reason, string cancelReason)
    {
        var cancelComment = $"Отменено: {cancelReason}";
        return string.IsNullOrWhiteSpace(reason) ? cancelComment : $"{reason}{Environment.NewLine}{cancelComment}";
    }

    private static string FormatOperationKind(string operationKind)
    {
        return operationKind == FundOperationKinds.Deposit ? "пополнение" : "изъятие";
    }

    private sealed record DefaultFundDefinition(string Name, int SortOrder, bool AllowOperations);

    private static readonly IReadOnlyDictionary<string, string> OperationFieldLabels = new Dictionary<string, string>
    {
        ["amount"] = "Сумма",
        ["reason"] = "Основание",
        ["balanceBefore"] = "Собранная сумма до",
        ["balanceAfter"] = "Собранная сумма после"
    };
}
