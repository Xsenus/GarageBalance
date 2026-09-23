using System.Text.Json;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Backups;

public sealed class DatabaseBackupProtectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('a', 64);

    [Theory]
    [InlineData("protected", "Защищена", "active")]
    [InlineData("protection_degraded", "Защита ослаблена", "warning")]
    [InlineData("protection_pending", "Ожидает копирования", "archived")]
    [InlineData("failed", "Требует внимания", "danger")]
    [InlineData("manifest_missing", "Нет манифеста", "warning")]
    [InlineData("local_verified", "Проверена локально", "active")]
    [InlineData("deleting", "Удаляется", "archived")]
    [InlineData("deleted", "Удалена", "archived")]
    [InlineData("local_only", "Только локально", "archived")]
    [InlineData("future", "Требует проверки", "archived")]
    public void BackupLabelsAndTonesAreStableAndSerialized(string state, string label, string tone)
    {
        var dto = new DatabaseBackupFileDto("backup.pgdump", 10, Now, "manual", ProtectionState: state);
        Assert.Equal(label, dto.ProtectionLabel);
        Assert.Equal(tone, dto.ProtectionTone);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(label, document.RootElement.GetProperty("protectionLabel").GetString());
        Assert.Equal(tone, document.RootElement.GetProperty("protectionTone").GetString());
    }

    [Theory]
    [InlineData("available", "Проверена")]
    [InlineData("pending", "В очереди")]
    [InlineData("uploading", "Копируется")]
    [InlineData("unknown", "Уточняется")]
    [InlineData("verificationpending", "Ожидает проверки")]
    [InlineData("missing", "Не найдена")]
    [InlineData("corrupted", "Повреждена")]
    [InlineData("stale", "Устарела")]
    [InlineData("failed", "Ошибка")]
    [InlineData("deleting", "Удаляется")]
    [InlineData("deleted", "Удалена")]
    [InlineData("disabled", "Отключена")]
    [InlineData("new-provider-state", "Требует проверки")]
    public void ReplicaLabelsAreStableAndSafe(string state, string expected) =>
        Assert.Equal(expected, new DatabaseBackupReplicaDto("test", "remote", state, null, null).StateLabel);

    [Theory]
    [InlineData("not_configured", "Проверка восстановления не настроена")]
    [InlineData("not_run", "Проверка восстановления ещё не запускалась")]
    [InlineData("running", "Выполняется проверка восстановления")]
    [InlineData("stale", "Проверку восстановления пора повторить")]
    [InlineData("failed", "Проверка восстановления завершилась ошибкой")]
    [InlineData("verified", "Восстановление проверено")]
    [InlineData("invalid", "Результат проверки восстановления недоступен")]
    public void RestoreMessagesAreStableAndSafe(string state, string expected) =>
        Assert.Equal(expected, new DatabaseRestoreVerificationDto(state, null, null, null, 168).Message);

    [Fact]
    public void CountsIndependentCurrentCopiesAndReportsDebtWithoutProviderSecrets()
    {
        var item = CreateObject();
        item.Replicas.Add(Replica("remote-a", error: "ProviderForbidden"));
        item.Replicas[1].State = StorageReplicaState.Failed;
        item.Replicas[1].NativeLocator = "private-bucket/secret-path";
        item.Replicas[1].LastError = "password=do-not-expose";
        var result = Describe(item);
        Assert.Equal("protection_degraded", result.ProtectionState);
        Assert.Equal(1, result.AvailableCopies);
        Assert.Equal(0, result.AvailableOffsiteCopies);
        Assert.Equal(2, result.RequiredCopies);
        Assert.Equal(3, result.DesiredCopies);
        Assert.Equal(3600, result.ProtectionLagSeconds);
        Assert.Contains("разрешения", result.Replicas![1].Error);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("private-bucket", json);
        Assert.DoesNotContain("secret-path", json);
        Assert.DoesNotContain("do-not-expose", json);
    }

    [Fact]
    public void CountsDomainsOnceAndNeverTreatsTwoLocalCopiesAsOffsiteProtection()
    {
        var item = CreateObject();
        item.Replicas.Add(Replica("local-b"));
        item.Replicas.Add(Replica("local-c"));
        var result = Describe(item);
        Assert.Equal(2, result.AvailableCopies);
        Assert.Equal(0, result.AvailableOffsiteCopies);
        Assert.Equal("protection_degraded", result.ProtectionState);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("disabled")]
    [InlineData("outside-pool")]
    [InlineData("failure-domain")]
    public void DoesNotCountStaleDisabledOrOutOfPoolReplica(string invalid)
    {
        var item = CreateObject();
        var replica = Replica(invalid == "disabled" ? "disabled" : invalid == "outside-pool" ? "outside" : "remote-a");
        if (invalid == "generation") replica.Generation++;
        if (invalid == "hash") replica.Sha256 = new string('b', 64);
        if (invalid == "size") replica.SizeBytes++;
        if (invalid == "failure-domain") replica.FailureDomain = "previous-domain";
        item.Replicas.Add(replica);
        var result = Describe(item);
        Assert.Equal(1, result.AvailableCopies);
        Assert.NotEqual("protected", result.ProtectionState);
        Assert.Contains(result.Replicas!, value => value.State is "stale" or "disabled");
    }

    [Theory]
    [InlineData(StorageCapability.Read, StorageDestinationState.Enabled)]
    [InlineData(StorageCapability.Stat, StorageDestinationState.Draining)]
    [InlineData(StorageCapability.Read, StorageDestinationState.ReadOnly)]
    public void UnreadableDestinationsNeverCountAsProtection(StorageCapability missing, StorageDestinationState state)
    {
        var item = CreateObject();
        item.Replicas.Add(Replica("remote-a"));
        var configuration = Configuration();
        configuration = configuration with
        {
            Destinations = configuration.Destinations.Select(destination =>
            destination.Id == "remote-a" ? destination with { Capabilities = destination.Capabilities & ~missing, State = state } : destination).ToArray()
        };
        var result = DatabaseBackupProtection.Describe(new("backup.pgdump", 10, Now.AddHours(-1), "manual"), item, configuration, Now);
        Assert.Equal(1, result.AvailableCopies);
        Assert.Equal(0, result.AvailableOffsiteCopies);
        Assert.Equal("protection_degraded", result.ProtectionState);
    }

    [Fact]
    public void ProtectedStillReportsDesiredCopyDebtAndOldestVerification()
    {
        var item = CreateObject();
        item.Replicas.Add(Replica("remote-a"));
        item.Replicas[1].LastVerifiedAtUtc = Now.AddMinutes(-30);
        var result = Describe(item);
        Assert.Equal("protected", result.ProtectionState);
        Assert.Equal(2, result.AvailableCopies);
        Assert.Equal(Now.AddMinutes(-30), result.LastVerifiedAtUtc);
        Assert.Equal(3600, result.ProtectionLagSeconds);
        item.Replicas.Add(Replica("remote-b"));
        Assert.Equal(0, Describe(item).ProtectionLagSeconds);
    }

    [Theory]
    [InlineData(StorageObjectState.Deleting, "deleting")]
    [InlineData(StorageObjectState.Deleted, "deleted")]
    [InlineData(StorageObjectState.ProtectionPending, "protection_pending")]
    [InlineData(StorageObjectState.Protected, "failed")]
    public void EmptyAndTerminalStatesStayHonest(StorageObjectState state, string expected)
    {
        var item = CreateObject();
        item.State = state;
        item.Replicas.Clear();
        var result = Describe(item);
        Assert.Equal(expected, result.ProtectionState);
        Assert.Null(result.LastVerifiedAtUtc);
    }

    [Theory]
    [InlineData("QuotaOrReadOnly")]
    [InlineData("ObjectMissing")]
    [InlineData("ChecksumOrStale")]
    [InlineData("TlsSecurity")]
    [InlineData("RateLimited")]
    [InlineData("UnknownOutcome")]
    [InlineData("ArchivePending")]
    [InlineData("Unexpected-secret-category")]
    public void ErrorsAreAllowlistedUserText(string category)
    {
        var item = CreateObject();
        item.Replicas[0].LastErrorCategory = category;
        var error = Describe(item).Replicas![0].Error;
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.DoesNotContain(category, error);
    }

    private static DatabaseBackupFileDto Describe(StorageObject item) => DatabaseBackupProtection.Describe(
        new("backup.pgdump", 10, Now.AddHours(-1), "manual"), item, Configuration(), Now);

    private static StorageObject CreateObject() => new()
    {
        CommittedGeneration = 1,
        SizeBytes = 10,
        Sha256 = Hash,
        CreatedAtUtc = Now.AddHours(-1),
        State = StorageObjectState.Protected,
        Replicas = [Replica("local-a")]
    };

    private static StorageObjectReplica Replica(string id, string? error = null) => new()
    {
        DestinationId = id,
        Generation = 1,
        State = StorageReplicaState.Available,
        SizeBytes = 10,
        Sha256 = Hash,
        LastVerifiedAtUtc = Now,
        LastErrorCategory = error,
        FailureDomain = id == "local-c" ? "local-b" : id
    };

    private static EffectiveStorageConfiguration Configuration()
    {
        var destinations = new[] { "local-a", "local-b", "local-c", "remote-a", "remote-b", "disabled", "outside" }
            .Select(id => new StorageDestinationOptions
            {
                Id = id,
                FailureDomain = id == "local-c" ? "local-b" : id,
                Type = id.StartsWith("local", StringComparison.Ordinal) ? StorageProviderType.LocalFileSystem : StorageProviderType.S3Compatible,
                State = id == "disabled" ? StorageDestinationState.Disabled : StorageDestinationState.Enabled,
                RootPath = Path.GetTempPath(),
                Endpoint = "https://s3.example.test",
                Bucket = "test",
                AllowedEndpointHosts = ["s3.example.test"],
                Capabilities = ["Read", "Write", "Stat", "ServerSideEncryption"]
            }).ToList();
        return new StorageConfigurationResolver(Options.Create(new StorageOptions
        {
            Mode = StorageMode.AsyncMirror,
            Destinations = destinations,
            Pools = [new() { Id = "backups", DestinationIds = destinations.Where(item => item.Id != "outside").Select(item => item.Id).ToList() }],
            Policies = [new() { Id = "backups", PoolId = "backups", DataClass = StorageDataClass.DatabaseBackup,
                RequiredIndependentCopies = 2, DesiredCopies = 3, MinimumOffsiteCopies = 1 }]
        }), Options.Create(new DatabaseBackupOptions())).Resolve();
    }
}
