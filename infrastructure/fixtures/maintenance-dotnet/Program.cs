using GarageBalance.StorageTool;

if (args.Length != 2 || args[0] is not ("success" or "concurrent" or "before-commit" or "before-receipt" or "after-receipt"))
{
    Console.Error.WriteLine("Invalid fixture mode.");
    return 64;
}

var mode = args[0];
var fixtureDirectory = args[1];
if (!System.Text.RegularExpressions.Regex.IsMatch(fixtureDirectory, @"\A/run/gb-barrier-dotnet\.[A-Za-z0-9]{8}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
{
    Console.Error.WriteLine("Invalid fixture directory.");
    return 64;
}
Task SignalAndWait(string phase, CancellationToken token)
{
    File.WriteAllText(Path.Combine(fixtureDirectory, "phase"), phase);
    Console.WriteLine($"phase={phase}");
    return Task.Delay(Timeout.InfiniteTimeSpan, token);
}

try
{
    if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("Fixture requires Linux.");
    var modeBits = File.GetUnixFileMode(fixtureDirectory);
    var expectedEntries = mode == "concurrent" ? new[] { "activity.lock", "deploy.lock", "holder-ready" } : new[] { "activity.lock", "deploy.lock" };
    if ((modeBits & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0 ||
        !Directory.EnumerateFileSystemEntries(fixtureDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal)
            .SequenceEqual(expectedEntries, StringComparer.Ordinal) ||
        (mode == "concurrent" && ((File.GetAttributes(Path.Combine(fixtureDirectory, "holder-ready")) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(Path.Combine(fixtureDirectory, "holder-ready")).Length != 0)))
        throw new InvalidOperationException("Fixture directory is not private and empty.");
    using var host = LinuxMaintenanceBarrierHost.CreateFixture(fixtureDirectory,
        Guid.Parse("5aa5e64f-d46d-4af5-85a0-2cc016219477"), new string('a', 64),
        _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask,
        token => mode == "before-receipt" ? SignalAndWait("before-receipt", token) : Task.CompletedTask,
        token => mode == "after-receipt" ? SignalAndWait("after-receipt", token) : Task.CompletedTask,
        () => true, () => new MaintenanceParticipantState(false, false, []), _ =>
        {
            var receipt = File.ReadAllLines(Path.Combine(fixtureDirectory, "participants-resume-required"));
            if (File.Exists(Path.Combine(fixtureDirectory, "recovery-required")) || receipt.Length != 5 ||
                receipt[3] != new MaintenanceParticipantState(false, false, []).Encode() || receipt[4] != "outcome=committed")
                throw new InvalidOperationException("Participant permission phase was not durable.");
            return Task.CompletedTask;
        });
    if (mode == "concurrent") host.FixtureActivityAttempt = acquired =>
        File.WriteAllText(Path.Combine(fixtureDirectory, "waiting-activity"), acquired ? "acquired" : "blocked");
    // Prepare durable state in one invocation scope, then adopt it in the next.
    // No database, working service, catalog or application path is involved.
    await using (var deploy = await host.AcquireDeployGateAsync(default))
    {
        await host.CaptureServiceStateAsync(default);
        await host.CreateDurableMarkerAsync(default);
        await host.TransitionPreparationAsync("preparing", null, default);
        await using var activity = await host.AcquireActivityGateAsync(default);
        await host.VerifyOfflineAsync(default);
        await host.SyncPreparedPlanDirectoryAsync(Path.Combine(fixtureDirectory, "synthetic-plan.json"), default);
        await host.TransitionPreparationAsync("prepared", new string('a', 64), default);
    }
    await MaintenanceBarrierCoordinator.ExecutePreparedAsync(host,
        _ =>
        {
            if (host.ReadPreparationProgress()?.Phase != "prepared") throw new InvalidOperationException("Expected prepared journal.");
            var files = host.ReadRecoveryFiles();
            var state = MaintenanceRecoveryInspection.Inspect(files.Marker, files.Receipt, 0, files.ParticipantsPermitted);
            host.AdoptRecoveryFiles(state);
            return Task.CompletedTask;
        }, token => host.TransitionPreparationAsync("executing", new string('a', 64), token),
        token =>
        {
            if (host.ReadPreparationProgress()?.Phase != "executing") throw new InvalidOperationException("Execution journal not durable.");
            if (mode == "before-commit") return SignalAndWait("before-commit", token);
            if (mode == "concurrent") File.WriteAllText(Path.Combine(fixtureDirectory, "operation-entered"), "entered");
            return Task.CompletedTask;
        },
        CancellationToken.None);
    Console.WriteLine("fixture=complete");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"fixtureFailure={error.GetType().Name}");
    return 1;
}
