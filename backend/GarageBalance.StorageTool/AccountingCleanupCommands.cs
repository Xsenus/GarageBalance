using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text.Json;
using System.Text.RegularExpressions;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace GarageBalance.StorageTool;

public sealed record AccountingCleanupCommandOptions(bool Execute, string? PlanPath, string? Fingerprint,
    string? BackupReceiptPath, string? ExpectedDatabase, string? ServiceName, int? ApplicationPort,
    string? Reason, int MaximumGarages, string? OutputPath)
{
    public static AccountingCleanupCommandOptions Parse(string[] args)
    {
        if (args.Length == 0 || !AccountingCleanupCommands.CanHandle(args[0])) throw Invalid();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var execute = false;
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index] == "--execute")
            {
                if (execute) throw Invalid();
                execute = true;
                continue;
            }
            if (args[index] is not ("--plan" or "--confirm" or "--backup-receipt" or "--expected-database" or "--service" or
                "--application-port" or "--reason" or "--max-garages" or "--output") || index + 1 >= args.Length ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[index], args[++index])) throw Invalid();
        }
        string? Value(string key) => values.GetValueOrDefault(key);
        int Number(string key, int fallback, int maximum) => !values.TryGetValue(key, out var raw) ? fallback :
            int.TryParse(raw, out var number) && number >= 1 && number <= maximum ? number : throw Invalid();
        var result = new AccountingCleanupCommandOptions(execute, Value("--plan"), Value("--confirm"), Value("--backup-receipt"),
            Value("--expected-database"), Value("--service"), values.ContainsKey("--application-port") ? Number("--application-port", 0, 65535) : null,
            Value("--reason"), Number("--max-garages", 100, 500), Value("--output"));
        if (values.ContainsKey("--max-garages") && !args[0].StartsWith("archived-garages-c", StringComparison.OrdinalIgnoreCase) &&
            !args[0].Equals("archived-garages-plan", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        if (execute)
        {
            if (AccountingCleanupCommands.IsPreview(args[0]) ||
                string.IsNullOrWhiteSpace(result.PlanPath) || string.IsNullOrWhiteSpace(result.BackupReceiptPath) ||
                string.IsNullOrWhiteSpace(result.Fingerprint) || result.Fingerprint.Length != 64 || !result.Fingerprint.All(Uri.IsHexDigit) ||
                string.IsNullOrWhiteSpace(result.ExpectedDatabase) || string.IsNullOrWhiteSpace(result.Reason) ||
                result.Reason.Trim().Length is < 3 or > 500 || result.Reason.Any(char.IsControl) || values.ContainsKey("--max-garages") ||
                (result.ServiceName is null) == (result.ApplicationPort is null)) throw Invalid();
        }
        else if (result.OutputPath is null || values.Keys.Any(key => key is not ("--output" or "--max-garages" or "--expected-database"))) throw Invalid();
        if (result.ServiceName is not null && !Regex.IsMatch(result.ServiceName, "^[A-Za-z0-9][A-Za-z0-9_.@:-]{0,119}$")) throw Invalid();
        foreach (var path in new[] { result.PlanPath, result.BackupReceiptPath, result.OutputPath }.OfType<string>())
            AccountingCleanupCommands.ValidatePrivateJsonPath(path);
        return result;
    }

    private static AccountingCleanupException Invalid() => new("cleanup_options_invalid",
        "Неверные параметры. Предпросмотр требует --output; удаление — --execute, --plan, --confirm, --backup-receipt, --expected-database, --reason и --service либо --application-port.");
}

public interface IAccountingCleanupHostEnvironment
{
    Task<AccountingCleanupExecutionIdentity> GetIdentityAsync(string? serviceName, int? applicationPort, CancellationToken cancellationToken);
}

public static class AccountingCleanupCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] PreviewCommands = ["archived-garages-plan", "archived-garages-purge-plan", "meter-history-plan", "archived-fund-balance-plan"];
    private static readonly string[] ExecuteCommands = ["archived-garages-cleanup", "archived-garages-purge", "meter-history-cleanup", "archived-fund-balance-repair"];
    public static bool CanHandle(string command) => IsPreview(command) ||
        ExecuteCommands.Contains(command, StringComparer.OrdinalIgnoreCase);
    public static bool IsPreview(string command) => PreviewCommands.Contains(command, StringComparer.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, CancellationToken cancellationToken,
        IAccountingCleanupHostEnvironment? hostEnvironment = null)
    {
        var committed = false;
        FileStream? output = null;
        string? ownedOutputPath = null;
        var reportComplete = false;
        try
        {
            var options = AccountingCleanupCommandOptions.Parse(args);
            var identity = options.Execute
                ? await (hostEnvironment ?? new AccountingCleanupHostEnvironment()).GetIdentityAsync(options.ServiceName, options.ApplicationPort, cancellationToken)
                : new(false, false, "preview");
            if (options.Execute && (!identity.HasHostPrivilege || !identity.ApplicationStopped))
                throw new AccountingCleanupException("cleanup_permission_denied", "Нужны привилегии администратора узла и остановленное приложение.");
            var connection = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connection))
                throw new AccountingCleanupException("cleanup_connection_missing", "ConnectionStrings__DefaultConnection не настроено.");
            var settings = new NpgsqlConnectionStringBuilder(connection);
            if (options.ExpectedDatabase is not null && settings.Database != options.ExpectedDatabase)
                throw new AccountingCleanupException("cleanup_database_mismatch", "Точное имя базы не совпадает с --expected-database.");
            if (options.OutputPath is not null)
            {
                output = OpenPrivateOutput(options.OutputPath);
                ownedOutputPath = options.OutputPath;
            }
            await using var db = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().UseNpgsql(connection).Options);
            var command = args[0].ToLowerInvariant();
            if (command is "archived-garages-purge-plan" or "archived-garages-purge" or "meter-history-plan" or "meter-history-cleanup"
                or "archived-fund-balance-plan" or "archived-fund-balance-repair")
            {
                if (options.Execute != (command is "archived-garages-purge" or "meter-history-cleanup" or "archived-fund-balance-repair"))
                    throw new AccountingCleanupException("cleanup_options_invalid", "Удаление выполняется только командой очистки с --execute.");
                object report;
                if (command.StartsWith("archived-fund", StringComparison.Ordinal))
                {
                    var repair = new ArchivedFundBalanceRepairEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System);
                    if (!options.Execute)
                    {
                        var repairPlan = await repair.PreviewAsync(cancellationToken);
                        report = repairPlan;
                        Console.WriteLine(JsonSerializer.Serialize(new
                        {
                            dryRun = true,
                            repairPlan.PlanId,
                            funds = repairPlan.Funds.Count,
                            blocked = repairPlan.BlockedFundNames.Count,
                            repairPlan.Fingerprint
                        }, JsonOptions));
                    }
                    else
                    {
                        await EnsureNoOtherClientsAsync(db, cancellationToken);
                        report = await repair.ExecuteAsync(await ReadAsync<ArchivedFundBalanceRepairPlan>(options.PlanPath!, cancellationToken), options.Fingerprint!,
                            await ReadAsync<AccountingCleanupBackupReceipt>(options.BackupReceiptPath!, cancellationToken), identity, options.Reason!, cancellationToken);
                        committed = true;
                    }
                }
                else if (command.StartsWith("archived", StringComparison.Ordinal))
                {
                    var purge = new ArchivedGarageFinancialPurgeEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System);
                    if (!options.Execute)
                    {
                        var purgePlan = await purge.PreviewAsync(cancellationToken);
                        report = purgePlan;
                        Console.WriteLine(JsonSerializer.Serialize(new
                        {
                            dryRun = true,
                            purgePlan.PlanId,
                            purgePlan.TotalArchived,
                            selected = purgePlan.Garages.Count,
                            garages = purgePlan.Garages.Select(row => row.GarageNumber),
                            blocked = purgePlan.BlockedGarageNumbers.Count,
                            accruals = purgePlan.Garages.Sum(row => row.AccrualCount),
                            operations = purgePlan.Garages.Sum(row => row.OperationCount),
                            fundOperations = purgePlan.Garages.Sum(row => row.FundOperationCount),
                            meterReadings = purgePlan.Garages.Sum(row => row.MeterReadingCount),
                            activeIncomeTotal = purgePlan.Garages.Sum(row => row.ActiveIncomeTotal),
                            purgePlan.Fingerprint
                        }, JsonOptions));
                    }
                    else
                    {
                        await EnsureNoOtherClientsAsync(db, cancellationToken);
                        report = await purge.ExecuteAsync(await ReadAsync<ArchivedGarageFinancialPurgePlan>(options.PlanPath!, cancellationToken), options.Fingerprint!,
                            await ReadAsync<AccountingCleanupBackupReceipt>(options.BackupReceiptPath!, cancellationToken), identity, options.Reason!, cancellationToken);
                        committed = true;
                    }
                }
                else
                {
                    var meters = new MeterHistoryCleanupEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System);
                    if (!options.Execute)
                    {
                        var meterPlan = await meters.PreviewAsync(cancellationToken);
                        report = meterPlan;
                        Console.WriteLine(JsonSerializer.Serialize(new
                        {
                            dryRun = true,
                            meterPlan.PlanId,
                            meterPlan.GarageCount,
                            meterPlan.ReadingCount,
                            meterPlan.KeptStartReadingCount,
                            meterPlan.ReplacementDeviceCount,
                            meterPlan.ReactivatedDeviceCount,
                            meterPlan.MeteredAccrualCount,
                            meterPlan.PaidMeteredAccrualCount,
                            meterPlan.MeteredAccrualTotal,
                            meterPlan.PaidMeteredTotal,
                            meterPlan.Fingerprint
                        }, JsonOptions));
                    }
                    else
                    {
                        await EnsureNoOtherClientsAsync(db, cancellationToken);
                        report = await meters.ExecuteAsync(await ReadAsync<MeterHistoryCleanupPlan>(options.PlanPath!, cancellationToken), options.Fingerprint!,
                            await ReadAsync<AccountingCleanupBackupReceipt>(options.BackupReceiptPath!, cancellationToken), identity, options.Reason!, cancellationToken);
                        committed = true;
                    }
                }
                if (output is not null)
                {
                    await JsonSerializer.SerializeAsync(output, report, report.GetType(), JsonOptions, CancellationToken.None);
                    await output.FlushAsync(CancellationToken.None);
                    output.Flush(flushToDisk: true);
                    reportComplete = true;
                }
                if (options.Execute) Console.WriteLine(JsonSerializer.Serialize(report, report.GetType(), JsonOptions));
                return 0;
            }
            var engine = new ArchivedGarageCleanupEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System);
            if (!options.Execute)
            {
                var plan = await engine.PreviewAsync(options.MaximumGarages, cancellationToken);
                await JsonSerializer.SerializeAsync(output!, plan, JsonOptions, cancellationToken);
                await output!.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
                reportComplete = true;
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    dryRun = true,
                    plan.PlanId,
                    plan.TotalArchived,
                    plan.TotalEligible,
                    selected = plan.EligibleGarages.Count,
                    protectedShown = plan.ProtectedGarages.Count,
                    plan.HasMoreEligible,
                    plan.Fingerprint
                }, JsonOptions));
                return 0;
            }
            await EnsureNoOtherClientsAsync(db, cancellationToken);
            var savedPlan = await ReadAsync<ArchivedGarageCleanupPlan>(options.PlanPath!, cancellationToken);
            var backup = await ReadAsync<AccountingCleanupBackupReceipt>(options.BackupReceiptPath!, cancellationToken);
            var result = await engine.ExecuteAsync(savedPlan, options.Fingerprint!, backup, identity, options.Reason!, cancellationToken);
            committed = true;
            if (output is not null)
            {
                await JsonSerializer.SerializeAsync(output, result, JsonOptions, CancellationToken.None);
                await output.FlushAsync(CancellationToken.None);
                output.Flush(flushToDisk: true);
                reportComplete = true;
            }
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Очистка прервана. Проверьте аудит и новый предпросмотр перед повторным запуском.");
            return 130;
        }
        catch (AccountingCleanupException error)
        {
            Console.Error.WriteLine($"{error.Code}: {error.Message}");
            return 2;
        }
        catch (Exception)
        {
            Console.Error.WriteLine(committed
                ? "cleanup_committed_report_failed: Очистка сохранена, но отчёт не записан. Проверьте аудит и новый предпросмотр."
                : "cleanup_outcome_unavailable: Не удалось получить итог операции. Проверьте аудит и новый предпросмотр; параметры подключения не выводятся.");
            return committed ? 8 : 6;
        }
        finally
        {
            if (output is not null) await output.DisposeAsync();
            if (ownedOutputPath is not null && !reportComplete && !committed) File.Delete(ownedOutputPath);
        }
    }

    private static async Task EnsureNoOtherClientsAsync(GarageBalanceDbContext db, CancellationToken cancellationToken)
    {
        var clients = await db.Database.SqlQuery<int>($"""
            SELECT COUNT(*)::int AS "Value" FROM pg_stat_activity
            WHERE datname = current_database() AND pid <> pg_backend_pid()
              AND (backend_type = 'client backend' OR backend_type IS NULL)
            """).SingleAsync(cancellationToken);
        if (clients != 0) throw new AccountingCleanupException("cleanup_other_clients_connected", "К базе подключены другие клиенты. Очистка не начата.");
    }

    public static void ValidatePrivateJsonPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".pgdump.manifest.json", StringComparison.OrdinalIgnoreCase))
            throw new AccountingCleanupException("cleanup_private_path_invalid", "Нужен абсолютный путь отдельного private JSON-файла.");
        FileSystemInfo? current = new FileInfo(path);
        while (current is not null)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new AccountingCleanupException("cleanup_private_path_invalid", "Ссылки и reparse points запрещены для плана и отчёта.");
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent;
        }
    }

    private static FileStream OpenPrivateOutput(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.WriteThrough };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var stream = new FileStream(path, options);
        try
        {
            if (OperatingSystem.IsWindows()) RestrictWindowsOutput(path);
            return stream;
        }
        catch
        {
            stream.Dispose();
            File.Delete(path);
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictWindowsOutput(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new AccountingCleanupException("cleanup_output_identity_unavailable", "Не удалось защитить private отчёт.");
        var permissions = new FileSecurity();
        permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            permissions.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(permissions);
    }

    private static async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new JsonException();
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<T>(file, JsonOptions, cancellationToken)
                ?? throw new AccountingCleanupException("cleanup_json_invalid", "План или доказательство резервной копии повреждены.");
        }
        catch (JsonException) { throw new AccountingCleanupException("cleanup_json_invalid", "План или доказательство резервной копии повреждены."); }
    }
}

public sealed class AccountingCleanupHostEnvironment : IAccountingCleanupHostEnvironment
{
    public async Task<AccountingCleanupExecutionIdentity> GetIdentityAsync(string? serviceName, int? applicationPort, CancellationToken cancellationToken)
    {
        var privileged = OperatingSystem.IsWindows() ? IsWindowsAdministrator() : OperatingSystem.IsLinux() &&
            (await RunAsync("/usr/bin/id", ["-u"], cancellationToken)).Output.Trim() == "0";
        if (!privileged) return new(false, false, "unprivileged-host");
        bool stopped;
        if (applicationPort.HasValue) stopped = !IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == applicationPort.Value);
        else if (OperatingSystem.IsLinux())
        {
            var result = await RunAsync("/usr/bin/systemctl", ["show", serviceName!, "--property=LoadState", "--property=ActiveState"], cancellationToken);
            var properties = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('=', 2))
                .Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => pair[1].Trim(), StringComparer.Ordinal);
            stopped = result.ExitCode == 0 && (properties.GetValueOrDefault("LoadState") is "loaded" or "masked") &&
                (properties.GetValueOrDefault("ActiveState") is "inactive" or "failed");
        }
        else
        {
            var result = await RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"), ["query", serviceName!], cancellationToken);
            stopped = result.ExitCode == 0 && Regex.IsMatch(result.Output, ":\\s*1\\s+STOPPED", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return new(true, stopped, "host-administrator");
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string executable, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new AccountingCleanupException("cleanup_host_check_failed", "Не удалось проверить состояние узла.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            await stderr;
            return (process.ExitCode, await stdout);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }
}
