using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlIrregularAccrualIntegrationTests
{
    [PostgreSqlFact]
    public async Task RepeatedManualMigration_PreservesExistingAmountsAndRegularUniqueness()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync("20260908135423_PreserveGeneratedFinancialComments");
        await using var context = database.CreateContext();
        var garage = new AccountingTestDataBuilder().BuildGarage(number: "IRR-MIGRATION");
        var template = new IrregularPayment { Name = "Основание миграции", Amount = 100m };
        var destination = await context.IncomeTypes.SingleAsync(item => item.Code == "other_payments");
        var month = new DateOnly(2026, 8, 1);
        Accrual CreateAccrual(DateOnly period, string source, decimal amount) => new()
        {
            Garage = garage,
            IncomeType = destination,
            IrregularPayment = template,
            Basis = template.Name,
            AccountingMonth = period,
            DueDate = period.AddMonths(1).AddDays(-1),
            Amount = amount,
            Source = source,
            Comment = "Данные до изменения ограничения"
        };
        var manual = CreateAccrual(month, AccrualSources.Manual, 100m);
        var regular = CreateAccrual(month.AddMonths(1), AccrualSources.Regular, 200m);
        context.AddRange(manual, regular);
        await context.SaveChangesAsync();

        await context.Database.MigrateAsync();

        var preserved = await context.Accruals.AsNoTracking().Where(item => item.GarageId == garage.Id).ToListAsync();
        Assert.Equal(2, preserved.Count);
        Assert.Equal(100m, preserved.Single(item => item.Id == manual.Id).Amount);
        Assert.Equal(200m, preserved.Single(item => item.Id == regular.Id).Amount);
        Assert.All(preserved, item => Assert.Equal("Данные до изменения ограничения", item.Comment));
        context.AddRange(
            CreateAccrual(month, AccrualSources.Manual, 150m),
            CreateAccrual(month.AddMonths(1), AccrualSources.Manual, 50m));
        await context.SaveChangesAsync();
        Assert.Equal(250m, await context.Accruals.Where(item => item.GarageId == garage.Id && item.AccountingMonth == month).SumAsync(item => item.Amount));
        var downgradeError = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.MigrateAsync("20260908135423_PreserveGeneratedFinancialComments"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, downgradeError.SqlState);
        Assert.Equal(4, await context.Accruals.CountAsync(item => item.GarageId == garage.Id));

        var duplicateRegular = CreateAccrual(month.AddMonths(1), AccrualSources.Regular, 300m);
        context.Add(duplicateRegular);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        context.Entry(duplicateRegular).State = EntityState.Detached;
        regular.IsCanceled = true;
        await context.SaveChangesAsync();
        context.Add(duplicateRegular);
        await context.SaveChangesAsync();
        Assert.Single(await context.Accruals.Where(item => item.GarageId == garage.Id && item.Source == AccrualSources.Regular && !item.IsCanceled).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task CatalogAccruals_SumRepeatedMonthAndSupportEditRestoreAndFullPayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var garage = new AccountingTestDataBuilder().BuildGarage(number: "IRR-REPEATED-MONTH");
        var template = new IrregularPayment { Name = "Повторная выдача карты", Amount = 300m };
        var month = new DateOnly(2026, 8, 1);
        await using var context = database.CreateContext();
        context.AddRange(garage, template);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context);
        var request = new CreateIrregularAccrualRequest(garage.Id, template.Id, template.Name, template.Amount, month, "Выдана карта");

        var first = await service.CreateIrregularAccrualAsync(request, null, CancellationToken.None);
        var second = await service.CreateIrregularAccrualAsync(request with { Comment = "Выдана ещё одна карта" }, null, CancellationToken.None);
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.NotEqual(first.Value!.Id, second.Value!.Id);
        template.Name = "Выдача дополнительной карты";
        await context.SaveChangesAsync();
        var nextMonth = await service.CreateIrregularAccrualAsync(request with { AccountingMonth = month.AddMonths(1) }, null, CancellationToken.None);
        Assert.True(nextMonth.Succeeded, nextMonth.ErrorMessage);

        var updated = await service.UpdateAccrualAsync(nextMonth.Value!.Id,
            new CreateAccrualRequest(garage.Id, first.Value.IncomeTypeId, month, 400m, AccrualSources.Manual,
                "Уточнение суммы", template.Id, template.Name), null, CancellationToken.None);
        Assert.True(updated.Succeeded, updated.ErrorMessage);
        var canceled = await service.CancelAccrualAsync(second.Value.Id, new("Проверка отдельной отмены"), null, CancellationToken.None);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        var partialWorksheet = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(month, month), CancellationToken.None);
        Assert.True(partialWorksheet.Succeeded, partialWorksheet.ErrorMessage);
        Assert.Equal(700m, Assert.Single(partialWorksheet.Value!.Rows, item => item.IrregularPaymentId == template.Id).AccrualAmount);
        var restored = await service.RestoreAccrualAsync(second.Value.Id, null, CancellationToken.None);
        Assert.True(restored.Succeeded, restored.ErrorMessage);
        var worksheet = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(month, month), CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var row = Assert.Single(worksheet.Value!.Rows, item => item.IrregularPaymentId == template.Id);
        Assert.Equal(template.Name, row.IncomeTypeName);
        Assert.Equal(1000m, row.AccrualAmount);
        Assert.Equal(1000m, row.Debt);
        Assert.Equal("Повторная выдача карты", (await context.Accruals.AsNoTracking().SingleAsync(item => item.Id == first.Value.Id)).Basis);

        var partialPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(garage.Id, first.Value.IncomeTypeId, month.AddDays(18), month,
                250m, null, "Частичная оплата нескольких начислений", IrregularPaymentId: template.Id), null, CancellationToken.None);
        Assert.True(partialPayment.Succeeded, partialPayment.ErrorMessage);
        var afterPartialPayment = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(month, month), CancellationToken.None);
        Assert.True(afterPartialPayment.Succeeded, afterPartialPayment.ErrorMessage);
        var remaining = Assert.Single(afterPartialPayment.Value!.Rows, item => item.IrregularPaymentId == template.Id);
        Assert.Equal(250m, remaining.IncomeAmount);
        Assert.Equal(750m, remaining.Debt);

        var paid = await service.CreateFullGaragePaymentAsync(
            new CreateFullGaragePaymentRequest(garage.Id, month.AddDays(19),
                [new(first.Value.IncomeTypeId, month, 750m, "Три начисления одного основания", IrregularPaymentId: template.Id)], Guid.NewGuid()),
            null, CancellationToken.None);
        Assert.True(paid.Succeeded, paid.ErrorMessage);
        var payment = Assert.Single(paid.Value!.Operations);
        var allocations = await context.AccrualPaymentAllocations.AsNoTracking()
            .Where(item => item.FinancialOperationId == payment.Id).ToListAsync();
        Assert.Equal(3, allocations.Count);
        Assert.Equal(750m, allocations.Sum(item => item.Amount));
        Assert.Equal(1000m, await context.AccrualPaymentAllocations.Where(item => item.Accrual.GarageId == garage.Id && item.IsActive).SumAsync(item => item.Amount));
        Assert.Equal(3, await context.Accruals.CountAsync(item => item.GarageId == garage.Id && !item.IsCanceled));
        Assert.Equal(3, await context.AuditEvents.CountAsync(item => item.Action == "finance.irregular_accrual_created"));
        Assert.Single(await context.AuditEvents.Where(item => item.Action == "finance.accrual_updated").ToListAsync());
        Assert.Single(await context.AuditEvents.Where(item => item.Action == "finance.accrual_restored").ToListAsync());
    }

    [PostgreSqlFact]
    public async Task MigratedDatabase_RoutesIrregularAccrualByStableCodeAndAllowsRepeatedManualTemplate()
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
        Guid customAccrualId;
        Guid destinationIncomeTypeId;
        await using (var createContext = database.CreateContext())
        {
            var service = FinanceServiceTestFactory.Create(createContext);
            var result = await service.CreateIrregularAccrualAsync(
                new CreateIrregularAccrualRequest(garage.Id, payment.Id, payment.Name, payment.Amount, new DateOnly(2026, 9, 18), null),
                null,
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(875.13m, result.Value!.Amount);
            Assert.Equal("Переименованное назначение", result.Value.IncomeTypeName);
            Assert.Equal(payment.Id, result.Value.IrregularPaymentId);
            createdAccrualId = result.Value.Id;
            destinationIncomeTypeId = result.Value.IncomeTypeId;

            var customResult = await service.CreateIrregularAccrualAsync(
                new CreateIrregularAccrualRequest(garage.Id, null, "Замена пульта ворот", 915.255m, new DateOnly(2026, 9, 18), null),
                null,
                CancellationToken.None);
            Assert.True(customResult.Succeeded);
            Assert.Equal("Замена пульта ворот", customResult.Value!.Basis);
            Assert.Equal(915.26m, customResult.Value.Amount);
            customAccrualId = customResult.Value.Id;

            var worksheet = await service.GetGarageIncomeWorksheetAsync(
                garage.Id,
                new GarageIncomeWorksheetRequest(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1)),
                CancellationToken.None);
            Assert.True(worksheet.Succeeded);
            Assert.Contains(
                worksheet.Value!.Rows,
                row => row.IncomeTypeName == "Переименованное назначение" &&
                    row.Reason == customResult.Value.Basis && row.AccrualAmount == customResult.Value.Amount);

            var fullPaymentQuote = await service.GetGarageFullPaymentQuoteAsync(
                garage.Id,
                CancellationToken.None);
            Assert.True(fullPaymentQuote.Succeeded, fullPaymentQuote.ErrorMessage);
            Assert.Equal(1790.39m, fullPaymentQuote.Value!.TotalAmount);
            Assert.Equal(2, fullPaymentQuote.Value.Lines.Count);
            Assert.Contains(
                fullPaymentQuote.Value.Lines,
                line => line.IncomeTypeId == destinationIncomeTypeId &&
                    line.IrregularPaymentId == payment.Id &&
                    line.OutstandingAmount == 875.13m);
            Assert.Contains(
                fullPaymentQuote.Value.Lines,
                line => line.IncomeTypeId == destinationIncomeTypeId &&
                    !line.IrregularPaymentId.HasValue &&
                    line.OutstandingAmount == 915.26m);
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
            Assert.Equal("Карта доступа", stored.Basis);
            var customStored = await verificationContext.Accruals.AsNoTracking().SingleAsync(item => item.Id == customAccrualId);
            Assert.Equal("Замена пульта ворот", customStored.Basis);
            Assert.Null(customStored.IrregularPaymentId);

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
            await verificationContext.SaveChangesAsync();
            Assert.Equal(1750.26m, await verificationContext.Accruals
                .Where(item => item.GarageId == garage.Id && item.IrregularPaymentId == payment.Id)
                .SumAsync(item => item.Amount));
        }
    }
}
