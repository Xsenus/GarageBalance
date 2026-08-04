namespace GarageBalance.Api.Domain.Common;

public interface IOptimisticConcurrencyEntity
{
    Guid Version { get; set; }
}
