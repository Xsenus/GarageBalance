using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Reports;

namespace GarageBalance.Api.Application.Reports;

public interface IGarageReportQuickListRepository
{
    Task<IReadOnlyList<GarageReportQuickList>> GetAllAsync(CancellationToken cancellationToken);
    Task<GarageReportQuickList?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(string normalizedName, Guid? exceptId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Garage>> GetActiveGaragesAsync(IReadOnlySet<Guid> garageIds, CancellationToken cancellationToken);
    void Add(GarageReportQuickList quickList);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
