using System.Diagnostics;

namespace GarageBalance.Api.Infrastructure.Backups;

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
