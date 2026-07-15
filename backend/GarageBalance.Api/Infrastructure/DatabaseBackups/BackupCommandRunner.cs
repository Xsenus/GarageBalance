using System.Diagnostics;

namespace GarageBalance.Api.Infrastructure.Backups;

public interface IBackupToolLocator
{
    string? Resolve(string configuredPath);
}

public sealed class BackupToolLocator : IBackupToolLocator
{
    public string? Resolve(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var executableName = OperatingSystem.IsWindows() && Path.GetExtension(configuredPath).Length == 0
            ? configuredPath + ".exe"
            : configuredPath;
        if (Path.IsPathFullyQualified(executableName) || executableName.Contains(Path.DirectorySeparatorChar) || executableName.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(executableName) ? Path.GetFullPath(executableName) : null;
        }

        foreach (var directory in EnumerateSearchDirectories())
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSearchDirectories()
    {
        var configuredBin = Environment.GetEnvironmentVariable("POSTGRESQL_BIN");
        if (!string.IsNullOrWhiteSpace(configuredBin))
        {
            yield return configuredBin;
        }

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var postgresRoot = Path.Combine(programFiles, "PostgreSQL");
            if (Directory.Exists(postgresRoot))
            {
                foreach (var versionDirectory in OrderPostgresInstallations(Directory.EnumerateDirectories(postgresRoot)))
                {
                    yield return Path.Combine(versionDirectory, "bin");
                }
            }
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return directory;
        }

        if (!OperatingSystem.IsWindows())
        {
            yield return "/usr/bin";
            yield return "/usr/local/bin";
        }
    }

    internal static IEnumerable<string> OrderPostgresInstallations(IEnumerable<string> directories)
    {
        return directories.OrderByDescending(
            path => ParsePostgresVersion(Path.GetFileName(path)),
            Comparer<Version>.Default);
    }

    private static Version ParsePostgresVersion(string value)
    {
        var normalized = value.Contains('.', StringComparison.Ordinal) ? value : value + ".0";
        return Version.TryParse(normalized, out var version) ? version : new Version();
    }
}

public sealed record BackupCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment);

public sealed record BackupCommandResult(int ExitCode, string StandardError);

public interface IBackupCommandRunner
{
    Task<BackupCommandResult> RunAsync(BackupCommand command, CancellationToken cancellationToken);
}

public sealed class BackupCommandRunner : IBackupCommandRunner
{
    public async Task<BackupCommandResult> RunAsync(BackupCommand command, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in command.Environment)
        {
            startInfo.Environment[key] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return new BackupCommandResult(-1, "Backup process could not be started.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            _ = await standardOutputTask;
            var standardError = await standardErrorTask;
            return new BackupCommandResult(process.ExitCode, standardError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            throw;
        }
    }
}
