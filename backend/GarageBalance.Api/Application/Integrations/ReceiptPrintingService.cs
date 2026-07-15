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

        var operation = await repository.FindOperationAsync(financialOperationId, cancellationToken);
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
        var documentNumber = string.IsNullOrWhiteSpace(operation.DocumentNumber)
            ? operation.Id.ToString("N")
            : operation.DocumentNumber.Trim();
        var garageNumber = operation.Garage?.Number;
        var ownerName = operation.Garage?.Owner?.FullName;
        var adapterResult = await receiptPrintingAdapter.ProcessAsync(
            new ReceiptPrintingAdapterRequest(
                action,
                operation.Id,
                documentNumber,
                operation.Amount,
                operation.OperationDate,
                operation.AccountingMonth,
                garageNumber,
                ownerName,
                operation.IncomeType?.Name,
                reason,
                isCopy,
                copyMark),
            cancellationToken);
        var auditEvent = auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            auditAction,
            "receipt_printing",
            operation.Id.ToString(),
            Summary: $"{actionLabel}: поступление {documentNumber} на сумму {MoneyFormatting.Format(operation.Amount)}.",
            Section: "integrations",
            ActionKind: actionKind,
            EntityDisplayName: isCopy ? $"Копия квитанции {documentNumber}" : $"Квитанция {documentNumber}",
            Reason: reason,
            Metadata: new Dictionary<string, object?>
            {
                ["receiptAction"] = action,
                ["operationKind"] = operation.OperationKind,
                ["amount"] = operation.Amount,
                ["incomeTypeName"] = operation.IncomeType?.Name,
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
            RelatedDocumentId: operation.Id.ToString(),
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
            auditEvent.CreatedAtUtc));
    }

    private static string? NormalizeAction(string action)
    {
        var normalized = action.Trim().ToLowerInvariant();
        return SupportedActions.Contains(normalized) ? normalized : null;
    }
}
