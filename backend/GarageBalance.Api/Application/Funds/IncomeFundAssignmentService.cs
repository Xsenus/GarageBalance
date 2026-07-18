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
            BalanceAfter = MoneyMath.RoundMoney(fund.Balance + amount),
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

        var oldFund = assignment.Fund;
        var oldFundOperations = (await repository.GetOperationsOrderedAsync(oldFund.Id, trackChanges: true, cancellationToken)).ToList();
        var remainsInOldFund = destinationFundId == oldFund.Id;
        if (!CanKeepBalancesNonNegative(
                oldFundOperations,
                assignment.Id,
                remainsInOldFund ? amount : 0m,
                remainsInOldFund && destinationFundId.HasValue))
        {
            return IncomeFundAssignmentResult.Failure(
                "fund_balance_insufficient",
                "Поступление нельзя изменить: связанная сумма фонда уже использована.");
        }

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
                : (await repository.GetOperationsOrderedAsync(destinationFund.Id, trackChanges: true, cancellationToken)).ToList();
        }

        var oldValues = Snapshot(assignment);
        assignment.FundId = destinationFund?.Id ?? oldFund.Id;
        assignment.Fund = destinationFund ?? oldFund;
        assignment.Amount = MoneyMath.RoundMoney(amount);
        assignment.Reason = BuildReason(incomeTypeName);
        assignment.IsCanceled = !destinationFundId.HasValue;
        assignment.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (destinationFund is null || destinationFund.Id != oldFund.Id)
        {
            Recalculate(oldFund, oldFundOperations.Where(operation => operation.Id != assignment.Id));
        }

        if (destinationFund is not null)
        {
            var operations = destinationOperations!;
            if (destinationFund.Id != oldFund.Id)
            {
                operations.Add(assignment);
            }
            Recalculate(destinationFund, operations);
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

        var operations = (await repository.GetOperationsOrderedAsync(assignment.FundId, trackChanges: true, cancellationToken)).ToList();
        if (!CanKeepBalancesNonNegative(operations, assignment.Id, 0m, active: false))
        {
            return IncomeFundAssignmentResult.Failure(
                "fund_balance_insufficient",
                "Поступление нельзя отменить: связанная сумма фонда уже использована.");
        }

        assignment.IsCanceled = true;
        assignment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Recalculate(assignment.Fund, operations);
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
        var operations = (await repository.GetOperationsOrderedAsync(assignment.FundId, trackChanges: true, cancellationToken)).ToList();
        Recalculate(assignment.Fund, operations);
        AddAudit("fund.income_assignment_restored", "restore", assignment, actorUserId, null);
        return IncomeFundAssignmentResult.Success();
    }

    private static bool CanKeepBalancesNonNegative(
        IReadOnlyList<FundOperation> operations,
        Guid assignmentId,
        decimal assignmentAmount,
        bool active)
    {
        var balance = 0m;
        foreach (var operation in operations.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id))
        {
            var isCanceled = operation.Id == assignmentId ? !active : operation.IsCanceled;
            if (isCanceled)
            {
                continue;
            }

            var amount = operation.Id == assignmentId ? assignmentAmount : operation.Amount;
            balance += operation.OperationKind == FundOperationKinds.Deposit ? amount : -amount;
            if (balance < 0m)
            {
                return false;
            }
        }

        return true;
    }

    private static void Recalculate(Fund fund, IEnumerable<FundOperation> source)
    {
        var balance = 0m;
        foreach (var operation in source.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id))
        {
            if (operation.IsCanceled)
            {
                continue;
            }

            operation.BalanceBefore = balance;
            balance += operation.OperationKind == FundOperationKinds.Deposit ? operation.Amount : -operation.Amount;
            operation.BalanceAfter = MoneyMath.RoundMoney(balance);
        }

        fund.Balance = MoneyMath.RoundMoney(balance);
        fund.UpdatedAtUtc = DateTimeOffset.UtcNow;
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
