using System.Data.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterConnectionScopeValidationTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    [Theory]
    [InlineData(MeterKinds.Water)]
    [InlineData(MeterKinds.Electricity)]
    [InlineData("custom")]
    public async Task NewReadingsAndReplacementsRespectScopeButHistoricalCorrectionsRemainAvailable(string kind)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyScopeWorkflowAsync(database.Context, ResolveKind(kind));
    }

    [PostgreSqlFact]
    public async Task PostgreSqlScopeWorkflowProtectsEveryMeterKind()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        foreach (var kind in new[] { MeterKinds.Water, MeterKinds.Electricity, ResolveKind("custom") })
            await VerifyScopeWorkflowAsync(context, kind);
    }

    private static async Task VerifyScopeWorkflowAsync(GarageBalanceDbContext context, string kind)
    {
        await context.ChargeServiceSettings.Where(item => item.MeterKind == kind)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsArchived, true));
        var garage = CreateGarage();
        var setting = new ChargeServiceSetting
        {
            Name = $"Scope {kind}",
            MeterKind = kind,
            IsRegular = true,
            IsMetered = true,
            AppliesToSelectedGarages = true,
            GarageIds = [Guid.NewGuid()]
        };
        context.AddRange(garage, CreateDevice(garage, kind), setting);
        await context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(context, businessDate: September.AddMonths(1).AddDays(29));
        var create = new CreateMeterReadingRequest(garage.Id, kind, September, September.AddDays(14), 10m, null);
        var replace = new ReplaceMeterDeviceRequest(garage.Id, kind, September, September.AddDays(19),
            "new-scope-device", 0, 1, 12, "Замена");
        Assert.Equal("meter_start_service_not_applicable", (await finance.CreateMeterReadingAsync(create, null, CancellationToken.None)).ErrorCode);
        Assert.Equal("meter_start_service_not_applicable", (await finance.ReplaceMeterDeviceAsync(replace, null, CancellationToken.None)).ErrorCode);
        Assert.False(await context.MeterReadings.AnyAsync(item => item.GarageId == garage.Id));
        Assert.Equal(1, await context.MeterDevices.CountAsync(item => item.GarageId == garage.Id));

        setting.GarageIds = [garage.Id];
        await context.SaveChangesAsync();
        var reading = await finance.CreateMeterReadingAsync(create, null, CancellationToken.None);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        setting.GarageIds = [];
        await context.SaveChangesAsync();
        var correction = await finance.CorrectHistoricalMeterReadingAsync(reading.Value!.Id,
            new(create.ReadingDate, 12m, null, "Исправление исторического показания", reading.Value.Version), null, CancellationToken.None);
        Assert.True(correction.Succeeded, correction.ErrorMessage);
        Assert.Equal(12m, correction.Value!.Consumption);
        Assert.Equal("meter_start_service_not_applicable", (await finance.CreateMeterReadingAsync(
            create with { AccountingMonth = September.AddMonths(1), ReadingDate = September.AddMonths(1).AddDays(19), CurrentValue = 20m },
            null, CancellationToken.None)).ErrorCode);
        Assert.Equal("meter_start_service_not_applicable", (await finance.ReplaceMeterDeviceAsync(
            replace with { AccountingMonth = September.AddMonths(1), ReplacementDate = September.AddMonths(1).AddDays(19) },
            null, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData(MeterKinds.Water)]
    [InlineData(MeterKinds.Electricity)]
    [InlineData("custom")]
    public async Task UnconfiguredKindsAllowOnlyLegacyReadingsAndReplacements(string kind)
    {
        kind = ResolveKind(kind);
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = CreateGarage();
        context.AddRange(garage, CreateDevice(garage, kind));
        await context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(context, businessDate: September.AddMonths(1).AddDays(29));
        var created = await finance.CreateMeterReadingAsync(new(garage.Id, kind, September, September.AddDays(14), 10m, null),
            null, CancellationToken.None);
        var replaced = await finance.ReplaceMeterDeviceAsync(new(garage.Id, kind, September.AddMonths(1),
            September.AddMonths(1).AddDays(19), "new-legacy-device", 0, 1, 12, "Замена"), null, CancellationToken.None);
        if (kind is not (MeterKinds.Water or MeterKinds.Electricity))
        {
            Assert.Equal("meter_start_service_not_applicable", created.ErrorCode);
            Assert.Equal("meter_start_service_not_applicable", replaced.ErrorCode);
            Assert.False(await context.MeterReadings.AnyAsync());
            return;
        }
        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.True(replaced.Succeeded, replaced.ErrorMessage);
        Assert.Equal(3m, replaced.Value!.Reading.Consumption);
    }

    [Fact]
    public async Task RepositoryDistinguishesNoConfigurationFromExplicitExclusion()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyRepositoryAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlRepositoryUsesOneBoundedScalarQueryAndSupportsArrayScope()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var capture = new SelectCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>().UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture).Options;
        await using var context = new GarageBalanceDbContext(options);
        await VerifyRepositoryAsync(context);
        Assert.NotEmpty(capture.Commands);
        Assert.All(capture.Commands, command =>
        {
            Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("MeterKind", command, StringComparison.Ordinal);
            Assert.DoesNotContain("JOIN", command, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static async Task VerifyRepositoryAsync(GarageBalanceDbContext context)
    {
        var repository = new EfChargeServiceSettingRepository(context);
        var garageId = Guid.NewGuid();
        foreach (var kind in new[] { MeterKinds.Water, MeterKinds.Electricity, ResolveKind("custom") })
        {
            await context.ChargeServiceSettings.Where(item => item.MeterKind == kind)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsArchived, true));
            var legacy = kind is MeterKinds.Water or MeterKinds.Electricity;
            Assert.Equal(legacy, await repository.IsRegularMeterKindApplicableAsync(garageId, kind, CancellationToken.None));
            context.ChargeServiceSettings.AddRange(
                new ChargeServiceSetting { Name = $"Archived {kind}", MeterKind = kind, IsRegular = true, IsMetered = true, IsArchived = true },
                new ChargeServiceSetting { Name = $"Irregular {kind}", MeterKind = kind, IsRegular = false, IsMetered = true },
                new ChargeServiceSetting { Name = $"Nonmetered {kind}", MeterKind = kind, IsRegular = true, IsMetered = false });
            await context.SaveChangesAsync();
            Assert.Equal(legacy, await repository.IsRegularMeterKindApplicableAsync(garageId, kind, CancellationToken.None));
            var excluded = new ChargeServiceSetting
            {
                Name = $"Excluded {kind}",
                MeterKind = kind,
                IsRegular = true,
                IsMetered = true,
                AppliesToSelectedGarages = true,
                GarageIds = [Guid.NewGuid()]
            };
            context.Add(excluded);
            await context.SaveChangesAsync();
            Assert.False(await repository.IsRegularMeterKindApplicableAsync(garageId, kind, CancellationToken.None));
            var included = new ChargeServiceSetting
            {
                Name = $"Included {kind}",
                MeterKind = kind,
                IsRegular = true,
                IsMetered = true,
                AppliesToSelectedGarages = true,
                GarageIds = [garageId]
            };
            context.Add(included);
            await context.SaveChangesAsync();
            Assert.True(await repository.IsRegularMeterKindApplicableAsync(garageId, kind, CancellationToken.None));
            included.GarageIds = [];
            included.AppliesToSelectedGarages = false;
            await context.SaveChangesAsync();
            Assert.True(await repository.IsRegularMeterKindApplicableAsync(garageId, kind, CancellationToken.None));
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.IsRegularMeterKindApplicableAsync(garageId, MeterKinds.Water, cancellation.Token));
    }

    private static string ResolveKind(string kind) => kind == "custom" ? MeterKinds.ForService(Guid.NewGuid()) : kind;
    private static Garage CreateGarage() => new()
    {
        Number = $"SCOPE-{Guid.NewGuid():N}",
        RegisteredOn = new DateOnly(2026, 1, 1),
        InitialWaterMeterValue = 0,
        InitialElectricityMeterValue = 0
    };
    private static MeterDevice CreateDevice(Garage garage, string kind) => new()
    {
        Garage = garage,
        MeterKind = kind,
        SerialNumber = "scope-device",
        InstalledOn = new DateOnly(2026, 1, 1),
        InitialValue = 0
    };
    private sealed class SelectCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
