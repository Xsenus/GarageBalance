using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GarageBalance.Api.Application.Backups;

namespace GarageBalance.StorageTool;

public sealed class MigrationToolException(string message) : Exception(message);

public sealed record MigrationCommandOptions(string Command, bool Execute, string? Checkpoint, int PageSize, int MaxJobs, string? Output, string? Reason = null)
{
    public static MigrationCommandOptions Parse(string[] args)
    {
        string[] commands = ["inventory", "plan", "copy", "verify", "diff", "delta-sync", "resume", "repair", "status", "report", "cutover-check", "rollback-check", "resume-reconciliation"];
        var command = args.FirstOrDefault()?.ToLowerInvariant();
        if (command is null || !commands.Contains(command, StringComparer.Ordinal))
            throw new MigrationToolException("Unknown command; use --help.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var execute = false;
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index] == "--execute") { execute = true; continue; }
            if (args[index] is not ("--checkpoint" or "--page-size" or "--max-jobs" or "--output" or "--reason") ||
                index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[index], args[++index]))
                throw new MigrationToolException("Invalid, duplicate or missing option; use --help.");
        }
        int Number(string key, int fallback, int maximum) => !values.TryGetValue(key, out var value) ? fallback :
            int.TryParse(value, out var parsed) && parsed > 0 && parsed <= maximum ? parsed :
            throw new MigrationToolException("Numeric option is outside the allowed range.");
        values.TryGetValue("--checkpoint", out var checkpoint);
        values.TryGetValue("--output", out var output);
        values.TryGetValue("--reason", out var reason);
        if (command == "resume-reconciliation" && execute && (reason is null || reason.Trim().Length is < 3 or > 500 || reason.Any(char.IsControl)))
            throw new MigrationToolException("Resuming reconciliation requires --execute --reason with a safe operator explanation (3–500 characters).");
        if (new[] { checkpoint, output }.Any(path => path is not null && path.EndsWith(".pgdump.manifest.json", StringComparison.OrdinalIgnoreCase)))
            throw new MigrationToolException("Checkpoint and report output may not overwrite backup manifests.");
        if ((command is "resume" or "delta-sync" || execute && command == "copy") && string.IsNullOrWhiteSpace(checkpoint))
            throw new MigrationToolException("This command requires --checkpoint <private-json-path>.");
        if (checkpoint is not null && output is not null && string.Equals(Path.GetFullPath(checkpoint), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new MigrationToolException("Report output must not overwrite its checkpoint.");
        return new(command, execute, checkpoint, Number("--page-size", 200, 1000), Number("--max-jobs", 100, 10000), output, reason);
    }
}

public sealed record MigrationSnapshotFile(string FileName, long SizeBytes, string Sha256, DatabaseBackupManifest Manifest);
public sealed record MigrationCheckpoint(int SchemaVersion, Guid RunId, string ConfigurationFingerprint,
    DateTimeOffset StartedAtUtc, DateTimeOffset UpdatedAtUtc, string State, int RegisteredCount, int ProcessedJobs,
    IReadOnlyList<MigrationSnapshotFile> Files, IReadOnlyList<Guid> ObjectIds);

public sealed class MigrationCheckpointStore(string path) : IAsyncDisposable
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.GetFullPath(path);
    private FileStream? _lock;

    public Task LockAsync(CancellationToken cancellationToken)
    {
        EnsureSafePath(_path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        try
        {
            if (File.Exists(_path + ".lock") && File.GetAttributes(_path + ".lock").HasFlag(FileAttributes.ReparsePoint))
                throw new MigrationToolException("Checkpoint lock may not be a symlink or reparse point.");
            // Keep the inode stable on Unix: deleting a lock file can allow two independent holders.
            _lock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            RestrictUnixPermissions(_path + ".lock");
        }
        catch (IOException) { throw new MigrationToolException("Another operation owns this checkpoint."); }
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task<MigrationCheckpoint> ReadAsync(CancellationToken cancellationToken)
    {
        EnsureSafePath(_path);
        if (!File.Exists(_path) || new FileInfo(_path).Length > 32 * 1024 * 1024)
            throw new MigrationToolException("Checkpoint is missing or exceeds its safe size limit.");
        try
        {
            await using var stream = File.OpenRead(_path);
            var envelope = await JsonSerializer.DeserializeAsync<CheckpointEnvelope>(stream, JsonOptions, cancellationToken);
            if (envelope is null || envelope.SchemaVersion != 1 || envelope.Payload.SchemaVersion != 1 ||
                !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(envelope.Sha256),
                    SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(envelope.Payload, JsonOptions))) ||
                envelope.Payload.RegisteredCount < 0 || envelope.Payload.RegisteredCount > envelope.Payload.Files.Count ||
                envelope.Payload.Files.Count > LocalBackupInspector.MaximumFiles ||
                envelope.Payload.ObjectIds.Count > LocalBackupInspector.MaximumFiles ||
                envelope.Payload.Files.Any(file => !LocalBackupInspector.IsManagedName(file.FileName) || file.SizeBytes <= 0 ||
                    file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) ||
                envelope.Payload.Files.Select(file => file.FileName).Distinct(StringComparer.Ordinal).Count() != envelope.Payload.Files.Count)
                throw new MigrationToolException("Checkpoint validation failed; do not use it for migration.");
            return envelope.Payload;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or NullReferenceException)
        { throw new MigrationToolException("Checkpoint is invalid or damaged."); }
    }

    public Task SaveAsync(MigrationCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(checkpoint, JsonOptions)));
        return AtomicWriteAsync(_path, JsonSerializer.Serialize(new CheckpointEnvelope(1, hash, checkpoint), JsonOptions), cancellationToken);
    }

    public static Task WriteReportAsync(string path, string json, CancellationToken cancellationToken) =>
        AtomicWriteAsync(Path.GetFullPath(path), json, cancellationToken);

    private static async Task AtomicWriteAsync(string path, string contents, CancellationToken cancellationToken)
    {
        EnsureSafePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                RestrictUnixPermissions(temporary);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(contents), cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void EnsureSafePath(string path)
    {
        if (Path.GetExtension(path) != ".json") throw new MigrationToolException("Checkpoint and report must use a .json file outside the source archives.");
        var current = new FileInfo(path) as FileSystemInfo;
        while (current is not null)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new MigrationToolException("Symlinks and reparse points are not allowed for checkpoint or report output.");
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent;
        }
    }

    private static void RestrictUnixPermissions(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public ValueTask DisposeAsync() => _lock?.DisposeAsync() ?? ValueTask.CompletedTask;
    private sealed record CheckpointEnvelope(int SchemaVersion, string Sha256, MigrationCheckpoint Payload);
}
