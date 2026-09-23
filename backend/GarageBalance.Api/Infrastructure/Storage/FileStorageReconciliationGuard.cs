using System.Text.Json;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;

namespace GarageBalance.Api.Infrastructure.Storage;

/// <summary>Durable, fail-closed operator pause independent of the production database.</summary>
public sealed class FileStorageReconciliationGuard : IStorageReconciliationGuard
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string directory;
    private readonly string statePath;
    private readonly TimeProvider timeProvider;

    public FileStorageReconciliationGuard(StorageConfigurationResolver resolver, TimeProvider timeProvider)
    {
        var configuration = resolver.Resolve();
        var local = configuration.Destinations.FirstOrDefault(item => item.Type == StorageProviderType.LocalFileSystem)
            ?? throw new InvalidOperationException("A local maintenance directory is required.");
        directory = DatabaseBackupPathResolver.Resolve(local.RootPath ?? "auto");
        statePath = Path.Combine(directory, ".storage-reconciliation.json");
        this.timeProvider = timeProvider;
    }

    public Task<StorageReconciliationSafetyState> GetStateAsync(CancellationToken cancellationToken) =>
        MutateAsync(null, cancellationToken);

    public async Task<StorageReconciliationSafetyState> PeekStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var input = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (input.Length > 128 * 1024)
            {
                return Unavailable();
            }
            var document = await JsonSerializer.DeserializeAsync<SafetyDocument>(input, JsonOptions, cancellationToken);
            return document is { SchemaVersion: 1, State: not null, History: not null } ? document.State : Unavailable();
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return NewDocument().State;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Unavailable();
        }
    }

    public Task<StorageReconciliationSafetyState> PauseAsync(int anomalyCount, CancellationToken cancellationToken)
    {
        if (anomalyCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(anomalyCount));
        }
        return MutateAsync(document => document with
        {
            State = new StorageReconciliationSafetyState(true, true, anomalyCount,
                timeProvider.GetUtcNow(), document.State.ResumedAtUtc, "MassReplicaAnomalies"),
            History = Append(document.History, new SafetyEvent(timeProvider.GetUtcNow(), "paused", "MassReplicaAnomalies"))
        }, cancellationToken);
    }

    public async Task<StorageReconciliationSafetyState> ResumeAsync(string reason, CancellationToken cancellationToken)
    {
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > 500 || reason.Any(char.IsControl))
        {
            throw new ArgumentException("An operator reason between 3 and 500 characters is required.", nameof(reason));
        }
        var result = await MutateAsync(document => document with
        {
            State = new StorageReconciliationSafetyState(false, true, 0, document.State.PausedAtUtc,
                timeProvider.GetUtcNow(), null),
            History = Append(document.History, new SafetyEvent(timeProvider.GetUtcNow(), "resumed", reason))
        }, cancellationToken, allowInvalidStateReset: true);
        if (!result.PersistenceAvailable)
        {
            throw new IOException("Reconciliation remains paused because its durable state is unavailable.");
        }
        return result;
    }

    private async Task<StorageReconciliationSafetyState> MutateAsync(
        Func<SafetyDocument, SafetyDocument>? mutation,
        CancellationToken cancellationToken,
        bool allowInvalidStateReset = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(directory);
            await using var fileLock = new FileStream(statePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            SafetyDocument document;
            try
            {
                if (File.Exists(statePath))
                {
                    await using var input = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (input.Length > 128 * 1024)
                    {
                        throw new JsonException("State exceeds its bounded size.");
                    }
                    document = await JsonSerializer.DeserializeAsync<SafetyDocument>(input, JsonOptions, cancellationToken)
                        ?? throw new JsonException("State is empty.");
                    if (document.SchemaVersion != 1 || document.State is null || document.History is null)
                    {
                        throw new JsonException("State schema is invalid.");
                    }
                }
                else
                {
                    document = NewDocument();
                }
            }
            catch (JsonException) when (allowInvalidStateReset)
            {
                document = NewDocument() with { State = Unavailable() };
            }
            if (mutation is not null)
            {
                document = mutation(document);
            }
            // Even reads prove that the next pause can be durably recorded. Failed persistence blocks repair.
            temporaryPath = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(output, document, JsonOptions, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, statePath, overwrite: true);
            temporaryPath = null;
            return document.State;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Unavailable();
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static SafetyDocument NewDocument() => new(1,
        new StorageReconciliationSafetyState(false, true, 0, null, null, null), []);

    private static StorageReconciliationSafetyState Unavailable() => new(true, false, 0, null, null, "SafetyStateUnavailable");

    private static IReadOnlyList<SafetyEvent> Append(IReadOnlyList<SafetyEvent> history, SafetyEvent item) =>
        history.TakeLast(99).Append(item).ToArray();

    private sealed record SafetyDocument(int SchemaVersion, StorageReconciliationSafetyState State, IReadOnlyList<SafetyEvent> History);
    private sealed record SafetyEvent(DateTimeOffset AtUtc, string Action, string Reason);
}
