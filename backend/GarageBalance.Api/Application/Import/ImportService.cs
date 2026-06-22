using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Import;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Import;

public sealed class ImportService(GarageBalanceDbContext dbContext) : IImportService
{
    private const long MaxDryRunFileSizeBytes = 512L * 1024L * 1024L;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AccessImportRunDto>> GetAccessImportRunsAsync(CancellationToken cancellationToken)
    {
        var runs = await dbContext.AccessImportRuns.AsNoTracking().ToListAsync(cancellationToken);
        return runs
            .OrderByDescending(run => run.StartedAtUtc)
            .Take(50)
            .Select(ToDto)
            .ToList();
    }

    public async Task<ImportResult<AccessImportRunDto>> DryRunAccessImportAsync(AccessImportDryRunRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return ImportResult<AccessImportRunDto>.Failure("file_name_required", "Имя файла Access обязательно.");
        }

        var fileName = Path.GetFileName(request.FileName.Trim());
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".accdb" and not ".mdb")
        {
            return ImportResult<AccessImportRunDto>.Failure("access_extension_required", "Для dry-run импорта нужен файл .accdb или .mdb.");
        }

        await using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            return ImportResult<AccessImportRunDto>.Failure("file_empty", "Файл Access пустой.");
        }

        if (buffer.Length > MaxDryRunFileSizeBytes)
        {
            return ImportResult<AccessImportRunDto>.Failure("file_too_large", "Файл Access слишком большой для первичной проверки.");
        }

        var bytes = buffer.ToArray();
        var checks = BuildChecks(extension, bytes);
        var errors = checks.Count(check => check.Status == "error");
        var warnings = checks.Count(check => check.Status == "warning");
        var passed = checks.Count(check => check.Status == "passed");
        var status = errors > 0 ? "blocked" : "completed";
        var summary = errors > 0
            ? "Dry-run остановлен: есть ошибки, которые нужно исправить до импорта."
            : warnings > 0
                ? "Dry-run завершен с предупреждениями: можно продолжать подготовку, но нужен драйвер/конвертация Access."
                : "Dry-run завершен: файл прошел первичные проверки.";

        var run = new AccessImportRun
        {
            Mode = "dry_run",
            Status = status,
            OriginalFileName = fileName,
            FileExtension = extension,
            FileSizeBytes = buffer.Length,
            ContentSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            ActorUserId = actorUserId,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            TotalChecks = checks.Count,
            PassedChecks = passed,
            WarningCount = warnings,
            ErrorCount = errors,
            Summary = summary,
            ReportJson = JsonSerializer.Serialize(checks, JsonOptions)
        };

        dbContext.AccessImportRuns.Add(run);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorUserId,
            Action = "import.access_dry_run",
            EntityType = "access_import_run",
            EntityId = run.Id.ToString(),
            Summary = $"Dry-run импорта Access: {fileName}, статус {status}, проверок {checks.Count}."
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ImportResult<AccessImportRunDto>.Success(ToDto(run));
    }

    private static List<AccessImportCheckDto> BuildChecks(string extension, byte[] bytes)
    {
        var checks = new List<AccessImportCheckDto>
        {
            new("extension", "Формат файла", "passed", $"Расширение {extension} поддерживается для импорта Access."),
            new("size", "Размер файла", "passed", $"Файл содержит {bytes.LongLength:N0} байт и может быть проверен."),
            HasOleSignature(bytes)
                ? new AccessImportCheckDto("signature", "Сигнатура Access", "passed", "Файл похож на OLE Compound документ Access.")
                : new AccessImportCheckDto("signature", "Сигнатура Access", "warning", "Не найдена стандартная OLE-сигнатура. Возможно, файл поврежден или требует конвертации."),
            new("native_reader", "Драйвер чтения .accdb", "warning", "На текущей машине прямое чтение Access не подтверждено. Для фактического переноса нужен ACE-драйвер, Microsoft Access или конвертация в промежуточный формат.")
        };

        var discoveredHints = DiscoverSchemaHints(bytes);
        checks.Add(discoveredHints.Count == 0
            ? new AccessImportCheckDto("schema_hints", "Ориентиры схемы", "warning", "В бинарном файле не найдены читаемые названия таблиц. Карта Access -> PostgreSQL потребует ACE-драйвер или экспорт.")
            : new AccessImportCheckDto("schema_hints", "Ориентиры схемы", "passed", $"Найдены текстовые ориентиры: {string.Join(", ", discoveredHints.Take(8))}."));

        return checks;
    }

    private static bool HasOleSignature(byte[] bytes)
    {
        var oleSignature = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        return bytes.Length >= oleSignature.Length && bytes.Take(oleSignature.Length).SequenceEqual(oleSignature);
    }

    private static IReadOnlyList<string> DiscoverSchemaHints(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes) + " " + Encoding.Unicode.GetString(bytes);
        var candidates = new[] { "гараж", "владел", "плат", "счет", "счёт", "тариф", "постав", "контраг", "garage", "owner", "payment", "tariff", "meter" };
        return candidates
            .Where(candidate => text.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AccessImportRunDto ToDto(AccessImportRun run)
    {
        var checks = JsonSerializer.Deserialize<List<AccessImportCheckDto>>(run.ReportJson, JsonOptions) ?? [];
        return new AccessImportRunDto(
            run.Id,
            run.Mode,
            run.Status,
            run.OriginalFileName,
            run.FileExtension,
            run.FileSizeBytes,
            run.ContentSha256,
            run.StartedAtUtc,
            run.FinishedAtUtc,
            run.TotalChecks,
            run.PassedChecks,
            run.WarningCount,
            run.ErrorCount,
            run.Summary,
            checks);
    }
}
