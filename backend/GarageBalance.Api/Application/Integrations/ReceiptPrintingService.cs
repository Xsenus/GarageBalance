using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Integrations;

public sealed class ReceiptPrintingService(
    GarageBalanceDbContext dbContext,
    IAuditEventWriter auditEventWriter) : IReceiptPrintingService
{
    private static readonly HashSet<string> SupportedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "print",
        "cancel",
        "reprint"
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
        if ((action is "cancel" or "reprint") && reason is null)
        {
            return ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure("receipt_print_reason_required", "Для отмены или повторной печати нужна причина.");
        }

        var operation = await dbContext.FinancialOperations
            .Include(item => item.Garage)
                .ThenInclude(garage => garage!.Owner)
            .Include(item => item.IncomeType)
            .Include(item => item.Supplier)
            .Include(item => item.ExpenseType)
            .AsTracking()
            .SingleOrDefaultAsync(item => item.Id == financialOperationId, cancellationToken);
        if (operation is null)
        {
            return ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure("financial_operation_not_found", "Финансовая операция не найдена.");
        }

        if (operation.OperationKind != FinancialOperationKinds.Income)
        {
            return ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure("receipt_print_income_required", "Квитанцию можно формировать только по поступлению владельца.");
        }

        if (operation.IsCanceled && action is "print" or "reprint")
        {
            return ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure("receipt_print_operation_canceled", "Нельзя печатать квитанцию по отмененному поступлению.");
        }

        var auditAction = action switch
        {
            "cancel" => "receipt.print_canceled",
            "reprint" => "receipt.reprint_requested",
            _ => "receipt.print_requested"
        };
        var actionKind = action == "cancel" ? "cancel" : "generate";
        var actionLabel = action switch
        {
            "cancel" => "Отмена печати",
            "reprint" => "Повторная печать",
            _ => "Печать квитанции"
        };
        var documentNumber = string.IsNullOrWhiteSpace(operation.DocumentNumber)
            ? operation.Id.ToString("N")
            : operation.DocumentNumber.Trim();
        var garageNumber = operation.Garage?.Number;
        var ownerName = operation.Garage?.Owner?.FullName;
        var auditEvent = auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            auditAction,
            "receipt_printing",
            operation.Id.ToString(),
            Summary: $"{actionLabel}: поступление {documentNumber} на сумму {operation.Amount:0.00}.",
            Section: "integrations",
            ActionKind: actionKind,
            EntityDisplayName: $"Квитанция {documentNumber}",
            Reason: reason,
            Metadata: new Dictionary<string, object?>
            {
                ["receiptAction"] = action,
                ["operationKind"] = operation.OperationKind,
                ["amount"] = operation.Amount,
                ["incomeTypeName"] = operation.IncomeType?.Name,
                ["adapterStatus"] = "pending_adapter"
            },
            RelatedGarageId: operation.GarageId?.ToString(),
            RelatedGarageNumber: garageNumber,
            RelatedAccountingMonth: operation.AccountingMonth.ToString("yyyy-MM"),
            RelatedCounterpartyId: operation.Garage?.OwnerId?.ToString(),
            RelatedCounterpartyName: ownerName,
            RelatedDocumentId: operation.Id.ToString(),
            RelatedDocumentNumber: documentNumber));
        await dbContext.SaveChangesAsync(cancellationToken);

        return ReceiptPrintingResult<ReceiptPrintingActionDto>.Success(new ReceiptPrintingActionDto(
            auditEvent!.Id,
            operation.Id,
            action,
            "registered",
            $"{actionLabel} зарегистрирована в истории. Фактическая печать будет доступна после подключения адаптера.",
            documentNumber,
            auditEvent.CreatedAtUtc));
    }

    private static string? NormalizeAction(string action)
    {
        var normalized = action.Trim().ToLowerInvariant();
        return SupportedActions.Contains(normalized) ? normalized : null;
    }
}
