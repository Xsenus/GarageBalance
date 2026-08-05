using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Funds;

public sealed class IncomeFundAssignmentService(
    IFundRepository repository,
    IAuditEventWriter auditEventWriter) : IIncomeFundAssignmentService
{
    public Task<IAsyncDisposable> AcquireUpdateLockAsync(CancellationToken cancellationToken) =>
        repository.AcquireAllocationLockAsync(cancellationToken);

    public async Task<IncomeFundAssignmentResult> CreateAsync(
        FinancialOperation sourceOperation,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var destinationFundId = sourceOperation.IncomeType?.DestinationFundId;
        if (!destinationFundId.HasValue)
        {
            return IncomeFundAssignmentResult.Success();
        }

        return await CreateAssignmentAsync(
            sourceOperation,
            destinationFundId.Value,
            sourceOperation.IncomeType?.Name ?? "Без названия",
            sourceOperation.Amount,
            actorUserId,
            cancellationToken);
    }

    private async Task<IncomeFundAssignmentResult> CreateAssignmentAsync(
        FinancialOperation sourceOperation,
        Guid destinationFundId,
        string incomeTypeName,
        decimal amount,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await repository.FindIncomeAssignmentForUpdateAsync(sourceOperation.Id, cancellationToken);
        if (existing is not null)
        {
            return IncomeFundAssignmentResult.Failure(
                "income_fund_assignment_duplicate",
                "Для поступления уже существует операция назначения фонда.");
        }

        var fund = await repository.FindFundForUpdateAsync(destinationFundId, cancellationToken);
        if (fund is null)
        {
            return IncomeFundAssignmentResult.Failure(
                "income_destination_fund_not_found",
                "Фонд назначения поступления не найден.");
        }

        var assignment = new FundOperation
        {
            FundId = fund.Id,
            Fund = fund,
            SourceFinancialOperationId = sourceOperation.Id,
            SourceFinancialOperation = sourceOperation,
            OperationKind = FundOperationKinds.Deposit,
            Amount = MoneyMath.RoundMoney(amount),
            BalanceBefore = fund.Balance,
            BalanceAfter = MoneyMath.RoundMoney(fund.Balance + MoneyMath.RoundMoney(amount)),
            Reason = BuildReason(incomeTypeName),
            ActorUserId = actorUserId,
            CreatedAtUtc = sourceOperation.CreatedAtUtc
        };
        fund.Balance = assignment.BalanceAfter;
        fund.UpdatedAtUtc = DateTimeOffset.UtcNow;
        repository.AddOperation(assignment);
        AddAudit("fund.income_assignment_created", "create", assignment, actorUserId, null);
        return IncomeFundAssignmentResult.Success();
    }

    public async Task<IncomeFundAssignmentResult> UpdateAsync(
        FinancialOperation sourceOperation,
        Guid? destinationFundId,
        string incomeTypeName,
        decimal amount,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var assignment = await repository.FindIncomeAssignmentForUpdateAsync(sourceOperation.Id, cancellationToken);
        if (assignment is null)
        {
            if (!destinationFundId.HasValue)
            {
                return IncomeFundAssignmentResult.Success();
            }

            return await CreateAssignmentAsync(
                sourceOperation,
                destinationFundId.Value,
                incomeTypeName,
                amount,
                actorUserId,
                cancellationToken);
        }

        var normalizedAmount = MoneyMath.RoundMoney(amount);

        var oldFund = assignment.Fund;
        var oldFundOperations = (await repository.GetOperationsSinceAsync(
            oldFund.Id,
            assignment.CreatedAtUtc,
            cancellationToken)).ToList();
        Fund? destinationFund = null;
        List<FundOperation>? destinationOperations = null;
        if (destinationFundId.HasValue)
        {
            destinationFund = destinationFundId == oldFund.Id
                ? oldFund
                : await repository.FindFundForUpdateAsync(destinationFundId.Value, cancellationToken);
            if (destinationFund is null)
            {
                return IncomeFundAssignmentResult.Failure(
                    "income_destination_fund_not_found",
                    "Фонд назначения поступления не найден.");
            }

            destinationOperations = destinationFund.Id == oldFund.Id
                ? oldFundOperations
                : (await repository.GetOperationsSinceAsync(
                    destinationFund.Id,
                    assignment.CreatedAtUtc,
                    cancellationToken)).ToList();
        }

        var oldValues = Snapshot(assignment);
        var previousFundId = assignment.FundId;
        var previousFund = assignment.Fund;
        var previousAmount = assignment.Amount;
        var previousReason = assignment.Reason;
        var previousCanceled = assignment.IsCanceled;
        var previousUpdatedAtUtc = assignment.UpdatedAtUtc;
        var destinationOpeningBalance = destinationOperations is { Count: > 0 }
            ? destinationOperations[0].BalanceBefore
            : destinationFund?.Balance ?? 0m;
        assignment.FundId = destinationFund?.Id ?? oldFund.Id;
        assignment.Fund = destinationFund ?? oldFund;
        assignment.Amount = normalizedAmount;
        assignment.Reason = BuildReason(incomeTypeName);
        assignment.IsCanceled = !destinationFundId.HasValue;
        assignment.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var oldFundTail = destinationFund is null || destinationFund.Id != oldFund.Id
            ? oldFundOperations.Where(operation => operation.Id != assignment.Id).ToList()
            : oldFundOperations;
        var destinationFundTail = destinationFund is null
            ? null
            : destinationFund.Id == oldFund.Id
                ? oldFundTail
                : destinationOperations!.Append(assignment).ToList();
        var oldOpeningBalance = oldFundOperations.Count > 0
            ? oldFundOperations[0].BalanceBefore
            : oldFund.Balance;
        if (!CanRecalculateTail(oldFundTail, oldOpeningBalance) ||
            (destinationFundTail is not null && !CanRecalculateTail(destinationFundTail, destinationOpeningBalance)))
        {
            assignment.FundId = previousFundId;
            assignment.Fund = previousFund;
            assignment.Amount = previousAmount;
            assignment.Reason = previousReason;
            assignment.IsCanceled = previousCanceled;
            assignment.UpdatedAtUtc = previousUpdatedAtUtc;
            return IncomeFundAssignmentResult.Failure(
                "fund_balance_insufficient",
                "Поступление нельзя изменить: после пересчета остаток связанного фонда станет отрицательным.");
        }

        if (destinationFund is null || destinationFund.Id != oldFund.Id)
        {
            RecalculateTail(oldFund, oldFundTail, oldOpeningBalance);
        }

        if (destinationFund is not null)
        {
            RecalculateTail(
                destinationFund,
                destinationFundTail!,
                destinationOpeningBalance);
        }

        AddAudit("fund.income_assignment_updated", "update", assignment, actorUserId, null, oldValues);
        return IncomeFundAssignmentResult.Success();
    }

    public async Task<IncomeFundAssignmentResult> CancelAsync(
        FinancialOperation sourceOperation,
        string reason,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var assignment = await repository.FindIncomeAssignmentForUpdateAsync(sourceOperation.Id, cancellationToken);
        if (assignment is null || assignment.IsCanceled)
        {
            return IncomeFundAssignmentResult.Success();
        }

        var operations = (await repository.GetOperationsFromAsync(
            assignment.FundId,
            assignment.Id,
            assignment.CreatedAtUtc,
            cancellationToken)).ToList();
        assignment.IsCanceled = true;
        if (!CanRecalculateTail(operations, assignment.BalanceBefore))
        {
            assignment.IsCanceled = false;
            return IncomeFundAssignmentResult.Failure(
                "fund_balance_insufficient",
                "Поступление нельзя отменить: после пересчета остаток связанного фонда станет отрицательным.");
        }

        assignment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        RecalculateTail(assignment.Fund, operations, assignment.BalanceBefore);
        AddAudit("fund.income_assignment_canceled", "cancel", assignment, actorUserId, reason);
        return IncomeFundAssignmentResult.Success();
    }

    public async Task<IncomeFundAssignmentResult> RestoreAsync(
        FinancialOperation sourceOperation,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var assignment = await repository.FindIncomeAssignmentForUpdateAsync(sourceOperation.Id, cancellationToken);
        if (assignment is null)
        {
            return await CreateAsync(sourceOperation, actorUserId, cancellationToken);
        }

        if (!assignment.IsCanceled)
        {
            return IncomeFundAssignmentResult.Success();
        }

        assignment.IsCanceled = false;
        assignment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var operations = (await repository.GetOperationsFromAsync(
            assignment.FundId,
            assignment.Id,
            assignment.CreatedAtUtc,
            cancellationToken)).ToList();
        RecalculateTail(assignment.Fund, operations, assignment.BalanceBefore);
        AddAudit("fund.income_assignment_restored", "restore", assignment, actorUserId, null);
        return IncomeFundAssignmentResult.Success();
    }

    private static void RecalculateTail(
        Fund fund,
        IEnumerable<FundOperation> source,
        decimal openingBalance)
    {
        var balance = openingBalance;
        foreach (var operation in source.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id))
        {
            operation.BalanceBefore = balance;
            if (operation.IsCanceled)
            {
                operation.BalanceAfter = balance;
                continue;
            }

            balance += operation.OperationKind == FundOperationKinds.Deposit ? operation.Amount : -operation.Amount;
            operation.BalanceAfter = MoneyMath.RoundMoney(balance);
        }

        fund.Balance = MoneyMath.RoundMoney(balance);
        fund.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static bool CanRecalculateTail(IEnumerable<FundOperation> source, decimal openingBalance)
    {
        var balance = openingBalance;
        foreach (var operation in source.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id))
        {
            if (operation.IsCanceled)
            {
                continue;
            }

            balance += operation.OperationKind == FundOperationKinds.Deposit
                ? operation.Amount
                : -operation.Amount;
            if (balance < 0m)
            {
                return false;
            }
        }

        return true;
    }

    private void AddAudit(
        string action,
        string actionKind,
        FundOperation assignment,
        Guid? actorUserId,
        string? reason,
        IReadOnlyDictionary<string, object?>? oldValues = null)
    {
        auditEventWriter.Add(new AuditEventWriteRequest(
            ActorUserId: actorUserId,
            Action: action,
            EntityType: "fund_operation",
            EntityId: assignment.Id.ToString(),
            Summary: $"Назначение поступления в фонд {assignment.Fund.Name}: {MoneyFormatting.Format(assignment.Amount)} руб.",
            Section: "funds",
            ActionKind: actionKind,
            EntityDisplayName: assignment.Fund.Name,
            Reason: reason,
            OldValues: oldValues,
            NewValues: Snapshot(assignment),
            FieldLabels: new Dictionary<string, string>
            {
                ["fund"] = "Фонд",
                ["amount"] = "Сумма",
                ["isCanceled"] = "Статус"
            },
            Metadata: new Dictionary<string, object?>
            {
                ["fundId"] = assignment.FundId,
                ["sourceFinancialOperationId"] = assignment.SourceFinancialOperationId,
                ["automatic"] = true
            },
            RelatedDocumentId: assignment.SourceFinancialOperationId?.ToString()));
    }

    private static IReadOnlyDictionary<string, object?> Snapshot(FundOperation assignment) =>
        new Dictionary<string, object?>
        {
            ["fund"] = assignment.Fund.Name,
            ["amount"] = assignment.Amount,
            ["isCanceled"] = assignment.IsCanceled
        };

    private static string BuildReason(string? incomeTypeName) =>
        $"Автоматическое назначение поступления «{incomeTypeName ?? "Без названия"}»";
}
