using System.Security.Cryptography;
using System.Text.Json;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Application.Finance;

public sealed record ExpenseBatchPreviewRequest(DateOnly AccountingMonth, DateOnly OperationDate);
public sealed record ExpenseBatchPreviewItem(ExpenseBatchLine Payment, string RecipientName, string ExpenseTypeName);
public sealed record ExpenseBatchFundPreview(Guid FundId, string Name, decimal Amount, decimal AvailableAmount);
public sealed record ExpenseBatchPreviewDto(
    DateOnly AccountingMonth,
    DateOnly OperationDate,
    IReadOnlyList<ExpenseBatchPreviewItem> Items,
    decimal BankAmount,
    decimal CashAmount,
    decimal AvailableBankAmount,
    decimal AvailableCashAmount,
    IReadOnlyList<ExpenseBatchFundPreview> Funds,
    IReadOnlyList<string> Issues,
    bool RequiresNegativeFundConfirmation,
    string Fingerprint)
{
    public bool CanSubmit => Items.Count > 0 && Issues.Count == 0;
}

public interface IExpenseBatchPreviewService
{
    Task<FinanceResult<ExpenseBatchPreviewDto>> PreviewAsync(ExpenseBatchPreviewRequest request, CancellationToken cancellationToken);
}

public sealed class ExpenseBatchPreviewService(
    IFinanceService financeService,
    IExpenseBatchStaffDebtQuery staffDebtQuery,
    IApplicationSettingsService settingsService,
    IBusinessDateProvider businessDateProvider) : IExpenseBatchPreviewService
{
    public async Task<FinanceResult<ExpenseBatchPreviewDto>> PreviewAsync(ExpenseBatchPreviewRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var businessDate = businessDateProvider.Today;
        var currentMonth = MonthPeriod.Normalize(businessDate);
        if (request.AccountingMonth.Day != 1 || request.OperationDate == DateOnly.MinValue
            || request.AccountingMonth < currentMonth)
        {
            return FinanceResult<ExpenseBatchPreviewDto>.Failure("expense_batch_period_invalid",
                "Выберите текущий или будущий расчётный месяц и укажите дату выплаты.");
        }
        var worksheetResult = await financeService.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(request.AccountingMonth), cancellationToken);
        if (!worksheetResult.Succeeded)
        {
            return FinanceResult<ExpenseBatchPreviewDto>.Failure(worksheetResult.ErrorCode!, worksheetResult.ErrorMessage!);
        }
        var worksheet = worksheetResult.Value!;
        var payableRows = worksheet.Rows.Where(row => row.RowKind is "supplier" or "staff").ToArray();
        if (payableRows.Any(row => (row.ExpenseTypeId ?? Guid.Empty) == Guid.Empty
            || ((row.RowKind == "supplier" ? row.SupplierId : row.StaffMemberId) ?? Guid.Empty) == Guid.Empty))
        {
            return FinanceResult<ExpenseBatchPreviewDto>.Failure("expense_batch_data_changed", "Состав задолженности изменился. Обновите форму выплат.");
        }
        var debts = payableRows.Where(row => row.RowKind == "supplier")
            .Select(row => new ExpenseBatchDebt("supplier", row.SupplierId ?? Guid.Empty,
                row.ExpenseTypeId ?? Guid.Empty, row.ExpenseFundId, request.AccountingMonth,
                row.ClosingDebt - row.ClosingAdvance)).ToList();
        var staffIds = payableRows.Where(row => row.RowKind == "staff")
            .Select(row => row.StaffMemberId ?? Guid.Empty).Distinct().ToArray();
        if (staffIds.Length > 0)
        {
            var salarySettings = await settingsService.GetSalaryAccrualSettingsAsync(cancellationToken);
            var lastAccrualMonth = businessDate.Day < salarySettings.AccrualDay
                ? currentMonth.AddMonths(-1) : currentMonth;
            try
            {
                debts.AddRange(await staffDebtQuery.GetAsync(staffIds, request.AccountingMonth, lastAccrualMonth, cancellationToken));
            }
            catch (ExpenseBatchPreparationException exception)
            {
                return FinanceResult<ExpenseBatchPreviewDto>.Failure(exception.ErrorCode, exception.Message);
            }
        }
        ExpenseBatchPlan plan;
        try
        {
            var staffClosingDebts = payableRows.Where(row => row.RowKind == "staff")
                .ToDictionary(row => row.StaffMemberId!.Value, row => Math.Max(0, row.ClosingDebt - row.ClosingAdvance));
            plan = ExpenseBatchPlanner.Build(request.AccountingMonth, debts, staffClosingDebts);
        }
        catch (ArgumentException)
        {
            return FinanceResult<ExpenseBatchPreviewDto>.Failure("expense_batch_data_changed",
                "Состав задолженности изменился. Обновите форму выплат.");
        }
        var rowsByRecipient = new Dictionary<(string, Guid, Guid), ExpenseWorksheetRowDto>();
        foreach (var row in payableRows)
        {
            if (!rowsByRecipient.TryAdd((row.RowKind, (row.RowKind == "supplier" ? row.SupplierId : row.StaffMemberId) ?? Guid.Empty,
                row.ExpenseTypeId ?? Guid.Empty), row))
            {
                return FinanceResult<ExpenseBatchPreviewDto>.Failure("expense_batch_data_changed", "Состав задолженности изменился. Обновите форму выплат.");
            }
        }
        var items = new List<ExpenseBatchPreviewItem>();
        foreach (var line in plan.Lines)
        {
            if (!rowsByRecipient.TryGetValue((line.RecipientKind, line.RecipientId, line.ExpenseTypeId), out var row))
            {
                return FinanceResult<ExpenseBatchPreviewDto>.Failure("expense_batch_data_changed", "Состав задолженности изменился. Обновите форму выплат.");
            }
            items.Add(new ExpenseBatchPreviewItem(line, row.CounterpartyName ?? "Получатель", row.ExpenseTypeName));
        }
        var rowsByFund = payableRows.Where(row => row.ExpenseFundId.HasValue).ToLookup(row => row.ExpenseFundId!.Value);
        var funds = plan.FundAmounts.OrderBy(pair => pair.Key).Select(pair =>
        {
            var rows = rowsByFund[pair.Key].ToArray();
            return new ExpenseBatchFundPreview(pair.Key, rows[0].ExpenseFundName ?? "Фонд", pair.Value,
                rows.Min(row => row.Difference ?? 0m));
        }).ToArray();
        var issues = new List<string>();
        if (plan.Lines.Any(item => item.RecipientKind == "supplier" && !item.FundId.HasValue))
        {
            issues.Add("Для одного из поставщиков не выбран действующий фонд расходования. Проверьте карточку поставщика.");
        }
        if (plan.BankAmount > 0 && plan.BankAmount > worksheet.BankAmount)
        {
            issues.Add("На банковском счёте недостаточно средств для всех выплат поставщикам.");
        }
        if (plan.CashAmount > 0 && plan.CashAmount > worksheet.CashAmount)
        {
            issues.Add("В кассе недостаточно средств для всех выплат сотрудникам.");
        }
        var requiresConfirmation = funds.Any(fund => fund.Amount > fund.AvailableAmount);
        var preview = new ExpenseBatchPreviewDto(request.AccountingMonth, request.OperationDate, items,
            plan.BankAmount, plan.CashAmount, worksheet.BankAmount, worksheet.CashAmount, funds,
            issues, requiresConfirmation, "");
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(preview)));
        return FinanceResult<ExpenseBatchPreviewDto>.Success(preview with { Fingerprint = fingerprint });
    }
}
