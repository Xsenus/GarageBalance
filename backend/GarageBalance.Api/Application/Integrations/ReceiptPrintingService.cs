using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Integrations;

public sealed class ReceiptPrintingService(
    IReceiptPrintingRepository repository,
    IAuditEventWriter auditEventWriter,
    IReceiptPrintingAdapter receiptPrintingAdapter) : IReceiptPrintingService
{
    private static readonly HashSet<string> SupportedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ReceiptPrintingActions.Print,
        ReceiptPrintingActions.Cancel,
        ReceiptPrintingActions.Reprint
    };

    public async Task<ReceiptPrintingResult<ReceiptPrintingActionDto>> RegisterActionAsync(
        Guid financialOperationId,
        ReceiptPrintingActionRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var action = NormalizeAction(request.Action);
        if (action is null)
        {
            return ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure("receipt_print_action_invalid", "Укажите действие печати: print, cancel или reprint.");
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if ((action is ReceiptPrintingActions.Cancel or ReceiptPrintingActions.Reprint) && reason is null)
        {
            return ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure("receipt_print_reason_required", "Для отмены или повторной печати нужна причина.");
        }

        var receiptOperations = await repository.FindReceiptOperationsAsync(financialOperationId, cancellationToken);
        var operation = receiptOperations.SingleOrDefault(item => item.Id == financialOperationId);
        if (operation is null)
        {
            return ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure("financial_operation_not_found", "Финансовая операция не найдена.");
        }

        if (operation.OperationKind != FinancialOperationKinds.Income)
        {
            return ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure("receipt_print_income_required", "Квитанцию можно формировать только по поступлению владельца.");
        }

        if (operation.IsCanceled && action is ReceiptPrintingActions.Print or ReceiptPrintingActions.Reprint)
        {
            return ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure("receipt_print_operation_canceled", "Нельзя печатать квитанцию по отмененному поступлению.");
        }

        if (receiptOperations.Any(item =>
                item.OperationKind != FinancialOperationKinds.Income ||
                item.GarageId != operation.GarageId ||
                item.OperationDate != operation.OperationDate))
        {
            return ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure(
                "receipt_print_batch_invalid",
                "Пакет квитанции содержит операции другого гаража или неподдерживаемого типа.");
        }

        if (receiptOperations.Count > ReceiptPrintingLimits.MaximumLineCount)
        {
            return ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure(
                "receipt_print_batch_too_large",
                $"Единая квитанция не может содержать больше {ReceiptPrintingLimits.MaximumLineCount} позиций.");
        }

        var activeOperations = receiptOperations
            .Where(item => !item.IsCanceled)
            .ToList();
        if (activeOperations.Count == 0)
        {
            return ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure(
                "receipt_print_operation_canceled",
                "Нельзя печатать квитанцию: все поступления пакета отменены.");
        }

        var allocations = await repository.GetActiveAllocationsAsync(
            activeOperations.Select(item => item.Id).ToArray(),
            cancellationToken);
        var allocationsByOperationId = allocations
            .GroupBy(item => item.FinancialOperationId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var receiptLines = activeOperations
            .Select(item => new ReceiptPrintingLineItem(
                item.Id,
                item.IncomeType?.Name ?? "Поступление",
                item.AccountingMonth,
                item.Amount,
                allocationsByOperationId.GetValueOrDefault(item.Id, [])
                    .Select(allocation => new ReceiptPrintingAllocationItem(
                        allocation.AccrualId,
                        allocation.AccountingMonth,
                        allocation.IncomeTypeName,
                        allocation.Amount))
                    .ToArray()))
            .ToArray();
        var receiptAmount = receiptLines.Sum(item => item.Amount);
        var receiptBatchId = operation.ReceiptBatchId;
        var auditAction = action switch
        {
            ReceiptPrintingActions.Cancel => "receipt.print_canceled",
            ReceiptPrintingActions.Reprint => "receipt.reprint_requested",
            _ => "receipt.print_requested"
        };
        var actionKind = action == ReceiptPrintingActions.Cancel ? "cancel" : "generate";
        var isCopy = action == ReceiptPrintingActions.Reprint;
        var copyMark = isCopy ? "КОПИЯ" : null;
        var actionLabel = action switch
        {
            ReceiptPrintingActions.Cancel => "Отмена печати",
            ReceiptPrintingActions.Reprint => "Повторная печать копии",
            _ => "Печать квитанции"
        };
        var documentNumber = receiptBatchId is not null
            ? $"ПАКЕТ-{receiptBatchId.Value:N}"
            : string.IsNullOrWhiteSpace(operation.DocumentNumber)
                ? operation.Id.ToString("N")
                : operation.DocumentNumber.Trim();
        var garageNumber = operation.Garage?.Number;
        var ownerName = operation.Garage?.Owner?.FullName;
        var adapterResult = await receiptPrintingAdapter.ProcessAsync(
            new ReceiptPrintingAdapterRequest(
                action,
                operation.Id,
                documentNumber,
                receiptAmount,
                operation.OperationDate,
                operation.AccountingMonth,
                garageNumber,
                ownerName,
                receiptLines.Length == 1 ? receiptLines[0].IncomeTypeName : "Несколько услуг",
                reason,
                isCopy,
                copyMark,
                receiptBatchId,
                receiptLines),
            cancellationToken);
        var auditEvent = auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            auditAction,
            "receipt_printing",
            receiptBatchId?.ToString() ?? operation.Id.ToString(),
            Summary: receiptLines.Length > 1
                ? $"{actionLabel}: единая квитанция {documentNumber}, {FormatReceiptLineCount(receiptLines.Length)} на сумму {MoneyFormatting.Format(receiptAmount)}."
                : $"{actionLabel}: поступление {documentNumber} на сумму {MoneyFormatting.Format(receiptAmount)}.",
            Section: "integrations",
            ActionKind: actionKind,
            EntityDisplayName: isCopy ? $"Копия квитанции {documentNumber}" : $"Квитанция {documentNumber}",
            Reason: reason,
            Metadata: new Dictionary<string, object?>
            {
                ["receiptAction"] = action,
                ["operationKind"] = operation.OperationKind,
                ["amount"] = receiptAmount,
                ["incomeTypeName"] = receiptLines.Length == 1 ? receiptLines[0].IncomeTypeName : "Несколько услуг",
                ["receiptBatchId"] = receiptBatchId,
                ["lineCount"] = receiptLines.Length,
                ["operationIds"] = receiptLines.Select(item => item.FinancialOperationId).ToArray(),
                ["allocationCount"] = receiptLines.Sum(item => item.Allocations.Count),
                ["isCopy"] = isCopy,
                ["copyMark"] = copyMark,
                ["adapterStatus"] = adapterResult.Status,
                ["adapterMessage"] = adapterResult.StatusMessage,
                ["deviceResponseCode"] = adapterResult.DeviceResponseCode,
                ["externalReceiptId"] = adapterResult.ExternalReceiptId
            },
            RelatedGarageId: operation.GarageId?.ToString(),
            RelatedGarageNumber: garageNumber,
            RelatedAccountingMonth: operation.AccountingMonth.ToString("yyyy-MM"),
            RelatedCounterpartyId: operation.Garage?.OwnerId?.ToString(),
            RelatedCounterpartyName: ownerName,
            RelatedDocumentId: receiptBatchId?.ToString() ?? operation.Id.ToString(),
            RelatedDocumentNumber: documentNumber));
        await repository.SaveChangesAsync(cancellationToken);

        return ReceiptPrintingResult<ReceiptPrintingActionDto>.Success(new ReceiptPrintingActionDto(
            auditEvent!.Id,
            operation.Id,
            action,
            adapterResult.Status,
            adapterResult.StatusMessage,
            documentNumber,
            isCopy,
            copyMark,
            auditEvent.CreatedAtUtc,
            receiptBatchId,
            receiptAmount,
            receiptLines.Length));
    }

    private static string? NormalizeAction(string action)
    {
        var normalized = action.Trim().ToLowerInvariant();
        return SupportedActions.Contains(normalized) ? normalized : null;
    }

    internal static string FormatReceiptLineCount(int count)
    {
        var absolute = Math.Abs(count);
        var lastTwoDigits = absolute % 100;
        var lastDigit = absolute % 10;
        var noun = lastTwoDigits is >= 11 and <= 14
            ? "позиций"
            : lastDigit switch
            {
                1 => "позиция",
                2 or 3 or 4 => "позиции",
                _ => "позиций"
            };
        return $"{count} {noun}";
    }
}
