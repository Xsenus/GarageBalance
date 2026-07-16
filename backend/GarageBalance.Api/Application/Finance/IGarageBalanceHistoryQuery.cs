namespace GarageBalance.Api.Application.Finance;

public interface IGarageBalanceHistoryQuery
{
    Task<GarageBalanceHistoryData> GetAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken);
}

public sealed record GarageBalanceHistoryData(
    decimal PreviousAccrualTotal,
    decimal PreviousIncomeTotal,
    IReadOnlyList<GarageBalanceHistoryBucketData> AccrualBuckets,
    IReadOnlyList<GarageBalanceHistoryBucketData> IncomeBuckets);

public sealed record GarageBalanceHistoryBucketData(
    DateOnly AccountingMonth,
    decimal Amount);
