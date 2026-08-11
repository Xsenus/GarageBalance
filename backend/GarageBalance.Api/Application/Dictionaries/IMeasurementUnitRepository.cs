using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IMeasurementUnitRepository
{
    Task<IReadOnlyList<MeasurementUnit>> GetListAsync(string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<MeasurementUnitPageData> GetPageAsync(string? normalizedSearch, bool includeArchived, int offset, int limit, CancellationToken cancellationToken);
    Task<MeasurementUnit?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<MeasurementUnit?> FindArchivedAsync(Guid id, CancellationToken cancellationToken);
    Task<MeasurementUnit?> FindActiveByNameAsync(string name, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken);
    Task<bool> HasActiveServiceAssignmentsAsync(string name, CancellationToken cancellationToken);
    Task RenameServiceAssignmentsAsync(string previousName, string newName, CancellationToken cancellationToken);
    void Add(MeasurementUnit unit);
}

public sealed record MeasurementUnitPageData(IReadOnlyList<MeasurementUnit> Items, int TotalCount);
