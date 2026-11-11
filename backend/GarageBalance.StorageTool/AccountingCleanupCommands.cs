using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text.Json;
using System.Text.RegularExpressions;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GarageBalance.StorageTool;

public sealed record AccountingCleanupCommandOptions(bool Execute, string? PlanPath, string? Fingerprint,
    string? BackupReceiptPath, string? ExpectedDatabase, string? ServiceName, int? ApplicationPort,
    string? Reason, int MaximumGarages, string? OutputPath, int? AnnualHoldYear = null, Guid? PreparationOperationId = null,
    string? LocalRecoveryPlanPath = null, string? LocalRecoveryFingerprint = null)
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
                "--application-port" or "--reason" or "--max-garages" or "--output" or "--annual-hold-year" or "--operation-id" or
                "--local-recovery-plan" or "--local-recovery-confirm") || index + 1 >= args.Length ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[index], args[++index])) throw Invalid();
        }
        string? Value(string key) => values.GetValueOrDefault(key);
        int Number(string key, int fallback, int maximum) => !values.TryGetValue(key, out var raw) ? fallback :
            int.TryParse(raw, out var number) && number >= 1 && number <= maximum ? number : throw Invalid();
        var result = new AccountingCleanupCommandOptions(execute, Value("--plan"), Value("--confirm"), Value("--backup-receipt"),
            Value("--expected-database"), Value("--service"), values.ContainsKey("--application-port") ? Number("--application-port", 0, 65535) : null,
            Value("--reason"), Number("--max-garages", 100, 500), Value("--output"), values.ContainsKey("--annual-hold-year") ? Number("--annual-hold-year", 0, 9999) : null,
            values.TryGetValue("--operation-id", out var operation) && Guid.TryParseExact(operation, "D", out var id) && id != Guid.Empty ? id : null,
            Value("--local-recovery-plan"), Value("--local-recovery-confirm"));
        var isAbortPreparation = args[0].Equals("selective-financial-abort-preparation", StringComparison.OrdinalIgnoreCase);
        if (values.ContainsKey("--operation-id") && (!isAbortPreparation || result.PreparationOperationId is null)) throw Invalid();
        if (result.AnnualHoldYear.HasValue && (result.AnnualHoldYear < 1900 || !args[0].Equals("selective-financial-plan", StringComparison.OrdinalIgnoreCase) && !args[0].Equals("selective-financial-stage", StringComparison.OrdinalIgnoreCase) && !args[0].Equals("annual-accrual-release-plan", StringComparison.OrdinalIgnoreCase) && !args[0].Equals("annual-accrual-impact-plan", StringComparison.OrdinalIgnoreCase))) throw Invalid();
        if (values.ContainsKey("--max-garages") && !args[0].StartsWith("archived-garages-c", StringComparison.OrdinalIgnoreCase) &&
            !args[0].Equals("archived-garages-plan", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        var isRecovery = args[0].Equals("selective-financial-recover", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("annual-accrual-release-recover", StringComparison.OrdinalIgnoreCase);
        var isAnnualImpact = args[0].Equals("annual-accrual-impact-plan", StringComparison.OrdinalIgnoreCase);
        var isAnnualPlan = args[0].Equals("annual-accrual-release-plan", StringComparison.OrdinalIgnoreCase);
        if ((isAnnualImpact || isAnnualPlan) && (result.AnnualHoldYear is null or > 9998 ||
            string.IsNullOrWhiteSpace(result.ExpectedDatabase))) throw Invalid();
        if (args[0].Equals("annual-accrual-release", StringComparison.OrdinalIgnoreCase) && !execute) throw Invalid();
        var isStage = args[0].Equals("selective-financial-stage", StringComparison.OrdinalIgnoreCase);
        if (result.LocalRecoveryPlanPath is not null || result.LocalRecoveryFingerprint is not null)
            if (!isStage || !execute || result.LocalRecoveryPlanPath is null || result.LocalRecoveryFingerprint?.Length != 64 ||
                !result.LocalRecoveryFingerprint.All(Uri.IsHexDigit)) throw Invalid();
        if (!execute && (isRecovery || isStage || isAbortPreparation)) throw Invalid();
        if (execute && isAbortPreparation)
        {
            if (result.PreparationOperationId is null || result.ExpectedDatabase != "garagebalance_staging" || result.ServiceName != StagingOfflineProof.Service ||
                values.Keys.Any(key => key is not ("--operation-id" or "--expected-database" or "--service"))) throw Invalid();
        }
        else if (execute && isStage)
        {
            if (string.IsNullOrWhiteSpace(result.OutputPath) || result.AnnualHoldYear != 2026 ||
                result.ExpectedDatabase != "garagebalance_staging" || result.ServiceName != StagingOfflineProof.Service ||
                string.IsNullOrWhiteSpace(result.Reason) || result.Reason.Trim().Length is < 3 or > 500 || result.Reason.Any(char.IsControl) ||
                values.Keys.Any(key => key is not ("--output" or "--annual-hold-year" or "--expected-database" or "--service" or "--reason" or
                    "--local-recovery-plan" or "--local-recovery-confirm"))) throw Invalid();
        }
        else if (execute && isRecovery)
        {
            if (string.IsNullOrWhiteSpace(result.PlanPath) || result.Fingerprint?.Length != 64 || !result.Fingerprint.All(Uri.IsHexDigit) ||
                result.ExpectedDatabase != "garagebalance_staging" || result.ServiceName != StagingOfflineProof.Service ||
                values.Keys.Any(key => key is not ("--plan" or "--confirm" or "--expected-database" or "--service"))) throw Invalid();
        }
        else if (execute)
        {
            if (AccountingCleanupCommands.IsPreview(args[0]) ||
                string.IsNullOrWhiteSpace(result.PlanPath) || string.IsNullOrWhiteSpace(result.BackupReceiptPath) ||
                string.IsNullOrWhiteSpace(result.Fingerprint) || result.Fingerprint.Length != 64 || !result.Fingerprint.All(Uri.IsHexDigit) ||
                string.IsNullOrWhiteSpace(result.ExpectedDatabase) || string.IsNullOrWhiteSpace(result.Reason) ||
                result.Reason.Trim().Length is < 3 or > 500 || result.Reason.Any(char.IsControl) || values.ContainsKey("--max-garages") ||
                (result.ServiceName is null) == (result.ApplicationPort is null)) throw Invalid();
        }
        else if (result.OutputPath is null || values.Keys.Any(key => key is not ("--output" or "--max-garages" or "--expected-database" or "--annual-hold-year"))) throw Invalid();
        if (result.ServiceName is not null && !Regex.IsMatch(result.ServiceName, "^[A-Za-z0-9][A-Za-z0-9_.@:-]{0,119}$")) throw Invalid();
        foreach (var path in new[] { result.PlanPath, result.BackupReceiptPath, result.OutputPath, result.LocalRecoveryPlanPath }.OfType<string>())
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
    private static readonly string[] PreviewCommands = ["archived-garages-plan", "archived-garages-purge-plan", "meter-history-plan", "archived-fund-balance-plan", "selective-financial-plan", "annual-accrual-release-plan", "annual-accrual-impact-plan"];
    private static readonly string[] ExecuteCommands = ["archived-garages-cleanup", "archived-garages-purge", "meter-history-cleanup", "archived-fund-balance-repair", "selective-financial-stage", "selective-financial-cleanup", "selective-financial-recover", "selective-financial-abort-preparation", "annual-accrual-release", "annual-accrual-release-recover"];
    public static bool CanHandle(string command) => IsPreview(command) ||
        ExecuteCommands.Contains(command, StringComparer.OrdinalIgnoreCase);
    public static bool IsPreview(string command) => PreviewCommands.Contains(command, StringComparer.OrdinalIgnoreCase);

    private static async Task<StagingBarrierQualification> RequireQualifiedStagingBarrierAsync(CancellationToken token)
    {
        try { return await StagingBarrierQualification.VerifyInstalledAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { throw new AccountingCleanupException("cleanup_service_exclusion_unverified", "Installed staging maintenance barrier is not qualified."); }
    }

    private static async Task<int> RunStagingPreparationAbortAsync(AccountingCleanupCommandOptions options, CancellationToken token)
    {
        await RequireQualifiedStagingBarrierAsync(token);
        var lifecycle = new StagingServiceLifecycle(allowFailedForRecovery: true);
        MaintenanceRecoveryState? recorded = null;
        using var barrier = new LinuxMaintenanceBarrierHost(options.PreparationOperationId!.Value, new string('0', 64),
            lifecycle.CaptureStateAsync, lifecycle.StopAndDrainAsync, lifecycle.VerifyOfflineAsync, _ => Task.CompletedTask,
            lifecycle.RestoreAsync, () => recorded!.ServiceWasActive, () => recorded!.Participants, lifecycle.RestoreParticipantsAsync);
        await StagingRecoveryRunner.AbortPreparationAsync(barrier, lifecycle, state => recorded = state, token);
        Console.WriteLine("maintenance_preparation_aborted:no_financial_sql");
        return 0;
    }

    private static async Task<int> RunStagingStageAsync(AccountingCleanupCommandOptions options, IConfiguration configuration, CancellationToken token)
    {
        var qualification = await RequireQualifiedStagingBarrierAsync(token);
        var connection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection) || new NpgsqlConnectionStringBuilder(connection).Database != "garagebalance_staging")
            throw new AccountingCleanupException("cleanup_database_mismatch", "Wrong database.");
        var operationId = Guid.NewGuid();
        var lifecycle = new StagingServiceLifecycle();
        // Preparation cannot create a commit receipt; the marker binds the ID and
        // participant state, and the final fingerprint is written in the plan.
        using var barrier = new LinuxMaintenanceBarrierHost(operationId, new string('0', 64), lifecycle.CaptureStateAsync,
            lifecycle.StopAndDrainAsync, lifecycle.VerifyOfflineAsync, _ => throw new InvalidOperationException("maintenance_preparation_cannot_commit"),
            lifecycle.RestoreAsync, () => lifecycle.ServiceWasActive, () => lifecycle.ParticipantState);
        var outputCreated = false;
        var outputComplete = false;
        StagingBackupPreparationResult? backupPreparation = null;
        try
        {
            await using var output = OpenPrivateOutput(options.OutputPath!);
            outputCreated = true;
            await using var db = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().UseNpgsql(connection).Options);
            var engine = new SelectiveFinancialCleanupEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System);
            var plan = await StagingMaintenancePreparation.PrepareAsync(barrier, operationId,
                async (id, ct) =>
                {
                    var permit = await StagingPreparationPermit.CreateAsync(barrier, id, ct);
                    backupPreparation = await StagingBackupPreparation.VerifyAndRestoreAsync(db, configuration, permit,
                        options.LocalRecoveryPlanPath, options.LocalRecoveryFingerprint, ct);
                    return await engine.PreviewAsync(ct, 2026, id);
                }, async (prepared, ct) =>
                {
                    await JsonSerializer.SerializeAsync(output, prepared, JsonOptions, ct);
                    await output.FlushAsync(ct);
                    output.Flush(flushToDisk: true);
                    outputComplete = true;
                }, token, async ct =>
                {
                    await qualification.RevalidateAsync(ct);
                    await barrier.TransitionPreparationAsync("preparing", null, ct);
                }, async (prepared, ct) =>
                {
                    await barrier.SyncPreparedPlanDirectoryAsync(options.OutputPath!, ct);
                    await barrier.TransitionPreparationAsync("prepared", prepared.Fingerprint, ct);
                });
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                plan.PlanId,
                plan.Fingerprint,
                applicationOffline = true,
                removedRows = 0,
                backupPreparation,
                counts = plan.Tables.Select(table => new { table.Table, count = table.Rows.Count })
            }, JsonOptions));
            return 0;
        }
        finally
        {
            if (outputCreated && !outputComplete && File.Exists(options.OutputPath)) File.Delete(options.OutputPath);
        }
    }

    private static async Task<int> RunStagingRecoveryAsync(AccountingCleanupCommandOptions options, IConfiguration configuration, CancellationToken token)
    {
        await RequireQualifiedStagingBarrierAsync(token);
        var connection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection) || new NpgsqlConnectionStringBuilder(connection).Database != "garagebalance_staging")
            throw new AccountingCleanupException("cleanup_database_mismatch", "Wrong database.");
        var plan = await ReadStagingAsync<SelectiveFinancialCleanupPlan>(options.PlanPath!, token);
        if (plan.HeldAnnualYear != 2026 || plan.Fingerprint != options.Fingerprint || plan.Fingerprint != SelectiveFinancialCleanupEngine.Fingerprint(plan))
            throw new AccountingCleanupException("cleanup_plan_invalid", "Recovery requires the exact approved plan and 2026 hold.");
        var lifecycle = new StagingServiceLifecycle(allowFailedForRecovery: true);
        var recordedActive = false;
        MaintenanceParticipantState? recordedParticipants = null;
        using var barrier = new LinuxMaintenanceBarrierHost(plan.PlanId, plan.Fingerprint, lifecycle.CaptureStateAsync, lifecycle.StopAndDrainAsync,
            lifecycle.VerifyOfflineAsync, _ => Task.CompletedTask, lifecycle.RestoreAsync, () => recordedActive, () => recordedParticipants, lifecycle.RestoreParticipantsAsync);
        await using var db = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().UseNpgsql(connection).Options);
        var outcome = await StagingRecoveryRunner.ResumeAsync(barrier, lifecycle, db, plan, state =>
        {
            recordedActive = state.ServiceWasActive;
            recordedParticipants = state.Participants;
        }, token);
        Console.WriteLine("maintenance_recovery_complete:" + outcome);
        return 0;
    }

    private static async Task<int> RunStagingSelectiveAsync(AccountingCleanupCommandOptions options, IConfiguration configuration, CancellationToken token)
    {
        var qualification = await RequireQualifiedStagingBarrierAsync(token);
        var connection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection) || new NpgsqlConnectionStringBuilder(connection).Database != "garagebalance_staging")
            throw new AccountingCleanupException("cleanup_database_mismatch", "Wrong database.");
        var plan = await ReadStagingAsync<SelectiveFinancialCleanupPlan>(options.PlanPath!, token);
        if (plan.HeldAnnualYear != 2026) throw new AccountingCleanupException("cleanup_annual_year_invalid", "The 2026 annual hold is required.");
        var backup = await ReadStagingAsync<AccountingCleanupBackupReceipt>(options.BackupReceiptPath!, token);
        var lifecycle = new StagingServiceLifecycle();
        SelectiveFinancialCleanupResult? result = null;
        MaintenanceRecoveryState? recorded = null;
        StagingExecutionPermit? permit = null;
        using var barrier = new LinuxMaintenanceBarrierHost(plan.PlanId, plan.Fingerprint, lifecycle.CaptureStateAsync, lifecycle.StopAndDrainAsync, lifecycle.VerifyOfflineAsync,
            async ct =>
            {
                await using var verification = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().UseNpgsql(connection).Options);
                var planId = plan.PlanId.ToString();
                if (await verification.AuditEvents.AsNoTracking().CountAsync(e => e.Action == "finance.selective_history_cleared" && e.EntityId == planId, ct) != 1)
                    throw new InvalidOperationException("maintenance_commit_unverified");
                var held = await verification.ApplicationSettings.AsNoTracking().CountAsync(s =>
                    s.Key == "finance.annual_accrual_hold.2026" && s.BooleanValue && s.IntegerValue == 2026, ct);
                if (held != 1) throw new InvalidOperationException("maintenance_annual_hold_unverified");
                var verifier = new SelectiveFinancialCleanupEngine(verification, new AccountingCleanupBackupVerifier(), TimeProvider.System);
                await verifier.VerifyPostCommitAsync(plan, ct);
            }, ct => lifecycle.RestoreRecordedAsync(recorded!.ServiceWasActive, ct, recorded.Participants),
            () => recorded!.ServiceWasActive, () => recorded!.Participants, lifecycle.RestoreParticipantsAsync);
        await MaintenanceBarrierCoordinator.ExecutePreparedAsync(barrier, async ct =>
        {
            await qualification.RevalidateAsync(ct);
            var progress = barrier.ReadPreparationProgress();
            if (progress is not { Phase: "prepared" } || progress.Fingerprint != plan.Fingerprint)
                throw new InvalidOperationException("maintenance_prepared_execution_required");
            var files = barrier.ReadRecoveryFiles();
            recorded = MaintenanceRecoveryInspection.Inspect(files.Marker, files.Receipt, 0, files.ParticipantsPermitted);
            if (recorded.PlanId != plan.PlanId || files.Marker is null || recorded.ReceiptPresent || recorded.Participants is null)
                throw new InvalidOperationException("maintenance_prepared_execution_required");
            barrier.AdoptRecoveryFiles(recorded);
            await lifecycle.CaptureStateAsync(ct);
        }, async ct =>
        {
            await barrier.TransitionPreparationAsync("executing", plan.Fingerprint, ct);
            permit = await StagingExecutionPermit.CreateAsync(barrier, plan.PlanId, plan.Fingerprint, ct);
        }, async ct =>
        {
            var identity = await new StagingOfflineProof().GetIdentityAsync(StagingOfflineProof.Service, null, ct);
            await using var db = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().UseNpgsql(connection).Options);
            await EnsureNoOtherClientsAsync(db, ct);
            var engine = new SelectiveFinancialCleanupEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System,
                confirmStopped: async inner => (await new StagingOfflineProof().GetIdentityAsync(StagingOfflineProof.Service, null, inner)).ApplicationStopped,
                stagingPermit: permit ?? throw new InvalidOperationException("maintenance_execution_authority_missing"));
            result = await engine.ExecuteAsync(plan, options.Fingerprint!, backup, identity, options.Reason!, ct);
            if (result.MustRemainOffline) throw new InvalidOperationException("maintenance_offline_required");
        }, token);
        Console.WriteLine(JsonSerializer.Serialize(result ?? throw new InvalidOperationException("maintenance_result_missing"), JsonOptions));
        return 0;
    }

    private static async Task<int> RunStagingAnnualAsync(AccountingCleanupCommandOptions options,
        IConfiguration configuration, bool recover, CancellationToken token)
    {
        var connection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection) || new NpgsqlConnectionStringBuilder(connection).Database != "garagebalance_staging")
            throw new AccountingCleanupException("cleanup_database_mismatch", "Wrong database.");
        var plan = await ReadStagingAsync<AnnualAccrualHoldReleasePlan>(options.PlanPath!, token);
        AnnualAccrualHoldReleaseEngine.ValidatePlan(plan);
        if (plan.Fingerprint != options.Fingerprint)
            throw new AccountingCleanupException("cleanup_plan_invalid", "The exact approved annual release plan is required.");
        if (recover)
        {
            var outcome = await StagingAnnualAccrualReleaseRunner.RecoverAsync(connection, plan, token);
            Console.WriteLine(JsonSerializer.Serialize(new { plan.PlanId, plan.Year, recoveryOutcome = outcome, repeatedRelease = false }, JsonOptions));
        }
        else
        {
            var backup = await ReadStagingAsync<AccountingCleanupBackupReceipt>(options.BackupReceiptPath!, token);
            await StagingAnnualAccrualReleaseRunner.ExecuteAsync(connection, plan, options.Fingerprint!, backup,
                options.Reason!, CreateBusinessDate(configuration), token);
            Console.WriteLine(JsonSerializer.Serialize(new { plan.PlanId, plan.Year, released = true, generatedByRelease = 0, resumedAutomationMayCreateAccruals = true }, JsonOptions));
        }
        return 0;
    }

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
            var normalizedCommand = args[0].ToLowerInvariant();
            var strictStaging = options.Execute && normalizedCommand is "selective-financial-stage" or "selective-financial-cleanup" or "selective-financial-recover" or "selective-financial-abort-preparation" or "annual-accrual-release" or "annual-accrual-release-recover";
            if (strictStaging && (options.ServiceName != StagingOfflineProof.Service || options.ApplicationPort.HasValue || options.ExpectedDatabase != "garagebalance_staging"))
                throw new AccountingCleanupException("cleanup_staging_target_invalid", "Укажите точный сервис GarageBalance staging; произвольный порт не допускается.");
            if (strictStaging && normalizedCommand == "selective-financial-cleanup")
                return await RunStagingSelectiveAsync(options, configuration, cancellationToken);
            if (strictStaging && normalizedCommand == "selective-financial-abort-preparation")
                return await RunStagingPreparationAbortAsync(options, cancellationToken);
            if (strictStaging && normalizedCommand == "selective-financial-stage")
                return await RunStagingStageAsync(options, configuration, cancellationToken);
            if (strictStaging && normalizedCommand == "selective-financial-recover")
                return await RunStagingRecoveryAsync(options, configuration, cancellationToken);
            if (strictStaging && normalizedCommand is "annual-accrual-release" or "annual-accrual-release-recover")
                return await RunStagingAnnualAsync(options, configuration, normalizedCommand == "annual-accrual-release-recover", cancellationToken);
            IAccountingCleanupHostEnvironment host = hostEnvironment ?? (strictStaging ? new StagingOfflineProof() : new AccountingCleanupHostEnvironment());
            var identity = options.Execute
                ? await host.GetIdentityAsync(options.ServiceName, options.ApplicationPort, cancellationToken)
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
            var command = normalizedCommand;
            if (command == "annual-accrual-impact-plan")
            {
                var forecast = await new AnnualAccrualForecastEngine(db, CreateBusinessDate(configuration), TimeProvider.System)
                    .PreviewAsync(options.AnnualHoldYear!.Value, cancellationToken);
                await JsonSerializer.SerializeAsync(output!, forecast, JsonOptions, cancellationToken);
                await output!.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
                reportComplete = true;
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    dryRun = true,
                    forecast.Year,
                    forecast.BusinessDate,
                    forecast.IsComplete,
                    forecast.AffectedGarageCount,
                    forecast.NewAccrualCount,
                    forecast.NewAccrualAmount,
                    forecast.InputFingerprint,
                    privateReportWritten = true
                }, JsonOptions));
                return forecast.IsComplete ? 0 : 3;
            }
            if (command == "annual-accrual-release-plan")
            {
                var releaseEngine = new AnnualAccrualHoldReleaseEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System,
                    businessDate: CreateBusinessDate(configuration));
                var report = await releaseEngine.PreviewAsync(options.AnnualHoldYear!.Value, cancellationToken);
                await JsonSerializer.SerializeAsync(output!, report, JsonOptions, cancellationToken);
                await output!.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
                reportComplete = true;
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    dryRun = true,
                    report.PlanId,
                    report.Year,
                    report.Fingerprint,
                    privateReportWritten = true
                }, JsonOptions));
                return 0;
            }
            if (command is "selective-financial-plan" or "selective-financial-cleanup")
            {
                if (options.Execute && command != "selective-financial-cleanup")
                    throw new AccountingCleanupException("cleanup_options_invalid", "Исполнение требует отдельной команды очистки.");
                var selective = new SelectiveFinancialCleanupEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System,
                    confirmStopped: options.Execute ? async ct => (await host.GetIdentityAsync(options.ServiceName, options.ApplicationPort, ct)).ApplicationStopped : null);
                object report;
                if (options.Execute)
                {
                    await EnsureNoOtherClientsAsync(db, cancellationToken);
                    report = await selective.ExecuteAsync(await ReadAsync<SelectiveFinancialCleanupPlan>(options.PlanPath!, cancellationToken),
                        options.Fingerprint!, await ReadAsync<AccountingCleanupBackupReceipt>(options.BackupReceiptPath!, cancellationToken),
                        identity, options.Reason!, cancellationToken);
                    committed = true;
                }
                else report = await selective.PreviewAsync(cancellationToken, options.AnnualHoldYear);
                if (output is not null)
                {
                    await JsonSerializer.SerializeAsync(output, report, report.GetType(), JsonOptions, CancellationToken.None);
                    await output.FlushAsync(CancellationToken.None);
                    output.Flush(flushToDisk: true);
                    reportComplete = true;
                }
                if (report is SelectiveFinancialCleanupPlan plan)
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        dryRun = true,
                        plan.PlanId,
                        plan.Fingerprint,
                        counts = plan.Tables.Select(table => new { table.Table, count = table.Rows.Count }),
                        plan.HeldAnnualYear,
                        mustRemainOfflineAfterExecution = !plan.HeldAnnualYear.HasValue
                    }, JsonOptions));
                else Console.WriteLine(JsonSerializer.Serialize(report, report.GetType(), JsonOptions));
                return 0;
            }
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
            Console.Error.WriteLine(IsAnnualReleaseExecution(args.FirstOrDefault())
                ? "annual_release_interrupted: Проверьте сохранённое состояние через annual-accrual-release-recover с тем же планом; не повторяйте release."
                : "Очистка прервана. Проверьте аудит и новый предпросмотр перед повторным запуском.");
            return 130;
        }
        catch (AccountingCleanupException error)
        {
            Console.Error.WriteLine($"{error.Code}: {error.Message}");
            if (IsAnnualReleaseExecution(args.FirstOrDefault()))
                Console.Error.WriteLine("annual_release_recovery_required_if_started: Если обслуживание уже началось, используйте annual-accrual-release-recover с тем же планом; не повторяйте release и не удаляйте maintenance marker.");
            return 2;
        }
        catch (Exception)
        {
            Console.Error.WriteLine(IsAnnualReleaseExecution(args.FirstOrDefault())
                ? "annual_release_outcome_unavailable: Исход не подтверждён. Используйте annual-accrual-release-recover с тем же планом; не повторяйте release и не удаляйте maintenance marker."
                : committed
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

    private static bool IsAnnualReleaseExecution(string? command) =>
        string.Equals(command, "annual-accrual-release", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(command, "annual-accrual-release-recover", StringComparison.OrdinalIgnoreCase);

    private static IBusinessDateProvider CreateBusinessDate(IConfiguration configuration) => new BusinessDateProvider(
        TimeProvider.System, Options.Create(configuration.GetSection(RegularAccrualAutomationOptions.SectionName)
            .Get<RegularAccrualAutomationOptions>() ?? new RegularAccrualAutomationOptions()));

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

    private static async Task<T> ReadStagingAsync<T>(string path, CancellationToken token)
    {
        using var handle = LinuxMaintenanceBarrierHost.OpenVerifiedRootFile(path);
        if (RandomAccess.GetLength(handle) > 2 * 1024 * 1024) throw new AccountingCleanupException("cleanup_json_invalid", "Private JSON exceeds its size limit.");
        await using var input = new FileStream(handle, FileAccess.Read);
        return await JsonSerializer.DeserializeAsync<T>(input, JsonOptions, token)
            ?? throw new AccountingCleanupException("cleanup_json_invalid", "Private JSON is invalid.");
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

    internal static async Task<(int ExitCode, string Output)> RunAsync(string executable, string[] arguments, CancellationToken cancellationToken)
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
