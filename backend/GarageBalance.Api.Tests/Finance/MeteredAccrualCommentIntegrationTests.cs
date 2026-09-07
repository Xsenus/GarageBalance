using System.Text.Json;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeteredAccrualCommentIntegrationTests
{
    [Fact]
    public async Task SqliteReadingCycleRefreshesCommentAndAuditWithoutLosingNotes()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlReadingCycleRefreshesCommentAndAuditWithoutLosingNotes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyAsync(context);
    }

    private static async Task VerifyAsync(GarageBalanceDbContext context)
    {
        var month = new DateOnly(2026, 6, 1);
        var service = FinanceServiceTestFactory.Create(context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));
        foreach (var kind in new[] { MeterKinds.Water, MeterKinds.Electricity })
        {
            var garage = new Garage { Number = $"COMMENT-{kind}", InitialWaterMeterValue = 11m, InitialElectricityMeterValue = 11m, CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
            var income = new IncomeType { Name = $"Comment {kind}", Code = $"comment_{kind}" };
            var tariff = new Tariff { Name = $"Исторический тариф {kind}", CalculationBase = kind == MeterKinds.Water ? TariffCalculationBases.MeterWater : TariffCalculationBases.MeterElectricity, Rate = 7.47m, EffectiveFrom = new DateOnly(2026, 1, 1) };
            context.Garages.Add(garage);
            context.ChargeServiceSettings.Add(new ChargeServiceSetting
            {
                Name = $"Comment service {kind}",
                IncomeType = income,
                Tariff = tariff,
                IsRegular = true,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                IsMetered = true,
                MeterKind = kind,
                UnitName = "ед."
            });
            await context.SaveChangesAsync();
            var created = await service.CreateMeterReadingAsync(new CreateMeterReadingRequest(garage.Id, kind, month, month.AddDays(19), 20m, null), null, CancellationToken.None);
            Assert.True(created.Succeeded, created.ErrorMessage);
            var accrual = await context.Accruals.SingleAsync(item => item.GarageId == garage.Id && item.IncomeTypeId == income.Id);
            Assert.Equal(67.23m, accrual.Amount);
            var prefix = $"Начисление по показанию {kind}: расход ";
            var originalComment = accrual.Comment!;
            var suffix = originalComment[originalComment.IndexOf(';')..] + "; заметка сотрудника";
            if (kind == MeterKinds.Water)
            {
                suffix += new string('я', 1000 - prefix.Length - 1 - suffix.Length);
            }
            var oldConsumption = kind == MeterKinds.Water ? "9" : "20";
            accrual.Comment = prefix + oldConsumption + suffix;
            await context.SaveChangesAsync();
            var originalEvents = await context.AuditEvents.AsNoTracking().ToDictionaryAsync(item => item.Id, item => new { item.Summary, item.MetadataJson });

            var updated = await service.UpdateMeterReadingAsync(created.Value!.Id,
                new CreateMeterReadingRequest(garage.Id, kind, month, month.AddDays(20), 21m, null, created.Value.Version), null, CancellationToken.None);
            Assert.True(updated.Succeeded, updated.ErrorMessage);
            Assert.Equal(74.70m, accrual.Amount);
            Assert.Equal(prefix + "10" + suffix, accrual.Comment);
            var updateAudit = await context.AuditEvents.SingleAsync(item => item.EntityId == accrual.Id.ToString() && item.Action == "finance.accrual_updated_from_meter_reading");
            using (var metadata = JsonDocument.Parse(updateAudit.MetadataJson!))
            {
                Assert.Contains("расход 10", metadata.RootElement.GetProperty("newValue").GetString());
                Assert.Contains("расход " + oldConsumption, metadata.RootElement.GetProperty("oldValue").GetString());
            }
            var restoredValue = await service.UpdateMeterReadingAsync(created.Value.Id,
                new CreateMeterReadingRequest(garage.Id, kind, month, month.AddDays(20), 20m, null, updated.Value!.Version), null, CancellationToken.None);
            Assert.True(restoredValue.Succeeded, restoredValue.ErrorMessage);
            Assert.Equal(prefix + "9" + suffix, accrual.Comment);
            Assert.Equal(67.23m, accrual.Amount);
            var canceled = await service.CancelMeterReadingAsync(created.Value.Id, new CancelFinanceEntryRequest("Контроль отмены"), null, CancellationToken.None);
            Assert.True(canceled.Succeeded, canceled.ErrorMessage);
            Assert.Equal(0m, accrual.Amount);
            Assert.Equal($"Начисление по показанию {kind}: показание отменено" + suffix, accrual.Comment);
            var restored = await service.RestoreMeterReadingAsync(created.Value.Id, null, CancellationToken.None);
            Assert.True(restored.Succeeded, restored.ErrorMessage);
            Assert.Equal(67.23m, accrual.Amount);
            Assert.Equal(prefix + "9" + suffix, accrual.Comment);
            foreach (var original in originalEvents)
            {
                var stored = await context.AuditEvents.AsNoTracking().SingleAsync(item => item.Id == original.Key);
                Assert.Equal(original.Value.Summary, stored.Summary);
                Assert.Equal(original.Value.MetadataJson, stored.MetadataJson);
            }
            Assert.Equal(accrual.Comment, (await context.Accruals.AsNoTracking().SingleAsync(item => item.Id == accrual.Id)).Comment);
            var paid = await service.CreateIncomeAsync(new CreateIncomeOperationRequest(garage.Id, income.Id, month.AddDays(20), month, 1m, $"COMMENT-PAID-{kind}", null), null, CancellationToken.None);
            Assert.True(paid.Succeeded, paid.ErrorMessage);
            var auditCount = await context.AuditEvents.CountAsync();
            var rejected = await service.UpdateMeterReadingAsync(created.Value.Id,
                new CreateMeterReadingRequest(garage.Id, kind, month, month.AddDays(20), 21m, null, restored.Value!.Version), null, CancellationToken.None);
            Assert.Equal("meter_reading_accrual_paid", rejected.ErrorCode);
            Assert.Equal("meter_reading_accrual_paid", (await service.CancelMeterReadingAsync(created.Value.Id, new CancelFinanceEntryRequest("Отмена оплаченного"), null, CancellationToken.None)).ErrorCode);
            Assert.Equal(prefix + "9" + suffix, accrual.Comment);
            Assert.Equal(67.23m, accrual.Amount);
            Assert.Equal(auditCount, await context.AuditEvents.CountAsync());

            var zeroGarage = new Garage { Number = $"ZERO-COMMENT-{kind}", InitialWaterMeterValue = 0m, InitialElectricityMeterValue = 0m, CreatedAtUtc = garage.CreatedAtUtc };
            context.Garages.Add(zeroGarage);
            await context.SaveChangesAsync();
            var zeroReading = await service.CreateMeterReadingAsync(new CreateMeterReadingRequest(zeroGarage.Id, kind, month, month.AddDays(19), 0m, null), null, CancellationToken.None);
            Assert.True(zeroReading.Succeeded, zeroReading.ErrorMessage);
            var zeroAccrual = new Accrual { Garage = zeroGarage, IncomeType = income, Tariff = tariff, AccountingMonth = month, DueDate = month.AddDays(25), Amount = 0m, Source = AccrualSources.Regular, RequiresMeterReading = true, CalculationMeterKind = kind, Comment = prefix + "0" + suffix };
            context.Accruals.Add(zeroAccrual);
            await context.SaveChangesAsync();
            Assert.True((await service.CancelMeterReadingAsync(zeroReading.Value!.Id, new CancelFinanceEntryRequest("Отмена нулевого"), null, CancellationToken.None)).Succeeded);
            Assert.Equal(0m, zeroAccrual.Amount);
            Assert.Equal($"Начисление по показанию {kind}: показание отменено" + suffix, zeroAccrual.Comment);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
