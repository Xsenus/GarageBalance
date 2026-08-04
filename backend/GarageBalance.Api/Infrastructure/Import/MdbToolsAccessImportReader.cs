using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using GarageBalance.Api.Application.Import;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Infrastructure.Import;

public sealed class MdbToolsAccessImportReaderOptions
{
    public const string SectionName = "AccessImportReader";

    public bool Enabled { get; init; } = true;

    [Required]
    public string ExecutablePath { get; init; } = "mdb-tables";

    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 30;

    [Range(1, 1000)]
    public int MaximumTableCount { get; init; } = 250;
}

public interface IAccessImportCommandRunner
{
    Task<AccessImportCommandResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed record AccessImportCommandResult(bool Started, int ExitCode, string StandardOutput, string ErrorCode);

public sealed class ProcessAccessImportCommandRunner : IAccessImportCommandRunner
{
    public async Task<AccessImportCommandResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return new AccessImportCommandResult(false, -1, string.Empty, "reader_start_failed");
            }
        }
        catch (Win32Exception)
        {
            return new AccessImportCommandResult(false, -1, string.Empty, "reader_not_installed");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            return new AccessImportCommandResult(true, -1, string.Empty, "reader_timeout");
        }

        var output = await outputTask;
        _ = await errorTask;
        return new AccessImportCommandResult(
            true,
            process.ExitCode,
            output,
            process.ExitCode == 0 ? string.Empty : "reader_rejected_file");
    }
}

public sealed class MdbToolsAccessImportReader(
    IOptions<MdbToolsAccessImportReaderOptions> options,
    IAccessImportCommandRunner commandRunner) : IAccessImportReader
{
    private readonly MdbToolsAccessImportReaderOptions _options = options.Value;

    public async Task<AccessImportReaderStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return CreateStatus(false, "disabled", "Reader Access отключён настройкой AccessImportReader:Enabled.");
        }

        var result = await commandRunner.RunAsync(
            _options.ExecutablePath,
            ["--version"],
            TimeSpan.FromSeconds(_options.TimeoutSeconds),
            cancellationToken);
        return result.Started && result.ExitCode == 0
            ? CreateStatus(true, "ready", "Reader mdbtools готов к чтению файлов .mdb и .accdb.")
            : CreateStatus(false, result.ErrorCode, GetFailureMessage(result.ErrorCode));
    }

    public async Task<AccessImportReaderInspectionDto> InspectAsync(
        ReadOnlyMemory<byte> content,
        string fileExtension,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return AccessImportReaderInspectionDto.Unavailable("disabled", "Reader Access отключён администратором.");
        }

        var extension = fileExtension.Equals(".mdb", StringComparison.OrdinalIgnoreCase) ? ".mdb" : ".accdb";
        var directory = Path.Combine(Path.GetTempPath(), "garagebalance-access-reader", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, $"source{extension}");
        try
        {
            Directory.CreateDirectory(directory);
            TryRestrictDirectory(directory);
            await using (var stream = new FileStream(
                             filePath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await stream.WriteAsync(content, cancellationToken);
            }

            var result = await commandRunner.RunAsync(
                _options.ExecutablePath,
                ["-1", filePath],
                TimeSpan.FromSeconds(_options.TimeoutSeconds),
                cancellationToken);
            if (!result.Started || result.ExitCode != 0)
            {
                return AccessImportReaderInspectionDto.Unavailable(result.ErrorCode, GetFailureMessage(result.ErrorCode));
            }

            var tableNames = result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => !name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(_options.MaximumTableCount + 1)
                .ToArray();
            if (tableNames.Length > _options.MaximumTableCount)
            {
                return AccessImportReaderInspectionDto.Unavailable(
                    "reader_table_limit_exceeded",
                    $"В Access найдено больше {_options.MaximumTableCount} пользовательских таблиц. Уточните состав базы до импорта.");
            }

            return AccessImportReaderInspectionDto.Success(tableNames);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private AccessImportReaderStatusDto CreateStatus(bool available, string status, string message) =>
        new(
            "mdbtools",
            "Reader Access (mdbtools)",
            available,
            status,
            message,
            ["mdbtools 1.0 или новее"],
            DateTimeOffset.UtcNow);

    private static string GetFailureMessage(string errorCode) => errorCode switch
    {
        "reader_not_installed" => "Reader mdbtools не установлен или недоступен в PATH.",
        "reader_timeout" => "Reader Access не завершил проверку за допустимое время.",
        "reader_rejected_file" => "Reader mdbtools не смог открыть файл Access. Проверьте формат и целостность копии.",
        _ => "Reader Access не удалось запустить."
    };

    private static void TryRestrictDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (PlatformNotSupportedException)
        {
            // The temporary directory is still random and removed in finally.
        }
    }
}
