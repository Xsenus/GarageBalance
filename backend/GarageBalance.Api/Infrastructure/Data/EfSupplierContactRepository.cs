using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfSupplierContactRepository(GarageBalanceDbContext dbContext) : ISupplierContactRepository
{
    public async Task<IReadOnlyList<SupplierContact>> GetListAsync(
        Guid? supplierId,
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SupplierContacts.AsNoTracking()
            .Include(contact => contact.Supplier)
            .Where(contact => includeArchived || !contact.IsArchived);
        if (supplierId is not null)
        {
            query = query.Where(contact => contact.SupplierId == supplierId);
        }

        if (normalizedSearch is not null)
        {
            query = query.Where(contact =>
                contact.FullName.ToLower().Contains(normalizedSearch) ||
                (contact.Position != null && contact.Position.ToLower().Contains(normalizedSearch)) ||
                (contact.Phone != null && contact.Phone.ToLower().Contains(normalizedSearch)) ||
                (contact.Email != null && contact.Email.ToLower().Contains(normalizedSearch)));
        }

        return await query.OrderBy(contact => contact.Supplier.Name)
            .ThenBy(contact => contact.FullName)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<SupplierContact?> FindActiveWithSupplierAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.SupplierContacts.Include(contact => contact.Supplier)
            .SingleOrDefaultAsync(contact => contact.Id == id && !contact.IsArchived, cancellationToken);
    }

    public Task<SupplierContact?> FindArchivedWithSupplierGroupAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.SupplierContacts.Include(contact => contact.Supplier)
            .ThenInclude(supplier => supplier.Group)
            .SingleOrDefaultAsync(contact => contact.Id == id && contact.IsArchived, cancellationToken);
    }

    public void Add(SupplierContact contact)
    {
        dbContext.SupplierContacts.Add(contact);
    }
}
