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
            BalanceAfter = fund.Balance,
            Reason = BuildReason(incomeTypeName),
            ActorUserId = actorUserId,
            CreatedAtUtc = sourceOperation.CreatedAtUtc
        };
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
        var releasedAmount = MoneyMath.RoundMoney(assignment.Amount - normalizedAmount);
        if (releasedAmount > 0m &&
            releasedAmount > await GetAvailableToDistributeAsync(cancellationToken))
        {
            return IncomeFundAssignmentResult.Failure(
                "fund_balance_insufficient",
                "Поступление нельзя уменьшить: часть общей нераспределенной суммы уже направлена в фонды.");
        }

        var oldFund = assignment.Fund;
        var oldFundOperations = (await repository.GetOperationsOrderedAsync(oldFund.Id, trackChanges: true, cancellationToken)).ToList();
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
        assignment.Amount = normalizedAmount;
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

        if (assignment.Amount > await GetAvailableToDistributeAsync(cancellationToken))
        {
            return IncomeFundAssignmentResult.Failure(
                "fund_balance_insufficient",
                "Поступление нельзя отменить: часть общей нераспределенной суммы уже направлена в фонды.");
        }

        var operations = (await repository.GetOperationsOrderedAsync(assignment.FundId, trackChanges: true, cancellationToken)).ToList();
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

    private async Task<decimal> GetAvailableToDistributeAsync(CancellationToken cancellationToken)
    {
        var totals = await repository.GetTotalsAsync(cancellationToken);
        return MoneyMath.RoundMoney(Math.Max(
            totals.IncomeTotal - totals.ExpenseTotal - totals.AllocatedFundTotal,
            0m));
    }

    private static void Recalculate(Fund fund, IEnumerable<FundOperation> source)
    {
        var balance = 0m;
        foreach (var operation in source.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id))
        {
            operation.BalanceBefore = balance;
            if (operation.IsCanceled || operation.SourceFinancialOperationId.HasValue)
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
