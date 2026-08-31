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
    public async Task ActionComments_DefaultToOptionalWhenSettingIsMissing()
    {
        var service = CreateService(new FakeRepository(), new CaptureAuditWriter());

        var result = await service.GetActionCommentSettingsAsync(CancellationToken.None);

        Assert.False(result.Required);
        Assert.NotEqual(Guid.Empty, result.Version);
    }

    [Fact]
    public async Task ActionComments_UpdatePersistsValueAndWritesAudit()
    {
        var actorUserId = Guid.NewGuid();
        var repository = new FakeRepository();
        var auditWriter = new CaptureAuditWriter();
        var service = CreateService(repository, auditWriter);

        var result = await service.UpdateActionCommentSettingsAsync(
            new UpdateActionCommentSettingsRequest(true),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Required);
        Assert.Equal(ApplicationSettingsService.ActionCommentsRequiredKey, repository.Setting!.Key);
        Assert.True(repository.Setting.BooleanValue);
        Assert.Equal(actorUserId, repository.Setting.UpdatedByUserId);
        Assert.Equal("application_setting.action_comments_updated", Assert.Single(auditWriter.Requests).Action);
    }

    [Fact]
    public async Task TariffPanelsLayout_DefaultsToFortyPercentForAUser()
    {
        var repository = new FakeRepository();
        var userId = Guid.NewGuid();
        var service = CreateService(repository, new CaptureAuditWriter());

        var result = await service.GetTariffPanelsLayoutAsync(userId, CancellationToken.None);

        Assert.Equal(40, result.IrregularPaymentsWidthPercent);
        Assert.Equal($"users.{userId:N}.tariffs.bottom_panels_split", repository.LastRequestedKey);
    }

    [Fact]
    public async Task TariffPanelsLayout_PersistsTheAuthenticatedUsersWidthWithoutAuditNoise()
    {
        var repository = new FakeRepository();
        var auditWriter = new CaptureAuditWriter();
        var userId = Guid.NewGuid();
        var service = CreateService(repository, auditWriter);

        var result = await service.UpdateTariffPanelsLayoutAsync(
            new UpdateTariffPanelsLayoutRequest(28),
            userId,
            CancellationToken.None);

        Assert.Equal(28, result.IrregularPaymentsWidthPercent);
        Assert.Equal(28, repository.Setting!.IntegerValue);
        Assert.Equal($"users.{userId:N}.tariffs.bottom_panels_split", repository.Setting.Key);
        Assert.Equal(userId, repository.Setting.UpdatedByUserId);
        Assert.Equal(1, repository.SaveChangesCount);
        Assert.Empty(auditWriter.Requests);

        var updated = await service.UpdateTariffPanelsLayoutAsync(
            new UpdateTariffPanelsLayoutRequest(31),
            userId,
            CancellationToken.None);

        Assert.Equal(31, updated.IrregularPaymentsWidthPercent);
        Assert.Equal(31, repository.Setting.IntegerValue);
        Assert.Equal(2, repository.SaveChangesCount);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(61)]
    public async Task TariffPanelsLayout_RejectsWidthsThatCouldHideAPanel(int width)
    {
        var service = CreateService(new FakeRepository(), new CaptureAuditWriter());

        await Assert.ThrowsAsync<TariffPanelsLayoutValidationException>(() =>
            service.UpdateTariffPanelsLayoutAsync(
                new UpdateTariffPanelsLayoutRequest(width),
                Guid.NewGuid(),
                CancellationToken.None));
    }

    [Fact]
    public async Task GetPaymentDisplaySettings_ReturnsFalseWhenSettingIsMissing()
    {
        var service = CreateService(new FakeRepository(), new CaptureAuditWriter());

        var result = await service.GetPaymentDisplaySettingsAsync(CancellationToken.None);

        Assert.False(result.ShowAllGarageOperationsByDefault);
        Assert.NotEqual(Guid.Empty, result.Version);
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
    public async Task GetTariffTableDisplaySettings_DefaultsAllOptionalValuesToHidden()
    {
        var service = CreateService(new FakeRepository(), new CaptureAuditWriter());

        var result = await service.GetTariffTableDisplaySettingsAsync(CancellationToken.None);

        Assert.False(result.ShowPeriodicityColumn);
        Assert.False(result.ShowAccrualMonthColumn);
        Assert.False(result.ShowFundName);
        Assert.NotEqual(Guid.Empty, result.Version);
    }

    [Fact]
    public async Task UpdateTariffTableDisplaySettings_PersistsIndependentFlagsAndWritesAuditEvent()
    {
        var actorUserId = Guid.NewGuid();
        var repository = new FakeRepository();
        var auditWriter = new CaptureAuditWriter();
        var service = CreateService(repository, auditWriter);

        var result = await service.UpdateTariffTableDisplaySettingsAsync(
            new UpdateTariffTableDisplaySettingsRequest(true, false, ShowFundName: true),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.ShowPeriodicityColumn);
        Assert.False(result.ShowAccrualMonthColumn);
        Assert.True(result.ShowFundName);
        Assert.Equal(5, repository.Setting!.IntegerValue);
        Assert.Equal(ApplicationSettingsService.TariffTableVisibleColumnsKey, repository.Setting.Key);
        Assert.Equal(actorUserId, repository.Setting.UpdatedByUserId);
        Assert.Equal(1, repository.SaveChangesCount);
        var audit = Assert.Single(auditWriter.Requests);
        Assert.Equal("application_setting.tariff_table_columns_updated", audit.Action);
        Assert.Equal(false, audit.OldValues!["showFundName"]);
        Assert.Equal(true, audit.NewValues!["showFundName"]);
    }

    [Fact]
    public async Task UpdateTariffTableDisplaySettings_DoesNotCreateSettingForDefaultValues()
    {
        var repository = new FakeRepository();
        var auditWriter = new CaptureAuditWriter();
        var service = CreateService(repository, auditWriter);

        var result = await service.UpdateTariffTableDisplaySettingsAsync(
            new UpdateTariffTableDisplaySettingsRequest(false, false),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.ShowPeriodicityColumn);
        Assert.False(result.ShowAccrualMonthColumn);
        Assert.False(result.ShowFundName);
        Assert.Null(repository.Setting);
        Assert.Equal(0, repository.SaveChangesCount);
        Assert.Empty(auditWriter.Requests);
    }

    [Fact]
    public async Task GetSalaryAccrualSettings_ReturnsFirstDayWhenSettingIsMissing()
    {
        var service = CreateService(new FakeRepository(), new CaptureAuditWriter());

        var result = await service.GetSalaryAccrualSettingsAsync(CancellationToken.None);

        Assert.Equal(1, result.AccrualDay);
        Assert.NotEqual(Guid.Empty, result.Version);
    }

    [Fact]
    public async Task GetBusinessDateSettings_ReturnsConcurrencyVersionWhenOverrideIsMissing()
    {
        var service = CreateService(new FakeRepository(), new CaptureAuditWriter());

        var result = await service.GetBusinessDateSettingsAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Version);
    }

    [Fact]
    public async Task UpdateSalaryAccrualSettings_PersistsDayAndWritesAuditEvent()
    {
        var actorUserId = Guid.NewGuid();
        var repository = new FakeRepository();
        var auditWriter = new CaptureAuditWriter();
        var service = CreateService(repository, auditWriter);

        var result = await service.UpdateSalaryAccrualSettingsAsync(
            new UpdateSalaryAccrualSettingsRequest(15),
            actorUserId,
            CancellationToken.None);

        Assert.Equal(15, result.AccrualDay);
        Assert.Equal(15, repository.Setting!.IntegerValue);
        Assert.Equal(ApplicationSettingsService.SalaryAccrualDayKey, repository.Setting.Key);
        Assert.Equal(actorUserId, repository.Setting.UpdatedByUserId);
        Assert.Equal("application_setting.salary_accrual_day_updated", Assert.Single(auditWriter.Requests).Action);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public async Task UpdateSalaryAccrualSettings_RejectsDayOutsideEveryMonth(int accrualDay)
    {
        var service = CreateService(new FakeRepository(), new CaptureAuditWriter());

        await Assert.ThrowsAsync<SalaryAccrualSettingsValidationException>(() =>
            service.UpdateSalaryAccrualSettingsAsync(
                new UpdateSalaryAccrualSettingsRequest(accrualDay),
                Guid.NewGuid(),
                CancellationToken.None));
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
    public async Task PreviewBusinessDate_ReturnsAccrualScopeWithoutChangingState()
    {
        var repository = new FakeRepository();
        var auditWriter = new CaptureAuditWriter();
        var businessDateProvider = new TestBusinessDateProvider(new DateOnly(2026, 7, 21));
        var automation = new FakeAutomationRunner();
        var service = CreateService(repository, auditWriter, businessDateProvider, automation);

        var preview = await service.PreviewBusinessDateChangeAsync(
            new PreviewBusinessDateRequest(new DateOnly(2026, 9, 15), Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 7, 21), preview.CurrentEffectiveDate);
        Assert.Equal(new DateOnly(2026, 9, 15), preview.ProposedEffectiveDate);
        Assert.Equal(new DateOnly(2026, 9, 1), preview.Automation.AccountingMonth);
        Assert.True(preview.IsChange);
        Assert.Equal(new DateOnly(2026, 9, 15), automation.ReceivedPreviewDate);
        Assert.Null(repository.Setting);
        Assert.Equal(0, repository.SaveChangesCount);
        Assert.Empty(auditWriter.Requests);
        Assert.Equal(new DateOnly(2026, 7, 21), businessDateProvider.Today);
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
        public string? LastRequestedKey { get; private set; }

        public Task<ApplicationSetting?> FindAsync(string key, CancellationToken cancellationToken)
        {
            LastRequestedKey = key;
            return Task.FromResult(Setting);
        }

        public Task<ApplicationSetting?> FindForUpdateAsync(string key, CancellationToken cancellationToken)
        {
            LastRequestedKey = key;
            return Task.FromResult(Setting);
        }
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
        public DateOnly? ReceivedPreviewDate { get; private set; }

        public Task<RegularAccrualAutomationRunResult> RunCurrentMonthAsync(CancellationToken cancellationToken) =>
            RunForDateAsync(new DateOnly(2026, 7, 21), null, cancellationToken);

        public Task<RegularAccrualAutomationRunResult> RunForDateAsync(DateOnly businessDate, Guid? actorUserId, CancellationToken cancellationToken)
        {
            ReceivedDate = businessDate;
            ReceivedActorUserId = actorUserId;
            return Task.FromResult(new RegularAccrualAutomationRunResult(true, 2, 3, "Готово"));
        }

        public Task<RegularAccrualAutomationPreviewDto> PreviewForDateAsync(DateOnly businessDate, CancellationToken cancellationToken)
        {
            ReceivedPreviewDate = businessDate;
            return Task.FromResult(new RegularAccrualAutomationPreviewDto(
                new DateOnly(businessDate.Year, businessDate.Month, 1),
                12,
                4,
                3,
                1,
                48,
                []));
        }
    }
}
