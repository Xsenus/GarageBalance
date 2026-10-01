using System.Globalization;

namespace GarageBalance.Api.Application.Storage;

public static class ReadableBackupLayout
{
    public const string CreatedAtMetadataKey = "garagebalance-created-at";
    public const string SourceMetadataKey = "garagebalance-backup-source";

    public static string NormalizeSource(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        // Validate the raw value before key normalization can trim trailing controls.
        if (source.Length > 128 || source.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.' or '/')))
            throw new ArgumentException("Backup source must be a short ASCII organization/server identifier.");
        return StorageObjectKey.Normalize(source);
    }

    public static string BuildKey(string prefix, string timeZoneId, DateTimeOffset createdAtUtc, Guid operationId, long generation, string? source = null)
    {
        if (operationId == Guid.Empty || generation < 1) throw new ArgumentException("A committed operation is required.");
        var local = TimeZoneInfo.ConvertTime(createdAtUtc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        var sourcePath = source is null ? string.Empty : NormalizeSource(source) + "/";
        // Full operation identity keeps immutable writes and repair operations collision-safe.
        return StorageObjectKey.Normalize(FormattableString.Invariant(
            $"{StorageObjectKey.Normalize(prefix)}/{sourcePath}{local:MM_yyyy}/sgk_{local:ddMMyyyy_HHmmss}_{operationId:N}_g{generation}.pgdump"));
    }

    public static string? TryBuildKey(EffectiveStorageDestination destination, StorageWriteRequest request)
    {
        if (destination.BackupPrefix is null ||
            !request.Metadata.TryGetValue("garagebalance-data-class", out var dataClass) || dataClass != "DatabaseBackup") return null;
        if (!request.Metadata.TryGetValue(CreatedAtMetadataKey, out var value) ||
            !DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var createdAt))
            throw new ArgumentException("Readable backup writes require their original creation time.");
        return BuildKey(destination.BackupPrefix, destination.BackupTimeZoneId, createdAt, request.OperationId, request.Generation, destination.BackupSource);
    }
}
