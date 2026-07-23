using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlChargeServiceExpenseTypeMigrationIntegrationTests
{
    [PostgreSqlFact]
    public async Task Migration_LinksStandardChargeServicesToMatchingExpenseTypesAndProtectsReferences()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var linkedServices = await context.ChargeServiceSettings
            .AsNoTracking()
            .Include(service => service.IncomeType)
            .Include(service => service.ExpenseType)
            .Where(service =>
                service.IncomeType != null &&
                (service.IncomeType.Code == "water" ||
                 service.IncomeType.Code == "trash" ||
                 service.IncomeType.Code == "electricity"))
            .ToListAsync();

        Assert.NotEmpty(linkedServices);
        Assert.All(linkedServices, service =>
        {
            Assert.NotNull(service.ExpenseType);
            var expectedExpenseCode = service.IncomeType!.Code switch
            {
                "water" => "water_supply",
                "trash" => "trash_removal",
                _ => service.IncomeType.Code
            };
            Assert.Equal(expectedExpenseCode, service.ExpenseType!.Code);
        });

        var linkedCount = linkedServices.Count;
        var audit = await context.AuditEvents
            .AsNoTracking()
            .SingleAsync(item => item.Action == "dictionary.charge_service_expense_types_linked");
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(linkedCount, metadata.RootElement.GetProperty("linkedServiceCount").GetInt32());

        var referencedExpenseType = await context.ExpenseTypes
            .SingleAsync(item => item.Id == linkedServices[0].ExpenseTypeId);
        context.ExpenseTypes.Remove(referencedExpenseType);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
