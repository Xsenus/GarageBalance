using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Funds;

public sealed record FundDto(
    Guid Id,
    string Name,
    decimal Balance,
    decimal AvailableToDistribute,
    int SortOrder,
    bool AllowOperations,
    bool IsSystem);

public sealed record FundOperationDto(
    Guid Id,
    Guid FundId,
    string FundName,
    string OperationKind,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string Reason,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateFundOperationRequest(
    [Required, MaxLength(20)] string OperationKind,
    [Range(0.01, 999999999)] decimal Amount,
    [Required, MaxLength(1000)] string Reason);
