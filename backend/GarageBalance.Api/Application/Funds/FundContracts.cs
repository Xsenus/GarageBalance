using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Application.Funds;

public sealed record FundDto(
    Guid Id,
    string Name,
    decimal Balance,
    decimal AvailableToDistribute,
    int SortOrder,
    bool AllowOperations,
    bool IsSystem,
    IReadOnlyList<FundLinkedServiceDto> LinkedServices,
    Guid Version = default);

public sealed record FundOptionDto(
    Guid Id,
    string Name,
    bool AllowOperations);

public sealed record FundLinkedServiceDto(
    Guid Id,
    string Name);

public sealed record UpsertFundRequest(
    [Required, MaxLength(200)] string Name,
    Guid? Version = null);

public sealed record DeleteFundRequest(
    [ActionComment, MaxLength(1000)] string? Reason);

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
    bool IsAutomaticIncomeAssignment);

public sealed record FundOperationPageDto(
    IReadOnlyList<FundOperationDto> Items,
    int TotalCount,
    int Offset,
    int Limit);

public sealed record CreateFundOperationRequest(
    [Required, MaxLength(20)] string OperationKind,
    [Range(0.01, 999999999)] decimal Amount,
    [MaxLength(1000)] string? Reason);

public sealed record UpdateFundOperationRequest(
    [Range(0.01, 999999999)] decimal Amount,
    [ActionComment, MaxLength(1000)] string Reason);

public sealed record CancelFundOperationRequest(
    [ActionComment, MaxLength(1000)] string? Reason);
