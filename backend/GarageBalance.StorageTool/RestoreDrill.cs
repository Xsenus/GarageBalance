using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace GarageBalance.StorageTool;

public sealed record RestoreVerificationReport(int SchemaVersion, Guid RunId, DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc, bool Succeeded, DateTimeOffset? BackupCreatedAtUtc, double? RtoSeconds,
    string? SourceDestinationId, string? ErrorCode);

public static class RestoreDrill
{
    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, EffectiveStorageConfiguration effective,
        IStorageProviderRegistry registry, byte[] key, CancellationToken cancellationToken)
    {
        var reportPath = configuration["Storage:Recovery:VerificationReportPath"];
        if (string.IsNullOrWhiteSpace(reportPath)) throw new InvalidOperationException("A durable verification report path is required.");
        var runId = Guid.NewGuid();
        var databaseName = $"gb_restore_{runId:N}";
        var admin = ValidateAdminConnection(configuration["Recovery:DrillConnectionString"]);
        var apiDll = Path.GetFullPath(DisasterRecoveryCommands.Value(args, "--api-dll"));
        if (!File.Exists(apiDll) || Path.GetFileName(apiDll) != "GarageBalance.Api.dll") throw new InvalidOperationException("An existing application DLL is required.");
        var maximumSeconds = configuration.GetValue("Storage:Recovery:MaximumDrillSeconds", 1800);
        if (maximumSeconds is < 30 or > 7200) throw new InvalidOperationException("Drill deadline must be between 30 and 7200 seconds.");
        var maximumBackupBytes = configuration.GetValue("Storage:Recovery:MaximumBackupBytes", 20L * 1024 * 1024 * 1024);
        if (maximumBackupBytes is <= 0 or > 1024L * 1024 * 1024 * 1024) throw new InvalidOperationException("Backup size limit is invalid.");
        var requiredBackupSource = DisasterRecoveryCommands.Value(args, "--exclude-failure-domain");
        var scratch = Path.Combine(Path.GetTempPath(), $"garagebalance-drill-{runId:N}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        // Cross-process file lease protects the durable result from overlapping scheduled/operator runs.
        await using var lease = new FileStream(reportPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(maximumSeconds));
        var ct = deadline.Token;
        var report = new RestoreVerificationReport(1, runId, DateTimeOffset.UtcNow, null, false, null, null, null, "in_progress");
        var databaseCreated = false;
        var stage = "recovery_fetch";
        Process? api = null;
        await DisasterRecoveryCommands.WriteJsonAtomicAsync(reportPath, report, CancellationToken.None);
        try
        {
            RecoveryArchive.CreatePrivateDirectory(scratch);
            var recovered = Path.Combine(scratch, "recovered");
            Console.WriteLine($"restoreStage={stage}");
            var inventory = await DisasterRecoveryCommands.FetchArchiveAsync(args, effective, registry, key, recovered, ct);
            var backup = Path.Combine(scratch, "restore.pgdump");
            stage = "backup_fetch";
            Console.WriteLine($"restoreStage={stage}");
            var selected = await FetchIndependentBackupAsync(inventory, effective, registry, requiredBackupSource, backup, maximumBackupBytes, ct);
            report = report with { BackupCreatedAtUtc = selected.Backup.CreatedAtUtc, SourceDestinationId = selected.Replica.DestinationId };
            stage = "archive_toc";
            Console.WriteLine($"restoreStage={stage}");
            await RunPgRestoreAsync(configuration, admin, "--list", backup, null, ct);
            stage = "database_create";
            Console.WriteLine($"restoreStage={stage}");
            await using (var adminConnection = new NpgsqlConnection(admin.ConnectionString))
            {
                await adminConnection.OpenAsync(ct);
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", adminConnection);
                await create.ExecuteNonQueryAsync(ct);
                databaseCreated = true;
            }
            var target = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName, Pooling = false };
            stage = "database_restore";
            Console.WriteLine($"restoreStage={stage}");
            await RunPgRestoreAsync(configuration, target, "--exit-on-error", backup, databaseName, ct);
            stage = "schema_and_secret";
            Console.WriteLine($"restoreStage={stage}");
            await using var db = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().UseNpgsql(target.ConnectionString).Options);
            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
            if (applied.Length == 0 || (await db.Database.GetPendingMigrationsAsync(ct)).Any()) throw new InvalidDataException("Restored schema does not match this application version.");
            await db.Garages.AsNoTracking().Take(1).Select(item => item.Id).ToListAsync(ct);
            RecoveryArchive.VerifyCanary(Path.Combine(recovered, "key-ring"), inventory.Canary);
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var email = $"drill-{runId:N}@example.invalid";
            var role = new AppRole { Code = $"drill-{runId:N}", Name = "Isolated restore verifier", Permissions = [SystemPermissions.ReportsRead] };
            var user = new AppUser { Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = "Restore verifier", PasswordHash = new Pbkdf2PasswordHasher().HashPassword(password) };
            user.UserRoles.Add(new AppUserRole { User = user, Role = role });
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            var port = ReserveLoopbackPort();
            stage = "api_startup";
            Console.WriteLine($"restoreStage={stage}");
            var baseUrl = $"http://127.0.0.1:{port}";
            api = StartApi(apiDll, scratch, target.ConnectionString, Path.Combine(recovered, "key-ring"), runId, baseUrl);
            var stdout = DrainAsync(api.StandardOutput, ct);
            var stderr = DrainAsync(api.StandardError, ct, reportSafeCategories: true);
            try
            {
                using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
                { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
                await WaitForApiAsync(client, api, ct);
                stage = "api_login";
                Console.WriteLine($"restoreStage={stage}");
                using var denied = await client.GetAsync("/api/settings/backups", ct);
                if (denied.StatusCode != HttpStatusCode.Forbidden) throw new InvalidOperationException("Restore API sandbox boundary was not enforced.");
                using var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, ct);
                login.EnsureSuccessStatusCode();
                using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync(ct));
                var token = loginBody.RootElement.GetProperty("accessToken").GetString();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var me = await client.GetAsync("/api/auth/me", ct);
                me.EnsureSuccessStatusCode();
                var month = selected.Backup.CreatedAtUtc.ToString("yyyy-MM-01", System.Globalization.CultureInfo.InvariantCulture);
                stage = "api_report";
                Console.WriteLine($"restoreStage={stage}");
                using var response = await client.GetAsync($"/api/reports/consolidated?monthFrom={month}&monthTo={month}&limit=1", ct);
                response.EnsureSuccessStatusCode();
                using var reportBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (reportBody.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Report smoke returned no object.");
                report = report with { Succeeded = true, ErrorCode = null };
            }
            finally
            {
                await StopProcessAsync(api);
                await Task.WhenAll(stdout, stderr);
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"restoreFailureStage={stage};category=cancelled_or_deadline");
            report = report with { ErrorCode = cancellationToken.IsCancellationRequested ? "cancelled" : "deadline_exceeded" };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"restoreFailureStage={stage};category={exception.GetType().Name}");
            report = report with { ErrorCode = stage + "_failed" };
        }
        finally
        {
            if (api is not null)
            {
                try { await StopProcessAsync(api); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    report = report with { Succeeded = false, ErrorCode = "cleanup_failed" };
                }
                finally { api.Dispose(); }
            }
            try
            {
                if (databaseCreated)
                {
                    await using var adminConnection = new NpgsqlConnection(admin.ConnectionString);
                    using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await adminConnection.OpenAsync(cleanupDeadline.Token);
                    await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", adminConnection);
                    await drop.ExecuteNonQueryAsync(cleanupDeadline.Token);
                }
                // This path is generated locally for this run; no caller-controlled recursive delete target.
                if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                report = report with { Succeeded = false, ErrorCode = "cleanup_failed" };
            }
            report = report with { CompletedAtUtc = DateTimeOffset.UtcNow, RtoSeconds = Math.Round((DateTimeOffset.UtcNow - report.StartedAtUtc).TotalSeconds, 3) };
            await DisasterRecoveryCommands.WriteJsonAtomicAsync(reportPath, report, CancellationToken.None);
        }
        Console.WriteLine($"restoreDrillSucceeded={report.Succeeded}");
        Console.WriteLine($"restoreDrillErrorCode={report.ErrorCode ?? "none"}");
        Console.WriteLine($"restoreDrillRtoSeconds={report.RtoSeconds}");
        return report.Succeeded ? 0 : 22;
    }

    public static NpgsqlConnectionStringBuilder ValidateAdminConnection(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("A separate explicit Recovery:DrillConnectionString is required.");
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Timeout = 10, CommandTimeout = 30 };
        if (admin.Database != "postgres" || !IPAddress.TryParse(admin.Host, out var address) || !IPAddress.IsLoopback(address))
            throw new InvalidOperationException("Disposable restore requires a loopback PostgreSQL administrative database, not a production database.");
        return admin;
    }

    public static IEnumerable<(StorageManifestEntry Backup, StorageManifestReplicaEntry Replica)> SelectIndependentReplicas(
        RecoveryArchivePayload inventory, EffectiveStorageConfiguration effective, string excludedFailureDomain)
    {
        if (string.IsNullOrWhiteSpace(excludedFailureDomain)) throw new ArgumentException("The primary host failure domain must be explicitly excluded.");
        var policy = effective.Policies.Single(item => item.DataClass == StorageDataClass.DatabaseBackup);
        var pool = effective.Pools.Single(item => item.Id == policy.PoolId);
        return inventory.Backups.Where(item => item.DataClass == StorageDataClass.DatabaseBackup && item.State is not (StorageObjectState.Deleted or StorageObjectState.Deleting or StorageObjectState.Failed))
            .OrderByDescending(item => item.CreatedAtUtc).SelectMany(backup => backup.Replicas.Where(replica =>
                replica.State == StorageReplicaState.Available && replica.Generation == backup.Generation && replica.SizeBytes == backup.SizeBytes &&
                string.Equals(replica.Sha256, backup.Sha256, StringComparison.OrdinalIgnoreCase) && replica.FailureDomain != excludedFailureDomain && pool.DestinationIds.Contains(replica.DestinationId) &&
                effective.Destinations.Any(destination => destination.Id == replica.DestinationId && destination.FailureDomain == replica.FailureDomain &&
                    destination.TenantId == inventory.TenantId && destination.State != StorageDestinationState.Disabled && destination.Capabilities.HasFlag(StorageCapability.Read)))
                .Select(replica => (backup, replica)));
    }

    private static async Task<(StorageManifestEntry Backup, StorageManifestReplicaEntry Replica)> FetchIndependentBackupAsync(
        RecoveryArchivePayload inventory, EffectiveStorageConfiguration effective, IStorageProviderRegistry registry, string excluded,
        string output, long maximumBytes, CancellationToken ct)
    {
        foreach (var candidate in SelectIndependentReplicas(inventory, effective, excluded))
        {
            if (candidate.Backup.SizeBytes is <= 0 || candidate.Backup.SizeBytes > maximumBytes) continue;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(effective.Replication.OperationDeadlineSeconds));
            try
            {
                await using var source = await registry.GetRequired(candidate.Replica.DestinationId).OpenReadAsync(candidate.Replica.NativeLocator, deadline.Token);
                await using (var target = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
                {
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[64 * 1024];
                    long total = 0;
                    int count;
                    while ((count = await source.ReadAsync(buffer, deadline.Token)) != 0)
                    {
                        total += count;
                        if (total > candidate.Backup.SizeBytes) throw new InvalidDataException("Replica exceeds committed backup size.");
                        hash.AppendData(buffer, 0, count);
                        await target.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
                    }
                    if (total != candidate.Backup.SizeBytes || !string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), candidate.Backup.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Backup replica checksum does not match its authenticated catalog.");
                }
                return candidate;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (StorageProviderException) { }
            catch (InvalidDataException) { }
            catch (IOException) { }
            if (File.Exists(output)) File.Delete(output);
        }
        throw new IOException("No current independent verified backup replica could be fetched.");
    }

    private static async Task RunPgRestoreAsync(IConfiguration configuration, NpgsqlConnectionStringBuilder connection, string mode, string backup, string? database, CancellationToken ct)
    {
        var start = new ProcessStartInfo(configuration["DatabaseBackup:PgRestorePath"] ?? "pg_restore")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(mode);
        if (database is not null)
        {
            foreach (var argument in new[] { $"--host={connection.Host}", $"--port={connection.Port}", $"--username={connection.Username}", $"--dbname={database}", "--no-owner", "--no-privileges", "--no-comments", "--no-password" }) start.ArgumentList.Add(argument);
        }
        start.ArgumentList.Add(backup);
        // libpq variables such as PGHOSTADDR/PGSERVICE must not redirect the validated loopback target.
        foreach (var name in start.Environment.Keys.Where(name => name.StartsWith("PG", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(name);
        start.Environment["PGPASSWORD"] = connection.Password ?? string.Empty;
        using var process = Process.Start(start) ?? throw new IOException("pg_restore did not start.");
        var stdout = DrainAsync(process.StandardOutput, ct);
        var stderr = DrainAsync(process.StandardError, ct);
        try
        {
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"restorePgExitCode={process.ExitCode}");
                throw new InvalidDataException("pg_restore rejected the replica or restored database.");
            }
        }
        finally { await StopProcessAsync(process); }
    }

    private static Process StartApi(string dll, string scratch, string connection, string keyRing, Guid runId, string url)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = scratch, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll);
        // Do not inherit cloud credentials, production endpoints or development launch profile settings.
        var inherited = new[] { "PATH", "SystemRoot", "WINDIR", "DOTNET_ROOT", "TEMP", "TMP", "HOME", "USERPROFILE" }
            .ToDictionary(name => name, Environment.GetEnvironmentVariable);
        start.Environment.Clear();
        foreach (var pair in inherited.Where(item => item.Value is not null)) start.Environment[pair.Key] = pair.Value;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_URLS"] = url;
        start.Environment["ConnectionStrings__DefaultConnection"] = connection;
        start.Environment["RecoveryDrill__Enabled"] = "true";
        start.Environment["RecoveryDrill__RunId"] = runId.ToString("N");
        start.Environment["Jwt__SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        start.Environment["DataProtection__KeysPath"] = keyRing;
        start.Environment["HttpsRedirection__Enabled"] = "false";
        start.Environment["DiagnosticLogging__Enabled"] = "false";
        start.Environment["Logging__LogLevel__Default"] = "None";
        return Process.Start(start) ?? throw new IOException("Isolated application did not start.");
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static async Task WaitForApiAsync(HttpClient client, Process process, CancellationToken ct)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startup.CancelAfter(TimeSpan.FromSeconds(60));
        HttpStatusCode? lastStatus = null;
        while (true)
        {
            startup.Token.ThrowIfCancellationRequested();
            if (process.HasExited) throw new IOException("Isolated API exited before readiness.");
            try
            {
                using var response = await client.GetAsync("/health", startup.Token);
                if (lastStatus != response.StatusCode) Console.WriteLine($"restoreApiReadinessStatus={(int)response.StatusCode}");
                lastStatus = response.StatusCode;
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(250, startup.Token);
        }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken ct, bool reportSafeCategories = false)
    {
        var buffer = new char[4096];
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var tail = string.Empty;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
            {
                if (!reportSafeCategories) continue;
                var chunk = tail + new string(buffer, 0, count);
                foreach (var category in new[] { "OptionsValidationException", "InvalidOperationException", "NpgsqlException", "IOException", "SocketException" })
                    if (chunk.Contains(category, StringComparison.Ordinal) && reported.Add(category)) Console.Error.WriteLine($"restoreChildErrorCategory={category}");
                tail = chunk[^Math.Min(128, chunk.Length)..];
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async Task StopProcessAsync(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) when (process.HasExited) { }
        catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(deadline.Token);
    }
}
