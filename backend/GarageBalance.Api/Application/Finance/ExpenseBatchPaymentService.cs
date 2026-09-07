using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public sealed record ExpenseBatchPaymentRequest(
    Guid RequestId,
    DateOnly AccountingMonth,
    DateOnly OperationDate,
    [Required, RegularExpression("^[a-f0-9]{64}$")] string Fingerprint,
    bool ConfirmNegativeFundBalance,
    // The required-comment policy is checked after idempotency lookup so a completed
    // request can still be retrieved after administrators change that policy.
    [MaxLength(1000)] string? Comment);
public sealed record ExpenseBatchPaymentResult(Guid RequestId, IReadOnlyList<Guid> OperationIds);

public interface IExpenseBatchPaymentService
{
    Task<FinanceResult<ExpenseBatchPaymentResult>> PayAsync(ExpenseBatchPaymentRequest request, Guid actorUserId, CancellationToken cancellationToken);
}

public sealed class ExpenseBatchPaymentService(
    IExpenseBatchPreviewService previewService,
    IFinanceService financeService,
    IExpensePaymentBatchRepository batchRepository,
    IExpenseBatchTransactionRunner transactionRunner,
    IExpenseFundDisbursementService fundService,
    IFinanceAvailableBalanceQuery balanceQuery,
    IStaffSalaryAdjustmentRepository salaryRepository,
    IApplicationUnitOfWork unitOfWork,
    IAuditEventWriter auditWriter,
    TimeProvider timeProvider) : IExpenseBatchPaymentService
{
    public async Task<FinanceResult<ExpenseBatchPaymentResult>> PayAsync(
        ExpenseBatchPaymentRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var comment = request.Comment?.Trim();
        if (request.RequestId == Guid.Empty || actorUserId == Guid.Empty
            || request.Fingerprint is null || request.Fingerprint.Length != 64
            || request.Fingerprint.Any(character => character is not (>= 'a' and <= 'f') and not (>= '0' and <= '9'))
            || comment?.Length is > 0 and < 3 or > 1000)
        {
            return Failure("expense_batch_request_invalid", "Проверьте параметры массовой выплаты и комментарий.");
        }
        comment = string.IsNullOrEmpty(comment) ? null : comment;
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.AccountingMonth,
            request.OperationDate,
            request.Fingerprint,
            request.ConfirmNegativeFundBalance,
            Comment = comment
        })));

        // The same connection keeps this existing global lock until the completed
        // request is recorded. Acquire it before the Serializable snapshot begins.
        await using var fundLock = await fundService.AcquireUpdateLockAsync(cancellationToken);
        var previous = await batchRepository.FindAsync(request.RequestId, actorUserId, cancellationToken);
        if (previous is not null)
        {
            return previous.RequestHash == requestHash
                ? FinanceResult<ExpenseBatchPaymentResult>.Success(new(previous.Id, previous.Operations.Select(item => item.OperationId).Order().ToArray()))
                : Failure("expense_batch_request_conflict", "Этот ключ запроса уже использован с другими параметрами.");
        }
        if (ActionCommentRequirementContext.IsRequired && comment is null)
        {
            return Failure("expense_batch_comment_required", "Укажите комментарий к массовой выплате.");
        }
        var previewRequest = new ExpenseBatchPreviewRequest(request.AccountingMonth, request.OperationDate);
        var initial = await previewService.PreviewAsync(previewRequest, cancellationToken);
        var validation = ValidatePreview(initial, request);
        if (validation is not null) return validation;

        var salaryLocks = new List<IAsyncDisposable>();
        try
        {
            foreach (var key in initial.Value!.Items.Where(item => item.Payment.RecipientKind == "staff")
                .Select(item => (item.Payment.RecipientId, item.Payment.AccountingMonth)).Distinct()
                .OrderBy(key => key.RecipientId).ThenBy(key => key.AccountingMonth))
            {
                salaryLocks.Add(await salaryRepository.AcquireMonthlyLockAsync(key.RecipientId, key.AccountingMonth, cancellationToken));
            }
            await using var balanceLock = await balanceQuery.AcquireUpdateLockAsync(
                FinanceBalanceAccounts.Bank | FinanceBalanceAccounts.Cash, cancellationToken);
            try
            {
                return await transactionRunner.ExecuteAsync(async token =>
                {
                    var fresh = await previewService.PreviewAsync(previewRequest, token);
                    var changed = ValidatePreview(fresh, request);
                    if (changed is not null) return changed;
                    var operationIds = new List<Guid>();
                    foreach (var item in fresh.Value!.Items)
                    {
                        var payment = item.Payment;
                        var result = payment.RecipientKind == "staff"
                            ? await financeService.CreateStaffPaymentAsync(new(payment.RecipientId, request.OperationDate,
                                payment.AccountingMonth, payment.Amount, null, comment), actorUserId, token)
                            : await financeService.CreateExpenseAsync(new(payment.RecipientId, payment.ExpenseTypeId,
                                request.OperationDate, payment.AccountingMonth, payment.Amount, null, comment,
                                ExpensePaymentSource: "bank", ExpenseFundId: payment.FundId,
                                ConfirmNegativeFundBalance: request.ConfirmNegativeFundBalance), actorUserId, token);
                        if (!result.Succeeded) return Failure(result.ErrorCode!, result.ErrorMessage!);
                        operationIds.Add(result.Value!.Id);
                    }
                    var batch = new ExpensePaymentBatch
                    {
                        Id = request.RequestId,
                        ActorUserId = actorUserId,
                        RequestHash = requestHash,
                        CreatedAtUtc = timeProvider.GetUtcNow(),
                        Operations = operationIds.Select(id => new ExpensePaymentBatchOperation { OperationId = id }).ToArray()
                    };
                    batchRepository.Add(batch);
                    auditWriter.Add(new(actorUserId, "finance.expense_batch_created", "expense_payment_batch", batch.Id.ToString(),
                        Summary: $"Проведена массовая выплата: {operationIds.Count} операций.", Section: "finance", ActionKind: "create",
                        Reason: comment, Metadata: new Dictionary<string, object?>
                        {
                            ["operationCount"] = operationIds.Count,
                            ["operationIds"] = string.Join(", ", operationIds.Order()),
                            ["bankAmount"] = fresh.Value.BankAmount,
                            ["cashAmount"] = fresh.Value.CashAmount
                        }));
                    await unitOfWork.SaveChangesAsync(token);
                    return FinanceResult<ExpenseBatchPaymentResult>.Success(new(batch.Id, operationIds.Order().ToArray()));
                }, cancellationToken);
            }
            catch (ApplicationPersistenceConflictException)
            {
                return Failure("expense_batch_request_conflict", "Ключ массовой выплаты уже использован. Обновите результат запроса.");
            }
        }
        finally
        {
            foreach (var lease in salaryLocks.AsEnumerable().Reverse()) await lease.DisposeAsync();
        }
    }

    private static FinanceResult<ExpenseBatchPaymentResult>? ValidatePreview(
        FinanceResult<ExpenseBatchPreviewDto> result, ExpenseBatchPaymentRequest request)
    {
        if (!result.Succeeded) return Failure(result.ErrorCode!, result.ErrorMessage!);
        var preview = result.Value!;
        if (preview.Fingerprint != request.Fingerprint)
            return Failure("expense_batch_preview_changed", "Суммы или состав выплат изменились. Проверьте новый предварительный расчёт.");
        if (!preview.CanSubmit)
            return Failure("expense_batch_not_payable", preview.Issues.FirstOrDefault() ?? "Неоплаченных сумм нет.");
        if (preview.RequiresNegativeFundConfirmation && !request.ConfirmNegativeFundBalance)
            return Failure("expense_batch_negative_fund_confirmation_required", "Подтвердите отрицательный остаток фонда перед выплатой.");
        if (preview.Items.Any(item => item.Payment.Amount is <= 0 or > 999999999m))
            return Failure("expense_batch_amount_invalid", "Сумма одной выплаты выходит за допустимые пределы.");
        return null;
    }

    private static FinanceResult<ExpenseBatchPaymentResult> Failure(string code, string message) =>
        FinanceResult<ExpenseBatchPaymentResult>.Failure(code, message);
}
