using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;

namespace GarageBalance.Api.Application.Backups;

/// <summary>Projects only safe operational information; provider locators and errors never cross the API boundary.</summary>
public static class DatabaseBackupProtection
{
    public static DatabaseBackupFileDto Describe(
        DatabaseBackupFileDto backup,
        StorageObject storageObject,
        EffectiveStorageConfiguration configuration,
        DateTimeOffset now,
        string? rejectedDestinationId = null)
    {
        var policy = configuration.Policies.Single(item => item.DataClass == StorageDataClass.DatabaseBackup);
        var pool = configuration.Pools.Single(item => item.Id == policy.PoolId);
        var destinations = configuration.Destinations
            .Where(item => pool.DestinationIds.Contains(item.Id) && item.State != StorageDestinationState.Disabled &&
                item.Capabilities.HasFlag(StorageCapability.Read) && item.Capabilities.HasFlag(StorageCapability.Stat))
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var available = storageObject.Replicas.Where(replica =>
            destinations.ContainsKey(replica.DestinationId) &&
            replica.FailureDomain == destinations[replica.DestinationId].FailureDomain &&
            replica.DestinationId != rejectedDestinationId &&
            replica.State == StorageReplicaState.Available &&
            replica.Generation == storageObject.CommittedGeneration &&
            replica.SizeBytes == storageObject.SizeBytes &&
            string.Equals(replica.Sha256, storageObject.Sha256, StringComparison.OrdinalIgnoreCase)).ToArray();
        var availableCopies = available.Select(item => destinations[item.DestinationId].FailureDomain).Distinct().Count();
        var offsiteCopies = available.Where(item => destinations[item.DestinationId].Type != StorageProviderType.LocalFileSystem)
            .Select(item => destinations[item.DestinationId].FailureDomain).Distinct().Count();
        var protectedEnough = availableCopies >= policy.RequiredIndependentCopies && offsiteCopies >= policy.MinimumOffsiteCopies;
        var state = storageObject.State switch
        {
            StorageObjectState.Deleting => "deleting",
            StorageObjectState.Deleted => "deleted",
            _ when protectedEnough => "protected",
            StorageObjectState.Creating or StorageObjectState.CreatedLocal or StorageObjectState.ProtectionPending => "protection_pending",
            _ when availableCopies > 0 => "protection_degraded",
            _ => "failed"
        };
        var terminal = storageObject.State is StorageObjectState.Deleting or StorageObjectState.Deleted;
        return backup with
        {
            ProtectionState = state,
            RequiredCopies = policy.RequiredIndependentCopies,
            AvailableCopies = availableCopies,
            DesiredCopies = policy.DesiredCopies,
            RequiredOffsiteCopies = policy.MinimumOffsiteCopies,
            AvailableOffsiteCopies = offsiteCopies,
            ProtectionLagSeconds = terminal || (protectedEnough && availableCopies >= policy.DesiredCopies)
                ? 0 : (long)Math.Max(0, (now - storageObject.CreatedAtUtc).TotalSeconds),
            LastVerifiedAtUtc = available.Select(item => item.LastVerifiedAtUtc).DefaultIfEmpty().Min(),
            Replicas = storageObject.Replicas.OrderBy(item => item.DestinationId, StringComparer.Ordinal)
                .Select(replica => new DatabaseBackupReplicaDto(
                    replica.DestinationId,
                    configuration.Destinations.FirstOrDefault(item => item.Id == replica.DestinationId)?.Type == StorageProviderType.LocalFileSystem
                        ? "local" : "remote",
                    replica.DestinationId == rejectedDestinationId ? "corrupted"
                        : !destinations.ContainsKey(replica.DestinationId) ? "disabled"
                        : replica.State == StorageReplicaState.Available && !available.Contains(replica) ? "stale"
                        : replica.State.ToString().ToLowerInvariant(),
                    replica.LastVerifiedAtUtc,
                    SafeError(replica.DestinationId == rejectedDestinationId ? "ChecksumOrStale" : replica.LastErrorCategory))).ToArray()
        };
    }

    private static string? SafeError(string? category) => category switch
    {
        null or "" => null,
        "CredentialsExpired" or "ProviderForbidden" => "Проверьте разрешения доступа к хранилищу.",
        "QuotaOrReadOnly" => "В хранилище недостаточно места или запрещена запись.",
        "ObjectMissing" => "Копия не найдена. Требуется повторная доставка.",
        "ChecksumOrStale" => "Копия повреждена или устарела и не используется.",
        "TlsSecurity" => "Не удалось подтвердить безопасность соединения.",
        "RateLimited" => "Хранилище временно ограничило частоту запросов.",
        "UnknownOutcome" => "Результат доставки уточняется проверкой.",
        "ArchivePending" => "Ожидается извлечение копии из архива.",
        _ => "Хранилище временно недоступно. Проверьте состояние защиты."
    };
}
