using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Settings;

namespace GarageBalance.Api.Tests.Settings;

public sealed class ApplicationSettingsServiceTests
{
    [Fact]
    public async Task GetPaymentDisplaySettings_ReturnsFalseWhenSettingIsMissing()
    {
        var service = new ApplicationSettingsService(new FakeRepository(), new CaptureAuditWriter());

        var result = await service.GetPaymentDisplaySettingsAsync(CancellationToken.None);

        Assert.False(result.ShowAllGarageOperationsByDefault);
    }

    [Fact]
    public async Task UpdatePaymentDisplaySettings_PersistsValueAndWritesAuditEvent()
    {
        var actorUserId = Guid.NewGuid();
        var repository = new FakeRepository();
        var auditWriter = new CaptureAuditWriter();
        var service = new ApplicationSettingsService(repository, auditWriter);

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
        var service = new ApplicationSettingsService(repository, auditWriter);

        var result = await service.UpdatePaymentDisplaySettingsAsync(
            new UpdatePaymentDisplaySettingsRequest(true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.ShowAllGarageOperationsByDefault);
        Assert.Equal(0, repository.SaveChangesCount);
        Assert.Empty(auditWriter.Requests);
    }

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
}
