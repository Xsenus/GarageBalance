using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageBalance.Api.Tests.Settings;

public sealed class ApplicationSettingsServiceTests
{
    [Fact]
    public async Task GetPaymentDisplaySettings_ReturnsFalseWhenSettingIsMissing()
    {
        var service = CreateService(new FakeRepository(), new CaptureAuditWriter());

        var result = await service.GetPaymentDisplaySettingsAsync(CancellationToken.None);

        Assert.False(result.ShowAllGarageOperationsByDefault);
    }

    [Fact]
    public async Task UpdatePaymentDisplaySettings_PersistsValueAndWritesAuditEvent()
    {
        var actorUserId = Guid.NewGuid();
        var repository = new FakeRepository();
        var auditWriter = new CaptureAuditWriter();
        var service = CreateService(repository, auditWriter);

        var result = await service.UpdatePaymentDisplaySettingsAsync(
            new UpdatePaymentDisplaySettingsRequest(true),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.ShowAllGarageOperationsByDefault);
        Assert.NotNull(repository.Setting);
        Assert.True(repository.Setting.BooleanValue);
        Assert.Equal(ApplicationSettingsService.ShowAllGarageOperationsKey, repository.Setting.Key);
        Assert.Equal(actorUserId, repository.Setting.UpdatedByUserId);
        Assert.Equal(1, repository.SaveChangesCount);
        var audit = Assert.Single(auditWriter.Requests);
        Assert.Equal("application_setting.updated", audit.Action);
        Assert.Equal("settings", audit.Section);
        Assert.Equal(actorUserId, audit.ActorUserId);
    }

    [Fact]
    public async Task UpdatePaymentDisplaySettings_DoesNotWriteAgainWhenValueIsUnchanged()
    {
        var repository = new FakeRepository
        {
            Setting = new ApplicationSetting
            {
                Key = ApplicationSettingsService.ShowAllGarageOperationsKey,
                BooleanValue = true
            }
        };
        var auditWriter = new CaptureAuditWriter();
        var service = CreateService(repository, auditWriter);

        var result = await service.UpdatePaymentDisplaySettingsAsync(
            new UpdatePaymentDisplaySettingsRequest(true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.ShowAllGarageOperationsByDefault);
        Assert.Equal(0, repository.SaveChangesCount);
        Assert.Empty(auditWriter.Requests);
    }

    [Fact]
    public async Task UpdateBusinessDate_PersistsOverrideAndRunsAutomationForSelectedMonth()
    {
        var actorUserId = Guid.NewGuid();
        var repository = new FakeRepository();
        var auditWriter = new CaptureAuditWriter();
        var businessDateProvider = new TestBusinessDateProvider(new DateOnly(2026, 7, 21));
        var automation = new FakeAutomationRunner();
        var service = CreateService(repository, auditWriter, businessDateProvider, automation);

        var result = await service.UpdateBusinessDateSettingsAsync(
            new UpdateBusinessDateRequest(new DateOnly(2026, 9, 15)),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.IsOverrideActive);
        Assert.Equal(new DateOnly(2026, 9, 15), result.EffectiveDate);
        Assert.Equal(new DateOnly(2026, 9, 15), repository.Setting!.DateValue);
        Assert.Equal(new DateOnly(2026, 9, 15), automation.ReceivedDate);
        Assert.Equal(actorUserId, automation.ReceivedActorUserId);
        Assert.True(result.Automation!.Succeeded);
        Assert.Equal(2, result.Automation.CreatedCount);
        Assert.Equal("application_setting.business_date_updated", Assert.Single(auditWriter.Requests).Action);
    }

    [Fact]
    public async Task UpdateBusinessDate_WithNullRestoresSystemDateAndRunsAutomation()
    {
        var repository = new FakeRepository
        {
            Setting = new ApplicationSetting
            {
                Key = ApplicationSettingsService.BusinessDateOverrideKey,
                DateValue = new DateOnly(2026, 9, 15)
            }
        };
        var businessDateProvider = new TestBusinessDateProvider(new DateOnly(2026, 7, 21));
        businessDateProvider.SetOverride(repository.Setting.DateValue);
        var automation = new FakeAutomationRunner();
        var service = CreateService(repository, new CaptureAuditWriter(), businessDateProvider, automation);

        var result = await service.UpdateBusinessDateSettingsAsync(
            new UpdateBusinessDateRequest(null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.IsOverrideActive);
        Assert.Equal(new DateOnly(2026, 7, 21), result.EffectiveDate);
        Assert.Null(repository.Setting.DateValue);
        Assert.Equal(new DateOnly(2026, 7, 21), automation.ReceivedDate);
    }

    [Fact]
    public async Task UpdateBusinessDate_RejectsDatesOutsideSafeRange()
    {
        var service = CreateService(
            new FakeRepository(),
            new CaptureAuditWriter(),
            new TestBusinessDateProvider(new DateOnly(2026, 7, 21)));

        await Assert.ThrowsAsync<BusinessDateValidationException>(() =>
            service.UpdateBusinessDateSettingsAsync(
                new UpdateBusinessDateRequest(new DateOnly(2040, 1, 1)),
                Guid.NewGuid(),
                CancellationToken.None));
    }

    private static ApplicationSettingsService CreateService(
        FakeRepository repository,
        CaptureAuditWriter auditWriter,
        TestBusinessDateProvider? businessDateProvider = null,
        FakeAutomationRunner? automation = null) =>
        new(
            repository,
            auditWriter,
            businessDateProvider ?? new TestBusinessDateProvider(new DateOnly(2026, 7, 21)),
            automation ?? new FakeAutomationRunner(),
            TimeProvider.System,
            NullLogger<ApplicationSettingsService>.Instance);

    private sealed class FakeRepository : IApplicationSettingRepository
    {
        public ApplicationSetting? Setting { get; set; }
        public int SaveChangesCount { get; private set; }

        public Task<ApplicationSetting?> FindAsync(string key, CancellationToken cancellationToken) => Task.FromResult(Setting);
        public Task<ApplicationSetting?> FindForUpdateAsync(string key, CancellationToken cancellationToken) => Task.FromResult(Setting);
        public void Add(ApplicationSetting setting) => Setting = setting;
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CaptureAuditWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Requests { get; } = [];

        public AuditEvent? Add(AuditEventWriteRequest request)
        {
            Requests.Add(request);
            return null;
        }
    }

    private sealed class FakeAutomationRunner : IRegularAccrualAutomationRunner
    {
        public DateOnly? ReceivedDate { get; private set; }
        public Guid? ReceivedActorUserId { get; private set; }

        public Task<RegularAccrualAutomationRunResult> RunCurrentMonthAsync(CancellationToken cancellationToken) =>
            RunForDateAsync(new DateOnly(2026, 7, 21), null, cancellationToken);

        public Task<RegularAccrualAutomationRunResult> RunForDateAsync(DateOnly businessDate, Guid? actorUserId, CancellationToken cancellationToken)
        {
            ReceivedDate = businessDate;
            ReceivedActorUserId = actorUserId;
            return Task.FromResult(new RegularAccrualAutomationRunResult(true, 2, 3, "Готово"));
        }
    }
}
