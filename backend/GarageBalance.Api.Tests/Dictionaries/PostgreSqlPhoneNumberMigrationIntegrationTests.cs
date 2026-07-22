using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlPhoneNumberMigrationIntegrationTests
{
    private const string MigrationId = "20260722192313_NormalizePhoneNumbers";

    [PostgreSqlFact]
    public async Task Migration_NormalizesRecognizedPhonesAndPreservesUnknownLegacyValue()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var ownerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var unknownOwnerId = Guid.NewGuid();

        await using (var setupContext = database.CreateContext())
        {
            var group = new SupplierGroup { Name = "Связь" };
            setupContext.Owners.AddRange(
                new Owner { Id = ownerId, LastName = "Иванов", FirstName = "Иван", Phone = "8 913 123 45 67" },
                new Owner { Id = unknownOwnerId, LastName = "Петров", FirstName = "Петр", Phone = "внутренний 123" });
            setupContext.SupplierGroups.Add(group);
            setupContext.Suppliers.Add(new Supplier
            {
                Id = supplierId,
                Name = "Оператор",
                Group = group,
                Phone = "79137654321"
            });
            setupContext.SupplierContacts.Add(new SupplierContact
            {
                Id = contactId,
                SupplierId = supplierId,
                FullName = "Сидоров Сергей",
                Phone = "9130001122",
                Status = "Работает"
            });
            await setupContext.SaveChangesAsync();
            await setupContext.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = {MigrationId};
                """);
            await setupContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        Assert.Equal("+7 (913) 123-45-67", (await verificationContext.Owners.FindAsync(ownerId))!.Phone);
        Assert.Equal("внутренний 123", (await verificationContext.Owners.FindAsync(unknownOwnerId))!.Phone);
        Assert.Equal("+7 (913) 765-43-21", (await verificationContext.Suppliers.FindAsync(supplierId))!.Phone);
        Assert.Equal("+7 (913) 000-11-22", (await verificationContext.SupplierContacts.FindAsync(contactId))!.Phone);
    }
}
