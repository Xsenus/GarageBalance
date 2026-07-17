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
    DateTimeOffset CreatedAtUtc,
    bool IsCanceled,
    bool IsCashToBankTransfer);

public sealed record FundOperationPageDto(
    IReadOnlyList<FundOperationDto> Items,
    int TotalCount,
    int Offset,
    int Limit);

public sealed record CreateFundOperationRequest(
    [Required, MaxLength(20)] string OperationKind,
    [Range(0.01, 999999999)] decimal Amount,
    [Required, MaxLength(1000)] string Reason,
    bool IsCashToBankTransfer = false);

public sealed record UpdateFundOperationRequest(
    [Range(0.01, 999999999)] decimal Amount,
    [Required, MaxLength(1000)] string Reason);

public sealed record CancelFundOperationRequest(
    [Required, MaxLength(1000)] string Reason);
