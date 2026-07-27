using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Funds;

public sealed class ExpenseFundDisbursementService(
    IFundRepository repository,
    IAuditEventWriter auditEventWriter) : IExpenseFundDisbursementService
{
    public Task<IAsyncDisposable> AcquireUpdateLockAsync(CancellationToken cancellationToken) =>
        repository.AcquireAllocationLockAsync(cancellationToken);

    public async Task<ExpenseFundDisbursementResult> CreateAsync(
        FinancialOperation sourceOperation,
        string supplierName,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (!sourceOperation.ExpenseFundId.HasValue)
        {
            return ExpenseFundDisbursementResult.Failure(
                "supplier_service_expense_fund_not_configured",
                $"Для выплаты поставщику «{supplierName}» не настроен фонд расходования.");
        }

        var existing = await repository.FindIncomeAssignmentForUpdateAsync(sourceOperation.Id, cancellationToken);
        if (existing is not null)
        {
            return ExpenseFundDisbursementResult.Failure(
                "expense_fund_disbursement_duplicate",
                "Для выплаты уже существует операция списания из фонда.");
        }

        var fund = await repository.FindFundForUpdateAsync(sourceOperation.ExpenseFundId.Value, cancellationToken);
        if (fund is null)
        {
            return ExpenseFundDisbursementResult.Failure(
                "supplier_service_expense_fund_not_found",
                "Фонд расходования услуги не найден.");
        }

        var amount = MoneyMath.RoundMoney(sourceOperation.Amount);
        if (amount > fund.Balance)
        {
            return InsufficientBalance(fund.Balance);
        }

        var disbursement = new FundOperation
        {
            FundId = fund.Id,
            Fund = fund,
            SourceFinancialOperationId = sourceOperation.Id,
            SourceFinancialOperation = sourceOperation,
            OperationKind = FundOperationKinds.Withdraw,
            Amount = amount,
            BalanceBefore = fund.Balance,
            BalanceAfter = MoneyMath.RoundMoney(fund.Balance - amount),
            Reason = BuildReason(supplierName, sourceOperation.ExpenseType?.Name),
            ActorUserId = actorUserId,
            CreatedAtUtc = sourceOperation.CreatedAtUtc
        };
        fund.Balance = disbursement.BalanceAfter;
        fund.UpdatedAtUtc = DateTimeOffset.UtcNow;
        repository.AddOperation(disbursement);
        AddAudit("fund.expense_disbursement_created", "create", disbursement, actorUserId, null);
        return ExpenseFundDisbursementResult.Success();
    }

    public async Task<ExpenseFundDisbursementResult> UpdateAsync(
        FinancialOperation sourceOperation,
        Guid expenseFundId,
        string supplierName,
        decimal amount,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var disbursement = await repository.FindIncomeAssignmentForUpdateAsync(sourceOperation.Id, cancellationToken);
        if (disbursement is null || disbursement.OperationKind != FundOperationKinds.Withdraw)
        {
            return ExpenseFundDisbursementResult.Failure(
                "expense_fund_disbursement_not_found",
                "Связанное списание из фонда не найдено. Отмените выплату и создайте её заново.");
        }

        var normalizedAmount = MoneyMath.RoundMoney(amount);
        var oldFund = disbursement.Fund;
        var oldOperations = (await repository.GetOperationsOrderedAsync(oldFund.Id, trackChanges: true, cancellationToken)).ToList();
        var destinationFund = expenseFundId == oldFund.Id
            ? oldFund
            : await repository.FindFundForUpdateAsync(expenseFundId, cancellationToken);
        if (destinationFund is null)
        {
            return ExpenseFundDisbursementResult.Failure(
                "supplier_service_expense_fund_not_found",
                "Фонд расходования услуги не найден.");
        }

        var destinationOperations = destinationFund.Id == oldFund.Id
            ? oldOperations
            : (await repository.GetOperationsOrderedAsync(destinationFund.Id, trackChanges: true, cancellationToken)).ToList();
        var availableAmount = destinationFund.Id == oldFund.Id
            ? MoneyMath.RoundMoney(destinationFund.Balance + disbursement.Amount)
            : destinationFund.Balance;
        if (normalizedAmount > availableAmount)
        {
            return InsufficientBalance(availableAmount);
        }

        var oldValues = Snapshot(disbursement);
        disbursement.FundId = destinationFund.Id;
        disbursement.Fund = destinationFund;
        disbursement.Amount = normalizedAmount;
        disbursement.Reason = BuildReason(supplierName, sourceOperation.ExpenseType?.Name);
        disbursement.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (destinationFund.Id != oldFund.Id)
        {
            Recalculate(oldFund, oldOperations.Where(operation => operation.Id != disbursement.Id));
            destinationOperations.Add(disbursement);
        }

        Recalculate(destinationFund, destinationOperations);
        AddAudit("fund.expense_disbursement_updated", "update", disbursement, actorUserId, null, oldValues);
        return ExpenseFundDisbursementResult.Success();
    }

    public async Task<ExpenseFundDisbursementResult> CancelAsync(
        FinancialOperation sourceOperation,
        string reason,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var disbursement = await repository.FindIncomeAssignmentForUpdateAsync(sourceOperation.Id, cancellationToken);
        if (disbursement is null || disbursement.IsCanceled)
        {
            return ExpenseFundDisbursementResult.Success();
        }

        var operations = await repository.GetOperationsOrderedAsync(
            disbursement.FundId,
            trackChanges: true,
            cancellationToken);
        disbursement.IsCanceled = true;
        disbursement.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Recalculate(disbursement.Fund, operations);
        AddAudit("fund.expense_disbursement_canceled", "cancel", disbursement, actorUserId, reason);
        return ExpenseFundDisbursementResult.Success();
    }

    public async Task<ExpenseFundDisbursementResult> RestoreAsync(
        FinancialOperation sourceOperation,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var disbursement = await repository.FindIncomeAssignmentForUpdateAsync(sourceOperation.Id, cancellationToken);
        if (disbursement is null)
        {
            return await CreateAsync(
                sourceOperation,
                sourceOperation.Supplier?.Name ?? "поставщик",
                actorUserId,
                cancellationToken);
        }

        if (!disbursement.IsCanceled)
        {
            return ExpenseFundDisbursementResult.Success();
        }

        if (disbursement.Amount > disbursement.Fund.Balance)
        {
            return InsufficientBalance(disbursement.Fund.Balance);
        }

        disbursement.IsCanceled = false;
        disbursement.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var operations = await repository.GetOperationsOrderedAsync(
            disbursement.FundId,
            trackChanges: true,
            cancellationToken);
        Recalculate(disbursement.Fund, operations);
        AddAudit("fund.expense_disbursement_restored", "restore", disbursement, actorUserId, null);
        return ExpenseFundDisbursementResult.Success();
    }

    private static ExpenseFundDisbursementResult InsufficientBalance(decimal availableAmount) =>
        ExpenseFundDisbursementResult.Failure(
            "fund_balance_insufficient",
            $"Сумма выплаты превышает доступный остаток фонда {MoneyFormatting.Format(availableAmount)}.");

    private static string BuildReason(string supplierName, string? expenseTypeName) =>
        $"Выплата поставщику «{supplierName}» по услуге «{expenseTypeName ?? "Без названия"}».";

    private static void Recalculate(Fund fund, IEnumerable<FundOperation> source)
    {
        var balance = 0m;
        foreach (var operation in source.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id))
        {
            operation.BalanceBefore = balance;
            if (operation.IsCanceled ||
                (operation.SourceFinancialOperationId.HasValue && operation.OperationKind == FundOperationKinds.Deposit))
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
        FundOperation operation,
        Guid? actorUserId,
        string? reason,
        IReadOnlyDictionary<string, object?>? oldValues = null)
    {
        auditEventWriter.Add(new AuditEventWriteRequest(
            ActorUserId: actorUserId,
            Action: action,
            EntityType: "fund_operation",
            EntityId: operation.Id.ToString(),
            Summary: $"Списание выплаты из фонда {operation.Fund.Name}: {MoneyFormatting.Format(operation.Amount)}.",
            Section: "funds",
            ActionKind: actionKind,
            EntityDisplayName: operation.Fund.Name,
            Reason: reason,
            OldValues: oldValues,
            NewValues: Snapshot(operation),
            FieldLabels: new Dictionary<string, string>
            {
                ["fund"] = "Фонд",
                ["amount"] = "Сумма",
                ["isCanceled"] = "Статус"
            },
            Metadata: new Dictionary<string, object?>
            {
                ["fundId"] = operation.FundId,
                ["sourceFinancialOperationId"] = operation.SourceFinancialOperationId,
                ["automatic"] = true
            },
            RelatedDocumentId: operation.SourceFinancialOperationId?.ToString()));
    }

    private static IReadOnlyDictionary<string, object?> Snapshot(FundOperation operation) =>
        new Dictionary<string, object?>
        {
            ["fund"] = operation.Fund.Name,
            ["amount"] = operation.Amount,
            ["isCanceled"] = operation.IsCanceled
        };
}
