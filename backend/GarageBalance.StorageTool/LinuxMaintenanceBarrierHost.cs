using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GarageBalance.StorageTool;

// Not enabled for staging execution until real Linux/integration qualification.
// Each gate is held by this process, not inferred from a lock held by a parent.
public sealed class LinuxMaintenanceBarrierHost : IMaintenanceBarrierHost, IDisposable
{
    private const string DirectoryPath = "/var/lib/garagebalance-maintenance";
    private const string Marker = "maintenance-active";
    private const string RecoveryReceipt = "recovery-required";
    private const string ParticipantsReceipt = "participants-resume-required";
    private const string PreparationProgress = "operation-progress";
    private const int NoFollow = 0x20000, CloseOnExec = 0x80000, Directory = 0x10000;
    private const int Create = 0x40, Exclusive = 0x80, WriteOnly = 1, AtEmptyPath = 0x1000;
    private const int LockExclusive = 2, LockNonBlocking = 4;
    private readonly SafeFileHandle directory;
    private readonly string directoryPath;
    private readonly Func<CancellationToken, Task> verifyOffline;
    private readonly Func<CancellationToken, Task> stopAndDrain;
    private readonly Func<CancellationToken, Task> captureServiceState;
    private readonly Func<CancellationToken, Task> restoreService;
    private readonly Func<CancellationToken, Task> restoreParticipants;
    private readonly Func<CancellationToken, Task> verifyCommitted;
    private readonly Guid planId;
    private readonly string planFingerprint;
    private readonly Func<bool> serviceWasActive;
    private readonly Func<MaintenanceParticipantState?>? participantState;
    private string? participantEncoding;
    private bool markerCreated;
    private bool receiptCreated;
    private bool participantsPermitted;
    private bool deployHeld;
    private bool activityHeld;
    private (long Inode, int DeviceMajor, int DeviceMinor)? deployIdentity;
    private (long Inode, int DeviceMajor, int DeviceMinor)? activityIdentity;

    public LinuxMaintenanceBarrierHost(Guid planId, string planFingerprint, Func<CancellationToken, Task> captureServiceState, Func<CancellationToken, Task> stopAndDrain, Func<CancellationToken, Task> verifyOffline, Func<CancellationToken, Task> verifyCommitted, Func<CancellationToken, Task> restoreService, Func<bool> serviceWasActive, Func<MaintenanceParticipantState?>? participantState = null, Func<CancellationToken, Task>? restoreParticipants = null)
        : this(DirectoryPath, planId, planFingerprint, captureServiceState, stopAndDrain, verifyOffline, verifyCommitted, restoreService, serviceWasActive, participantState, restoreParticipants)
    {
    }

#if MAINTENANCE_FIXTURE
    internal Action<bool>? FixtureActivityAttempt { get; set; }

    internal static LinuxMaintenanceBarrierHost CreateFixture(string fixtureDirectory, Guid planId, string planFingerprint,
        Func<CancellationToken, Task> captureServiceState, Func<CancellationToken, Task> stopAndDrain,
        Func<CancellationToken, Task> verifyOffline, Func<CancellationToken, Task> verifyCommitted,
        Func<CancellationToken, Task> restoreService, Func<bool> serviceWasActive, Func<MaintenanceParticipantState?>? participantState = null, Func<CancellationToken, Task>? restoreParticipants = null)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(fixtureDirectory, @"\A/run/gb-barrier-dotnet\.[A-Za-z0-9]{8}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidOperationException("maintenance_fixture_path_invalid");
        return new LinuxMaintenanceBarrierHost(fixtureDirectory, planId, planFingerprint, captureServiceState, stopAndDrain,
            verifyOffline, verifyCommitted, restoreService, serviceWasActive, participantState, restoreParticipants);
    }
#endif

    private LinuxMaintenanceBarrierHost(string directoryPath, Guid planId, string planFingerprint, Func<CancellationToken, Task> captureServiceState, Func<CancellationToken, Task> stopAndDrain, Func<CancellationToken, Task> verifyOffline, Func<CancellationToken, Task> verifyCommitted, Func<CancellationToken, Task> restoreService, Func<bool> serviceWasActive, Func<MaintenanceParticipantState?>? participantState, Func<CancellationToken, Task>? restoreParticipants)
    {
        ArgumentNullException.ThrowIfNull(verifyOffline);
        ArgumentNullException.ThrowIfNull(stopAndDrain);
        ArgumentNullException.ThrowIfNull(captureServiceState);
        ArgumentNullException.ThrowIfNull(restoreService);
        ArgumentNullException.ThrowIfNull(verifyCommitted);
        ArgumentNullException.ThrowIfNull(serviceWasActive);
        if (planId == Guid.Empty) throw new ArgumentException("A plan ID is required", nameof(planId));
        if (planFingerprint is null || planFingerprint.Length != 64 || !planFingerprint.All(Uri.IsHexDigit)) throw new ArgumentException("A plan fingerprint is required", nameof(planFingerprint));
        if (!OperatingSystem.IsLinux() || geteuid() != 0) throw new InvalidOperationException("maintenance_host_unavailable");
        this.verifyOffline = verifyOffline;
        this.directoryPath = directoryPath;
        this.stopAndDrain = stopAndDrain;
        this.captureServiceState = captureServiceState;
        this.restoreService = restoreService;
        this.restoreParticipants = restoreParticipants ?? (_ => Task.CompletedTask);
        this.verifyCommitted = verifyCommitted;
        this.planId = planId;
        this.planFingerprint = planFingerprint;
        this.serviceWasActive = serviceWasActive;
        this.participantState = participantState;
        directory = OpenHandle(open(directoryPath, NoFollow | CloseOnExec | Directory, 0));
        try { Validate(directory, directoryExpected: true); }
        catch { directory.Dispose(); throw; }
    }

    public ValueTask<IAsyncDisposable> AcquireDeployGateAsync(CancellationToken cancellationToken) => Acquire("deploy.lock", cancellationToken);
    public ValueTask<IAsyncDisposable> AcquireActivityGateAsync(CancellationToken cancellationToken) => Acquire("activity.lock", cancellationToken);

    private async ValueTask<IAsyncDisposable> Acquire(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handle = OpenHandle(openat(Fd(directory), name, NoFollow | CloseOnExec, 0));
        try
        {
            var identity = Validate(handle, directoryExpected: false);
            var attempts = name == "activity.lock" ? 61 : 1;
            var acquired = false;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lockResult = flock(Fd(handle), LockExclusive | LockNonBlocking);
                var lockError = Marshal.GetLastPInvokeError();
#if MAINTENANCE_FIXTURE
                if (name == "activity.lock" && attempt == 0) FixtureActivityAttempt?.Invoke(lockResult == 0);
#endif
                if (lockResult == 0) { acquired = true; break; }
                if (lockError is not (11 or 35)) throw new InvalidOperationException("maintenance_gate_failed");
                if (attempt + 1 < attempts) await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            if (!acquired) throw new InvalidOperationException("maintenance_gate_busy");
            using var current = OpenHandle(openat(Fd(directory), name, NoFollow | CloseOnExec, 0));
            if (identity != Validate(current, directoryExpected: false)) throw new InvalidOperationException("maintenance_gate_replaced");
            if (name == "deploy.lock") { deployHeld = true; deployIdentity = identity; }
            else { activityHeld = true; activityIdentity = identity; }
            return new Gate(handle, () =>
            {
                if (name == "deploy.lock") { deployHeld = false; deployIdentity = null; }
                else { activityHeld = false; activityIdentity = null; }
            });
        }
        catch { handle.Dispose(); throw; }
    }

    public Task CreateDurableMarkerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var handle = OpenHandle(openat(Fd(directory), Marker, WriteOnly | Create | Exclusive | NoFollow | CloseOnExec, 0x180));
        // Existence itself is the fail-closed signal, including a partially written marker.
        markerCreated = true;
        participantEncoding ??= participantState?.Invoke()?.Encode();
        RandomAccess.Write(handle, Encoding.ASCII.GetBytes(MarkerContents()), 0);
        if (fsync(Fd(handle)) != 0 || fsync(Fd(directory)) != 0) throw new InvalidOperationException("maintenance_marker_sync_failed");
        return Task.CompletedTask;
    }

    public Task CaptureServiceStateAsync(CancellationToken cancellationToken) => captureServiceState(cancellationToken);
    public Task StopAndDrainAsync(CancellationToken cancellationToken) => stopAndDrain(cancellationToken);
    public Task VerifyOfflineAsync(CancellationToken cancellationToken) => verifyOffline(cancellationToken);
    public Task VerifyCommittedAsync(CancellationToken cancellationToken) => verifyCommitted(cancellationToken);
    public Task RestoreServiceAsync(CancellationToken cancellationToken) => restoreService(cancellationToken);
    public Task RestoreParticipantsAsync(CancellationToken cancellationToken) => restoreParticipants(cancellationToken);

    internal void VerifyHeldProtocol(Guid expectedId, string expectedFingerprint)
        => VerifyHeldProtocol(expectedId, expectedFingerprint, "executing");

    internal void VerifyHeldPreparation(Guid expectedId)
        => VerifyHeldProtocol(expectedId, new string('0', 64), "preparing");

    private void VerifyHeldProtocol(Guid expectedId, string expectedFingerprint, string phase)
    {
        if (!deployHeld || !activityHeld || !markerCreated || receiptCreated || planId != expectedId || planFingerprint != expectedFingerprint)
            throw new InvalidOperationException("maintenance_execution_authority_missing");
        using var currentDirectory = OpenHandle(open(directoryPath, NoFollow | CloseOnExec | Directory, 0));
        if (Validate(currentDirectory, true) != Validate(directory, true)) throw new InvalidOperationException("maintenance_directory_replaced");
        foreach (var gate in new[] { (Name: "deploy.lock", Identity: deployIdentity), (Name: "activity.lock", Identity: activityIdentity) })
        {
            using var current = OpenHandle(openat(Fd(directory), gate.Name, NoFollow | CloseOnExec, 0));
            if (gate.Identity is null || Validate(current, false) != gate.Identity.Value)
                throw new InvalidOperationException("maintenance_gate_replaced");
        }
        var files = ReadRecoveryFiles();
        if (files.Marker != MarkerContents() || files.Receipt is not null || participantEncoding is null)
            throw new InvalidOperationException("maintenance_execution_marker_mismatch");
        var progress = ReadPreparationProgress();
        if (progress is null || progress.Phase != phase ||
            (phase == "preparing" ? progress.Fingerprint is not null : progress.Fingerprint != expectedFingerprint))
            throw new InvalidOperationException("maintenance_execution_progress_mismatch");
    }

    internal void VerifyProductionExecutionAuthority(Guid expectedId, string expectedFingerprint)
    {
        if (directoryPath != DirectoryPath) throw new InvalidOperationException("maintenance_production_directory_required");
        VerifyHeldProtocol(expectedId, expectedFingerprint);
    }

    internal void VerifyProductionPreparationAuthority(Guid expectedId)
    {
        if (directoryPath != DirectoryPath) throw new InvalidOperationException("maintenance_production_directory_required");
        VerifyHeldPreparation(expectedId);
    }

    public MaintenancePreparationProgress? ReadPreparationProgress()
    {
        if (!deployHeld) throw new InvalidOperationException("maintenance_deploy_gate_required");
        var contents = ReadSmallFile(PreparationProgress);
        var value = contents is null ? null : MaintenancePreparationProgress.Parse(contents);
        if (value is not null && value.PlanId != planId) throw new InvalidOperationException("maintenance_progress_plan_mismatch");
        return value;
    }

    public Task RequireFreshAnnualReleaseAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!deployHeld) throw new InvalidOperationException("maintenance_deploy_gate_required");
        var files = ReadRecoveryFiles();
        if (files.Marker is not null || files.Receipt is not null || ReadPreparationProgress() is not null)
            throw new InvalidOperationException("maintenance_recovery_required");
        return Task.CompletedTask;
    }

    public Task SyncPreparedPlanDirectoryAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!deployHeld || !activityHeld || !markerCreated) throw new InvalidOperationException("maintenance_progress_gates_required");
        using var parent = OpenHandle(open(Path.GetDirectoryName(Path.GetFullPath(path))!, NoFollow | CloseOnExec | Directory, 0));
        Validate(parent, directoryExpected: true);
        if (fsync(Fd(parent)) != 0) throw new InvalidOperationException("maintenance_plan_sync_failed");
        return Task.CompletedTask;
    }

    public Task TransitionPreparationAsync(string phase, string? fingerprint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!deployHeld || !markerCreated || (phase != "preparing" && !activityHeld))
            throw new InvalidOperationException("maintenance_progress_gates_required");
        var previous = ReadPreparationProgress();
        var next = new MaintenancePreparationProgress(planId, phase, fingerprint);
        MaintenancePreparationProgress.ValidateTransition(previous, next);
        if (phase == "executing" && fingerprint != planFingerprint) throw new InvalidOperationException("maintenance_progress_plan_mismatch");
        var temporary = "progress-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var handle = OpenHandle(openat(Fd(directory), temporary, WriteOnly | Create | Exclusive | NoFollow | CloseOnExec, 0x180)))
            {
                RandomAccess.Write(handle, Encoding.ASCII.GetBytes(next.Encode()), 0);
                if (fsync(Fd(handle)) != 0) throw new InvalidOperationException("maintenance_progress_sync_failed");
            }
            if (renameat2(Fd(directory), temporary, Fd(directory), PreparationProgress, previous is null ? 1U : 0U) != 0 || fsync(Fd(directory)) != 0)
                throw new InvalidOperationException("maintenance_progress_sync_failed");
        }
        finally { unlinkat(Fd(directory), temporary, 0); }
        return Task.CompletedTask;
    }

    // Only fixed filenames in the already verified root-owned directory are read.
    // Parsing an incomplete marker fails closed; no recovery action is implied.
    public (string? Marker, string? Receipt, bool ParticipantsPermitted) ReadRecoveryFiles()
    {
        var required = ReadSmallFile(RecoveryReceipt);
        var permitted = ReadSmallFile(ParticipantsReceipt);
        if (required is not null && permitted is not null) throw new InvalidOperationException("maintenance_recovery_state_invalid");
        return (ReadSmallFile(Marker), required ?? permitted, permitted is not null);
    }

    // A corrupt receipt must not leave application starts enabled merely because a
    // previous owner removed the marker before crashing. This deliberately incomplete
    // participant snapshot cannot issue authority or permit automatic restoration.
    // It blocks starts until the protected operator evidence is repaired/reviewed.
    public Task PreserveUnresolvedRecoveryMarkerAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!deployHeld) throw new InvalidOperationException("maintenance_deploy_gate_required");
        if (ReadSmallFile(Marker) is not null) return Task.CompletedTask;
        using var handle = OpenHandle(openat(Fd(directory), Marker, WriteOnly | Create | Exclusive | NoFollow | CloseOnExec, 0x180));
        RandomAccess.Write(handle, Encoding.ASCII.GetBytes($"{planId:D}\ninactive\n"), 0);
        if (fsync(Fd(handle)) != 0 || fsync(Fd(directory)) != 0) throw new InvalidOperationException("maintenance_marker_sync_failed");
        markerCreated = true;
        return Task.CompletedTask;
    }

    // Caller must hold both relevant gates before using the adopted state for writes.
    public void AdoptRecoveryFiles(MaintenanceRecoveryState expected)
    {
        var files = ReadRecoveryFiles();
        var current = MaintenanceRecoveryInspection.Inspect(files.Marker, files.Receipt, expected.CommitVerified ? 1 : 0, files.ParticipantsPermitted);
        if (current.PlanId != planId || current.PlanId != expected.PlanId || current.ServiceWasActive != expected.ServiceWasActive ||
            current.ReceiptPresent != expected.ReceiptPresent || current.Participants?.Encode() != expected.Participants?.Encode() ||
            current.ParticipantsPermitted != expected.ParticipantsPermitted ||
            current.ReceiptOutcome != expected.ReceiptOutcome || current.RollbackVerified != expected.RollbackVerified ||
            (current.ReceiptPresent && current.ReceiptFingerprint != planFingerprint))
            throw new InvalidOperationException("maintenance_recovery_state_mismatch");
        markerCreated = files.Marker is not null;
        receiptCreated = files.Receipt is not null;
        participantEncoding = current.Participants?.Encode();
        participantsPermitted = current.ParticipantsPermitted;
    }

    private string? ReadSmallFile(string name)
    {
        var fd = openat(Fd(directory), name, NoFollow | CloseOnExec, 0);
        if (fd < 0)
        {
            if (Marshal.GetLastPInvokeError() == 2) return null;
            throw new InvalidOperationException("maintenance_recovery_read_failed");
        }
        using var handle = OpenHandle(fd);
        Validate(handle, directoryExpected: false);
        var length = RandomAccess.GetLength(handle);
        if (length is <= 0 or > 4096) throw new InvalidOperationException("maintenance_recovery_state_invalid");
        var buffer = new byte[(int)length];
        if (RandomAccess.Read(handle, buffer, 0) != buffer.Length) throw new InvalidOperationException("maintenance_recovery_read_failed");
        return new UTF8Encoding(false, true).GetString(buffer);
    }

    public Task CreateRecoveryReceiptAsync(CancellationToken cancellationToken) => CreateReceiptAsync("committed", cancellationToken);
    public Task CreateRollbackRecoveryReceiptAsync(CancellationToken cancellationToken) => CreateReceiptAsync("rolled-back", cancellationToken);

    private Task CreateReceiptAsync(string outcome, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!markerCreated) throw new InvalidOperationException("maintenance_marker_not_owned");
        using var handle = OpenHandle(openat(Fd(directory), RecoveryReceipt, WriteOnly | Create | Exclusive | NoFollow | CloseOnExec, 0x180));
        var participants = participantEncoding is null ? "" : participantEncoding + "\noutcome=" + outcome + "\n";
        RandomAccess.Write(handle, Encoding.ASCII.GetBytes($"{planId:D}\n{(serviceWasActive() ? "active" : "inactive")}\n{planFingerprint}\n{participants}"), 0);
        if (fsync(Fd(handle)) != 0 || fsync(Fd(directory)) != 0) throw new InvalidOperationException("maintenance_receipt_sync_failed");
        receiptCreated = true;
        return Task.CompletedTask;
    }

    public Task RemoveDurableMarkerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!markerCreated || !receiptCreated) throw new InvalidOperationException("maintenance_receipt_not_durable");
        if (unlinkat(Fd(directory), Marker, 0) != 0) throw new InvalidOperationException("maintenance_marker_remove_failed");
        markerCreated = false;
        if (fsync(Fd(directory)) != 0)
        {
            // Best effort only: a failed filesystem cannot provide a durable guarantee.
            // Staging remains blocked pending a qualified recovery protocol.
            var restored = openat(Fd(directory), Marker, WriteOnly | Create | Exclusive | NoFollow | CloseOnExec, 0x180);
            if (restored >= 0)
            {
                using var replacement = OpenHandle(restored);
                markerCreated = true;
                RandomAccess.Write(replacement, Encoding.ASCII.GetBytes(MarkerContents()), 0);
                fsync(Fd(replacement));
                fsync(Fd(directory));
            }
            throw new InvalidOperationException("maintenance_marker_sync_failed");
        }
        return Task.CompletedTask;
    }

    public Task CompleteRecoveryAsync(CancellationToken cancellationToken) => CompleteRecoveryCoreAsync(false, cancellationToken);

    public Task CompleteAnnualReleaseRecoveryAsync(CancellationToken cancellationToken) => CompleteRecoveryCoreAsync(true, cancellationToken);

    private Task CompleteRecoveryCoreAsync(bool annualRelease, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!receiptCreated || markerCreated || !participantsPermitted) throw new InvalidOperationException("maintenance_recovery_invalid");
        var progress = ReadPreparationProgress();
        var preparationOnly = progress is { Phase: "preparing", Fingerprint: null } && (planFingerprint.All(c => c == '0') || annualRelease) &&
            MaintenanceRecoveryInspection.Inspect(null, ReadRecoveryFiles().Receipt, 0, participantsPermitted) is
            { RollbackVerified: true } rollback && rollback.ReceiptFingerprint == planFingerprint;
        if (progress is not null && progress.Fingerprint != planFingerprint && !preparationOnly) throw new InvalidOperationException("maintenance_progress_plan_mismatch");
        if (progress is not null && unlinkat(Fd(directory), PreparationProgress, 0) != 0)
            throw new InvalidOperationException("maintenance_progress_remove_failed");
        if (unlinkat(Fd(directory), ParticipantsReceipt, 0) != 0) throw new InvalidOperationException("maintenance_receipt_remove_failed");
        receiptCreated = false;
        // At this point commit, service restoration and health are verified; if fsync
        // fails, the receipt's persistence is uncertain but deletion is never retried.
        if (fsync(Fd(directory)) != 0) throw new InvalidOperationException("maintenance_receipt_sync_failed");
        return Task.CompletedTask;
    }

    public Task PermitParticipantsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!receiptCreated || markerCreated) throw new InvalidOperationException("maintenance_recovery_invalid");
        if (participantsPermitted) return Task.CompletedTask;
        // Preserve the durable participant snapshot while normal storage/health
        // starts become allowed. Deploy checks both receipt names.
        if (renameat2(Fd(directory), RecoveryReceipt, Fd(directory), ParticipantsReceipt, 1) != 0)
            throw new InvalidOperationException("maintenance_participant_permit_failed");
        participantsPermitted = true;
        if (fsync(Fd(directory)) != 0) throw new InvalidOperationException("maintenance_receipt_sync_failed");
        return Task.CompletedTask;
    }

    private string MarkerContents() => $"{planId:D}\n{(serviceWasActive() ? "active" : "inactive")}\n" +
        (participantEncoding is null ? "" : participantEncoding + "\n");

    private static (long Inode, int DeviceMajor, int DeviceMinor) Validate(SafeFileHandle handle, bool directoryExpected)
    {
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            if (statx(Fd(handle), "", AtEmptyPath, 0x7ff, buffer) != 0) throw new InvalidOperationException("maintenance_stat_failed");
            if ((Marshal.ReadInt32(buffer) & 0x10f) != 0x10f) throw new InvalidOperationException("maintenance_stat_incomplete");
            var uid = Marshal.ReadInt32(buffer, 20);
            var mode = (ushort)Marshal.ReadInt16(buffer, 28);
            var links = Marshal.ReadInt32(buffer, 16);
            var type = mode & 0xf000;
            if (uid != 0 || (directoryExpected ? type != 0x4000 || (mode & 0x12) != 0 : type != 0x8000 || (mode & 0x12) != 0 || links != 1))
                throw new InvalidOperationException("maintenance_inode_invalid");
            return (Marshal.ReadInt64(buffer, 32), Marshal.ReadInt32(buffer, 136), Marshal.ReadInt32(buffer, 140));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static SafeFileHandle OpenHandle(int fd)
    {
        if (fd < 0) throw new InvalidOperationException("maintenance_open_failed");
        if (fd == 0) { close(fd); throw new InvalidOperationException("maintenance_fd_invalid"); }
        return new SafeFileHandle((IntPtr)fd, ownsHandle: true);
    }
    private static int Fd(SafeFileHandle handle) => checked((int)handle.DangerousGetHandle());
    internal static SafeFileHandle OpenVerifiedRootFile(string path)
    {
        var handle = OpenHandle(open(path, NoFollow | CloseOnExec, 0));
        try
        {
            Validate(handle, directoryExpected: false);
            for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
            {
                using var directory = OpenHandle(open(parent, NoFollow | CloseOnExec | Directory, 0));
                Validate(directory, directoryExpected: true);
            }
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
    public void Dispose() => directory.Dispose();
    private sealed class Gate(SafeFileHandle handle, Action released) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { handle.Dispose(); released(); return ValueTask.CompletedTask; }
    }

    [DllImport("libc", SetLastError = true)] private static extern uint geteuid();
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags, uint mode);
    [DllImport("libc", SetLastError = true)] private static extern int openat(int dirfd, string path, int flags, uint mode);
    [DllImport("libc", SetLastError = true)] private static extern int flock(int fd, int operation);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int unlinkat(int dirfd, string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int renameat2(int oldDirectory, string oldPath, int newDirectory, string newPath, uint flags);
    [DllImport("libc", SetLastError = true)] private static extern int statx(int dirfd, string path, int flags, uint mask, IntPtr buffer);
}
