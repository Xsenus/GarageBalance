using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IGarageTariffAssignmentRepository
{
    Task<GarageTariffAssignmentPage> GetPageAsync(Guid serviceId, Guid? garageId, bool includeArchived, int offset, int limit, CancellationToken cancellationToken);
    Task<Guid?> FindGarageIdAsync(Guid serviceId, Guid assignmentId, CancellationToken cancellationToken);
    Task<GarageTariffAssignment?> FindForUpdateAsync(Guid serviceId, Guid assignmentId, CancellationToken cancellationToken);
    Task<bool> HasOverlapAsync(Guid serviceId, IReadOnlyCollection<Guid> garageIds, DateOnly from, DateOnly? to, Guid? ignoredId, CancellationToken cancellationToken);
    Task<IGarageTariffWriteScope> BeginWriteAsync(IReadOnlyCollection<Guid> garageIds, CancellationToken cancellationToken);
    void Add(GarageTariffAssignment assignment);
}

public interface IGarageTariffWriteScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public sealed record GarageTariffAssignmentPage(IReadOnlyList<GarageTariffAssignment> Items, int TotalCount);
