namespace GarageBalance.Api.Application.Settings;

public sealed record CashBankBalanceSettingsDto(
    decimal CashOpeningBalance,
    decimal BankOpeningBalance,
    decimal CashCurrentBalance,
    decimal BankCurrentBalance,
    IReadOnlyList<CashBankBalanceOperationDto> RecentOperations);

public sealed record CashBankBalanceOperationDto(
    Guid Id,
    string Account,
    string OperationKind,
    string Direction,
    DateOnly OperationDate,
    decimal Amount,
    string Reason,
    DateTimeOffset CreatedAtUtc);

public sealed record UpdateCashBankOpeningBalancesRequest(
    decimal CashOpeningBalance,
    decimal BankOpeningBalance,
    [ActionComment] string? Reason);

public sealed record CreateCashBankBalanceAdjustmentRequest(
    string Account,
    string Direction,
    DateOnly OperationDate,
    decimal Amount,
    [ActionComment] string? Reason);
