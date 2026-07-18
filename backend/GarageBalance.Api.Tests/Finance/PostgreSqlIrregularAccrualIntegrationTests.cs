using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlIrregularAccrualIntegrationTests
{
    [PostgreSqlFact]
    public async Task MigratedDatabase_RoutesIrregularAccrualByStableCodeAndEnforcesTemplateDuplicate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var builder = new AccountingTestDataBuilder();
        var garage = builder.BuildGarage(number: "IRR-PG-1");
        var payment = new IrregularPayment { Name = "Карта доступа", Amount = 875.125m };

        await using (var setupContext = database.CreateContext())
        {
            var destination = await setupContext.IncomeTypes.SingleAsync(item => item.Code == "other_payments");
            destination.Name = "Переименованное назначение";
            setupContext.AddRange(garage, payment);
            await setupContext.SaveChangesAsync();
        }

        Guid createdAccrualId;
        Guid destinationIncomeTypeId;
        await using (var createContext = database.CreateContext())
        {
            var service = FinanceServiceTestFactory.Create(createContext);
            var result = await service.CreateIrregularAccrualAsync(
                new CreateIrregularAccrualRequest(garage.Id, payment.Id, new DateOnly(2026, 9, 18), null),
                null,
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(875.13m, result.Value!.Amount);
            Assert.Equal("Переименованное назначение", result.Value.IncomeTypeName);
            Assert.Equal(payment.Id, result.Value.IrregularPaymentId);
            createdAccrualId = result.Value.Id;
            destinationIncomeTypeId = result.Value.IncomeTypeId;
        }

        await using (var verificationContext = database.CreateContext())
        {
            var stored = await verificationContext.Accruals
                .AsNoTracking()
                .Include(item => item.IncomeType)
                .Include(item => item.IrregularPayment)
                .SingleAsync(item => item.Id == createdAccrualId);
            Assert.Equal("other_payments", stored.IncomeType.Code);
            Assert.NotNull(stored.IncomeType.DestinationFundId);
            Assert.Equal("Карта доступа", stored.IrregularPayment!.Name);

            verificationContext.Accruals.Add(new Accrual
            {
                GarageId = garage.Id,
                IncomeTypeId = destinationIncomeTypeId,
                IrregularPaymentId = payment.Id,
                AccountingMonth = new DateOnly(2026, 9, 1),
                DueDate = new DateOnly(2026, 10, 31),
                OverdueFromDate = new DateOnly(2026, 12, 1),
                Amount = 875.13m,
                Source = AccrualSources.Manual
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => verificationContext.SaveChangesAsync());
        }
    }
}
