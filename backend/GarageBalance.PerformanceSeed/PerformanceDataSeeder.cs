using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.PerformanceSeed;

public sealed class PerformanceDataSeeder(GarageBalanceDbContext context)
{
    public const string MarkerCode = "performance_seed_v1";
    private static readonly DateOnly FirstMonth = new(2021, 1, 1);
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<PerformanceSeedResult> SeedAsync(
        PerformanceSeedOptions options,
        CancellationToken cancellationToken)
    {
        var existingMarker = await context.IncomeTypes
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Code == MarkerCode, cancellationToken);
        if (existingMarker is not null)
        {
            var existingGarageCount = await context.Garages
                .CountAsync(item => item.Number.StartsWith("PERF-"), cancellationToken);
            var existingAccrualCount = await context.Accruals
                .CountAsync(item => item.IncomeTypeId == existingMarker.Id, cancellationToken);
            var existingPaymentCount = await context.FinancialOperations
                .CountAsync(item => item.IncomeTypeId == existingMarker.Id, cancellationToken);
            var existingReadingCount = await context.MeterReadings
                .CountAsync(item => item.Comment == MarkerCode, cancellationToken);
            return new PerformanceSeedResult(
                existingGarageCount,
                existingAccrualCount,
                existingPaymentCount,
                existingReadingCount,
                TimeSpan.Zero,
                AlreadyPresent: true);
        }

        var stopwatch = Stopwatch.StartNew();
        var incomeType = new IncomeType
        {
            Id = DeterministicGuid("income-type"),
            Name = "Нагрузочный взнос",
            Code = MarkerCode,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        };
        context.IncomeTypes.Add(incomeType);

        var garages = new Garage[options.GarageCount];
        for (var index = 1; index <= options.GarageCount; index++)
        {
            var owner = new Owner
            {
                Id = DeterministicGuid($"owner-{index}"),
                LastName = $"Тестовый{index:0000}",
                FirstName = "Владелец",
                MiddleName = "Нагрузочный",
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = CreatedAtUtc
            };
            var garage = new Garage
            {
                Id = DeterministicGuid($"garage-{index}"),
                Number = $"PERF-{index:0000}",
                PeopleCount = 1 + index % 5,
                FloorCount = 1 + index % 3,
                StartingBalance = index % 7 == 0 ? 250m : 0m,
                InitialElectricityMeterValue = index * 1000m,
                Owner = owner,
                Comment = MarkerCode,
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = CreatedAtUtc
            };
            garages[index - 1] = garage;
        }

        context.Garages.AddRange(garages);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        const int garageBatchSize = 25;
        for (var offset = 0; offset < garages.Length; offset += garageBatchSize)
        {
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            var batchEnd = Math.Min(offset + garageBatchSize, garages.Length);
            for (var garageOffset = offset; garageOffset < batchEnd; garageOffset++)
            {
                var garage = garages[garageOffset];
                var garageIndex = garageOffset + 1;
                for (var monthOffset = 0; monthOffset < options.MonthCount; monthOffset++)
                {
                    var month = FirstMonth.AddMonths(monthOffset);
                    var previousValue = garageIndex * 1000m + monthOffset * 125m;
                    context.Accruals.Add(new Accrual
                    {
                        Id = DeterministicGuid($"accrual-{garage.Id}-{month:yyyyMM}"),
                        GarageId = garage.Id,
                        IncomeTypeId = incomeType.Id,
                        AccountingMonth = month,
                        DueDate = month.AddMonths(1).AddDays(-1),
                        OverdueFromDate = month.AddMonths(1),
                        Amount = 100m + garageIndex % 11,
                        Source = MarkerCode,
                        CreatedAtUtc = CreatedAtUtc,
                        UpdatedAtUtc = CreatedAtUtc
                    });
                    context.FinancialOperations.Add(new FinancialOperation
                    {
                        Id = DeterministicGuid($"payment-{garage.Id}-{month:yyyyMM}"),
                        OperationKind = FinancialOperationKinds.Income,
                        OperationDate = month.AddDays(14),
                        AccountingMonth = month,
                        Amount = 80m + garageIndex % 9,
                        GarageId = garage.Id,
                        IncomeTypeId = incomeType.Id,
                        DocumentNumber = $"PERF-{garageIndex:0000}-{month:yyyyMM}",
                        Comment = MarkerCode,
                        CreatedAtUtc = CreatedAtUtc,
                        UpdatedAtUtc = CreatedAtUtc
                    });
                    context.MeterReadings.Add(new MeterReading
                    {
                        Id = DeterministicGuid($"meter-{garage.Id}-{month:yyyyMM}"),
                        GarageId = garage.Id,
                        MeterKind = MeterKinds.Electricity,
                        AccountingMonth = month,
                        ReadingDate = month.AddDays(24),
                        PreviousValue = previousValue,
                        CurrentValue = previousValue + 125m,
                        Consumption = 125m,
                        Comment = MarkerCode,
                        Version = DeterministicGuid($"meter-version-{garage.Id}-{month:yyyyMM}"),
                        CreatedAtUtc = CreatedAtUtc,
                        UpdatedAtUtc = CreatedAtUtc
                    });
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
            context.ChangeTracker.AutoDetectChangesEnabled = true;
        }

        await context.Database.ExecuteSqlRawAsync(
            "ANALYZE owners; ANALYZE garages; ANALYZE income_types; ANALYZE accruals; ANALYZE financial_operations; ANALYZE meter_readings;",
            cancellationToken);
        stopwatch.Stop();

        var expectedRows = options.GarageCount * options.MonthCount;
        return new PerformanceSeedResult(
            options.GarageCount,
            expectedRows,
            expectedRows,
            expectedRows,
            stopwatch.Elapsed,
            AlreadyPresent: false);
    }

    private static Guid DeterministicGuid(string value)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"garagebalance-performance:{value}"));
        return new Guid(hash);
    }
}

public sealed record PerformanceSeedResult(
    int GarageCount,
    int AccrualCount,
    int PaymentCount,
    int MeterReadingCount,
    TimeSpan Elapsed,
    bool AlreadyPresent);
