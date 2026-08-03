using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Settings;

public sealed class CashBankBalanceSettingsService(
    ICashBankBalanceOperationRepository repository,
    IFinanceAvailableBalanceQuery availableBalanceQuery,
    IApplicationUnitOfWork unitOfWork,
    IAuditEventWriter auditEventWriter,
    IBusinessDateProvider businessDateProvider,
    TimeProvider timeProvider) : ICashBankBalanceSettingsService
{
    private static readonly string[] CashExpenseTypeCodes = CashExpenseClassification.TypeCodes;
    private static readonly string[] CashExpenseTypeNames = CashExpenseClassification.TypeNames;

    public async Task<CashBankBalanceSettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var totals = await repository.GetTotalsAsync(cancellationToken);
        var balance = await availableBalanceQuery.GetAsync(
            CashExpenseTypeCodes,
            CashExpenseTypeNames,
            cancellationToken);
        var recent = await repository.GetRecentAsync(50, cancellationToken);
        return CreateDto(totals, balance, recent);
    }

    public async Task<FinanceResult<CashBankBalanceSettingsDto>> UpdateOpeningBalancesAsync(
        UpdateCashBankOpeningBalancesRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var cashOpeningBalance = MoneyMath.RoundMoney(request.CashOpeningBalance);
        var bankOpeningBalance = MoneyMath.RoundMoney(request.BankOpeningBalance);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (cashOpeningBalance < 0m || bankOpeningBalance < 0m)
        {
            return FinanceResult<CashBankBalanceSettingsDto>.Failure(
                "opening_balance_negative",
                "Стартовый остаток не может быть отрицательным.");
        }

        if (!IsValidReason(reason))
        {
            return FinanceResult<CashBankBalanceSettingsDto>.Failure(
                "reason_invalid",
                "Укажите причину изменения от 3 до 1000 символов.");
        }

        await using var balanceLock = await availableBalanceQuery.AcquireUpdateLockAsync(
            FinanceBalanceAccounts.Cash | FinanceBalanceAccounts.Bank,
            cancellationToken);

        var totals = await repository.GetTotalsAsync(cancellationToken);
        var balance = await availableBalanceQuery.GetAsync(
            CashExpenseTypeCodes,
            CashExpenseTypeNames,
            cancellationToken);
        var cashDifference = MoneyMath.RoundMoney(cashOpeningBalance - totals.CashOpeningBalance);
        var bankDifference = MoneyMath.RoundMoney(bankOpeningBalance - totals.BankOpeningBalance);
        if (cashDifference == 0m && bankDifference == 0m)
        {
            return FinanceResult<CashBankBalanceSettingsDto>.Success(
                await GetAsync(cancellationToken));
        }

        if (RawCashBalance(balance) + cashDifference < 0m ||
            RawBankBalance(balance) + bankDifference < 0m)
        {
            return FinanceResult<CashBankBalanceSettingsDto>.Failure(
                "opening_balance_below_committed_amount",
                "Новый стартовый остаток не должен делать текущий остаток кассы или счёта отрицательным.");
        }

        var now = timeProvider.GetUtcNow();
        var operationDate = businessDateProvider.Today;
        AddDifference(
            CashBankAccounts.Cash,
            CashBankBalanceOperationKinds.OpeningBalance,
            cashDifference,
            operationDate,
            reason,
            actorUserId,
            now);
        AddDifference(
            CashBankAccounts.Bank,
            CashBankBalanceOperationKinds.OpeningBalance,
            bankDifference,
            operationDate,
            reason,
            actorUserId,
            now);

        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "cash_bank_opening_balances.updated",
            "cash_bank_balance_settings",
            "opening-balances",
            Summary: "Изменены стартовые остатки кассы и банковского счёта.",
            Section: "settings",
            ActionKind: "update",
            EntityDisplayName: "Стартовые остатки кассы и счёта",
            Reason: reason,
            OldValues: new Dictionary<string, object?>
            {
                ["cashOpeningBalance"] = totals.CashOpeningBalance,
                ["bankOpeningBalance"] = totals.BankOpeningBalance
            },
            NewValues: new Dictionary<string, object?>
            {
                ["cashOpeningBalance"] = cashOpeningBalance,
                ["bankOpeningBalance"] = bankOpeningBalance
            },
            FieldLabels: new Dictionary<string, string>
            {
                ["cashOpeningBalance"] = "Стартовый остаток кассы",
                ["bankOpeningBalance"] = "Стартовый остаток банковского счёта"
            }));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<CashBankBalanceSettingsDto>.Success(
            await GetAsync(cancellationToken));
    }

    public async Task<FinanceResult<CashBankBalanceSettingsDto>> CreateAdjustmentAsync(
        CreateCashBankBalanceAdjustmentRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var account = request.Account?.Trim().ToLowerInvariant() ?? string.Empty;
        var direction = request.Direction?.Trim().ToLowerInvariant() ?? string.Empty;
        var amount = MoneyMath.RoundMoney(request.Amount);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (account is not CashBankAccounts.Cash and not CashBankAccounts.Bank)
        {
            return Invalid("account_invalid", "Выберите кассу или банковский счёт.");
        }

        if (direction is not CashBankBalanceDirections.Increase and not CashBankBalanceDirections.Decrease)
        {
            return Invalid("direction_invalid", "Выберите пополнение или списание.");
        }

        if (request.OperationDate == default)
        {
            return Invalid("operation_date_required", "Укажите дату операции.");
        }

        if (amount <= 0m)
        {
            return Invalid("amount_invalid", "Сумма операции должна быть больше нуля.");
        }

        if (!IsValidReason(reason))
        {
            return Invalid("reason_invalid", "Укажите причину операции от 3 до 1000 символов.");
        }

        var cashAccount = account == CashBankAccounts.Cash;
        await using var balanceLock = await availableBalanceQuery.AcquireUpdateLockAsync(
            cashAccount ? FinanceBalanceAccounts.Cash : FinanceBalanceAccounts.Bank,
            cancellationToken);
        var balance = await availableBalanceQuery.GetAsync(
            CashExpenseTypeCodes,
            CashExpenseTypeNames,
            cancellationToken);
        var available = cashAccount ? RawCashBalance(balance) : RawBankBalance(balance);
        if (direction == CashBankBalanceDirections.Decrease && amount > available)
        {
            return Invalid(
                "insufficient_balance",
                cashAccount
                    ? "В кассе недостаточно средств для списания."
                    : "На банковском счёте недостаточно средств для списания.");
        }

        var operation = new CashBankBalanceOperation
        {
            Account = account,
            OperationKind = CashBankBalanceOperationKinds.Adjustment,
            Direction = direction,
            OperationDate = request.OperationDate,
            Amount = amount,
            Reason = reason,
            ActorUserId = actorUserId,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        repository.Add(operation);
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            direction == CashBankBalanceDirections.Increase
                ? "cash_bank_balance.increased"
                : "cash_bank_balance.decreased",
            "cash_bank_balance_operation",
            operation.Id.ToString(),
            Summary: $"{AccountName(account)}: {DirectionName(direction)} на {amount:N2} ₽.",
            Section: "settings",
            ActionKind: "create",
            EntityDisplayName: AccountName(account),
            Reason: reason,
            NewValues: new Dictionary<string, object?>
            {
                ["account"] = account,
                ["direction"] = direction,
                ["operationDate"] = request.OperationDate,
                ["amount"] = amount
            },
            FieldLabels: new Dictionary<string, string>
            {
                ["account"] = "Счёт",
                ["direction"] = "Операция",
                ["operationDate"] = "Дата",
                ["amount"] = "Сумма"
            }));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return FinanceResult<CashBankBalanceSettingsDto>.Success(
            await GetAsync(cancellationToken));
    }

    private void AddDifference(
        string account,
        string operationKind,
        decimal difference,
        DateOnly operationDate,
        string reason,
        Guid? actorUserId,
        DateTimeOffset now)
    {
        if (difference == 0m)
        {
            return;
        }

        repository.Add(new CashBankBalanceOperation
        {
            Account = account,
            OperationKind = operationKind,
            Direction = difference > 0m
                ? CashBankBalanceDirections.Increase
                : CashBankBalanceDirections.Decrease,
            OperationDate = operationDate,
            Amount = Math.Abs(difference),
            Reason = reason,
            ActorUserId = actorUserId,
            CreatedAtUtc = now
        });
    }

    private static CashBankBalanceSettingsDto CreateDto(
        CashBankBalanceOperationTotals totals,
        FinanceAvailableBalanceData balance,
        IReadOnlyList<CashBankBalanceOperation> recent) =>
        new(
            MoneyMath.RoundMoney(totals.CashOpeningBalance),
            MoneyMath.RoundMoney(totals.BankOpeningBalance),
            MoneyMath.RoundMoney(Math.Max(RawCashBalance(balance), 0m)),
            MoneyMath.RoundMoney(Math.Max(RawBankBalance(balance), 0m)),
            recent.Select(operation => new CashBankBalanceOperationDto(
                operation.Id,
                operation.Account,
                operation.OperationKind,
                operation.Direction,
                operation.OperationDate,
                operation.Amount,
                operation.Reason,
                operation.CreatedAtUtc)).ToList());

    private static decimal RawCashBalance(FinanceAvailableBalanceData balance) =>
        balance.CashAdjustmentTotal +
        balance.IncomeTotal -
        balance.BankDepositTotal -
        balance.CashExpenseTotal;

    private static decimal RawBankBalance(FinanceAvailableBalanceData balance) =>
        balance.BankAdjustmentTotal +
        balance.BankDepositTotal -
        balance.BankExpenseTotal;

    private static bool IsValidReason(string reason) =>
        reason.Length is >= 3 and <= 1000;

    private static string AccountName(string account) =>
        account == CashBankAccounts.Cash ? "Касса" : "Банковский счёт";

    private static string DirectionName(string direction) =>
        direction == CashBankBalanceDirections.Increase ? "пополнение" : "списание";

    private static FinanceResult<CashBankBalanceSettingsDto> Invalid(string code, string message) =>
        FinanceResult<CashBankBalanceSettingsDto>.Failure(code, message);
}
