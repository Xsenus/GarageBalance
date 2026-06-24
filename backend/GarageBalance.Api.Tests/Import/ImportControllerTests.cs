using System.Security.Claims;
using System.Text;
using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Import;

public sealed class ImportControllerTests
{
    [Fact]
    public async Task DryRunAccessImport_ReturnsBadRequestWhenFileMissing()
    {
        var controller = CreateController(new FakeImportService());

        var result = await controller.DryRunAccessImport(null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("file_required", problem.Title);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("file_required", problem.Extensions[ApiProblemDetails.CodeExtensionKey]);
    }

    [Fact]
    public async Task DryRunAccessImport_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var run = CreateRun();
        var service = new FakeImportService
        {
            DryRunResult = ImportResult<AccessImportRunDto>.Success(run)
        };
        var controller = CreateController(service, actorUserId);
        var file = CreateFormFile("ГСК.accdb");

        var result = await controller.DryRunAccessImport(file, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal("ГСК.accdb", service.LastFileName);
    }

    [Fact]
    public async Task DryRunAccessImport_ReturnsBadRequestForServiceError()
    {
        var service = new FakeImportService
        {
            DryRunResult = ImportResult<AccessImportRunDto>.Failure("access_extension_required", "Нужен файл Access.")
        };
        var controller = CreateController(service);

        var result = await controller.DryRunAccessImport(CreateFormFile("data.xlsx"), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("access_extension_required", problem.Title);
    }

    [Fact]
    public async Task ExportAccessImportRunReport_ReturnsFile()
    {
        var service = new FakeImportService
        {
            ExportResult = ImportResult<ImportReportFileDto>.Success(new ImportReportFileDto("report.json", "application/json; charset=utf-8", Encoding.UTF8.GetBytes("{}")))
        };
        var controller = CreateController(service);
        var runId = Guid.NewGuid();

        var result = await controller.ExportAccessImportRunReport(runId, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("report.json", file.FileDownloadName);
        Assert.Equal("application/json; charset=utf-8", file.ContentType);
        Assert.Equal(runId, service.LastExportRunId);
    }

    [Fact]
    public async Task ExportAccessImportRunReport_ReturnsNotFoundForMissingRun()
    {
        var service = new FakeImportService
        {
            ExportResult = ImportResult<ImportReportFileDto>.Failure("import_run_not_found", "Запуск dry-run импорта не найден.")
        };
        var controller = CreateController(service);

        var result = await controller.ExportAccessImportRunReport(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("import_run_not_found", problem.Title);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("import_run_not_found", problem.Extensions[ApiProblemDetails.CodeExtensionKey]);
    }

    [Fact]
    public async Task GetAccessImportRunLog_ReturnsLogEntries()
    {
        var runId = Guid.NewGuid();
        var service = new FakeImportService
        {
            LogResult = ImportResult<IReadOnlyList<AccessImportRunLogEntryDto>>.Success(
            [
                CreateLogEntry(runId, "file_received"),
                CreateLogEntry(runId, "dry_run_finished")
            ])
        };
        var controller = CreateController(service);

        var result = await controller.GetAccessImportRunLog(runId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var entries = Assert.IsAssignableFrom<IReadOnlyList<AccessImportRunLogEntryDto>>(ok.Value);
        Assert.Equal(2, entries.Count);
        Assert.Equal(runId, service.LastLogRunId);
    }

    [Fact]
    public async Task GetAccessImportRunLog_ReturnsNotFoundForMissingRun()
    {
        var service = new FakeImportService
        {
            LogResult = ImportResult<IReadOnlyList<AccessImportRunLogEntryDto>>.Failure("import_run_not_found", "Запуск dry-run импорта не найден.")
        };
        var controller = CreateController(service);

        var result = await controller.GetAccessImportRunLog(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("import_run_not_found", problem.Title);
    }

    [Fact]
    public async Task GetOpenQuarantineItems_ReturnsItemsFromService()
    {
        var quarantineItem = CreateQuarantineItem();
        var quarantineService = new FakeImportQuarantineService
        {
            OpenItems = [quarantineItem]
        };
        var controller = CreateController(new FakeImportService(), quarantineService);
        var runId = Guid.NewGuid();

        var result = await controller.GetOpenQuarantineItems(runId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<AccessImportQuarantineItemDto>>(ok.Value);
        Assert.Equal(quarantineItem.Id, Assert.Single(items).Id);
        Assert.Equal(runId, quarantineService.LastAccessImportRunId);
    }

    [Fact]
    public async Task ResolveQuarantineItem_ReturnsResolvedItemAndPassesActor()
    {
        var actorUserId = Guid.NewGuid();
        var quarantineItem = CreateQuarantineItem("resolved");
        var quarantineService = new FakeImportQuarantineService
        {
            ResolveResult = ImportResult<AccessImportQuarantineItemDto>.Success(quarantineItem)
        };
        var controller = CreateController(new FakeImportService(), quarantineService, actorUserId);

        var result = await controller.ResolveQuarantineItem(
            quarantineItem.Id,
            new ResolveImportQuarantineItemRequest("Разобрано."),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var item = Assert.IsType<AccessImportQuarantineItemDto>(ok.Value);
        Assert.Equal("resolved", item.Status);
        Assert.Equal(quarantineItem.Id, quarantineService.LastResolveId);
        Assert.Equal(actorUserId, quarantineService.LastActorUserId);
    }

    [Fact]
    public async Task ResolveQuarantineItem_ReturnsNotFoundForMissingItem()
    {
        var quarantineService = new FakeImportQuarantineService
        {
            ResolveResult = ImportResult<AccessImportQuarantineItemDto>.Failure("import_quarantine_item_not_found", "Строка карантина импорта не найдена.")
        };
        var controller = CreateController(new FakeImportService(), quarantineService);

        var result = await controller.ResolveQuarantineItem(Guid.NewGuid(), new ResolveImportQuarantineItemRequest(null), CancellationToken.None);

        var notFound = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        Assert.Equal("import_quarantine_item_not_found", problem.Title);
    }

    private static ImportController CreateController(FakeImportService service, Guid? actorUserId = null)
    {
        return CreateController(service, new FakeImportQuarantineService(), actorUserId);
    }

    private static ImportController CreateController(FakeImportService service, FakeImportQuarantineService quarantineService, Guid? actorUserId = null)
    {
        var controller = new ImportController(service, quarantineService);
        var claims = actorUserId is null ? [] : new[] { new Claim(ClaimTypes.NameIdentifier, actorUserId.Value.ToString()) };
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        return controller;
    }

    private static IFormFile CreateFormFile(string fileName)
    {
        var bytes = Encoding.UTF8.GetBytes("garage owner");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);
    }

    private static AccessImportRunDto CreateRun()
    {
        return new AccessImportRunDto(
            Guid.NewGuid(),
            "dry_run",
            "completed",
            "ГСК.accdb",
            ".accdb",
            100,
            new string('a', 64),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            1,
            0,
            0,
            "Проверка завершена.",
            [new AccessImportCheckDto("extension", "Формат", "passed", "OK")]);
    }

    private static AccessImportQuarantineItemDto CreateQuarantineItem(string status = "open")
    {
        return new AccessImportQuarantineItemDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Access",
            "Garage",
            "42",
            new string('a', 64),
            "missing-owner",
            "Не найден владелец гаража.",
            "error",
            status,
            DateTimeOffset.UtcNow,
            null,
            status == "resolved" ? DateTimeOffset.UtcNow : null,
            status == "resolved" ? Guid.NewGuid() : null,
            status == "resolved" ? "Разобрано." : null);
    }

    private static AccessImportRunLogEntryDto CreateLogEntry(Guid runId, string stepCode)
    {
        return new AccessImportRunLogEntryDto(
            Guid.NewGuid(),
            runId,
            DateTimeOffset.UtcNow,
            "info",
            stepCode,
            $"Step {stepCode}");
    }

    private sealed class FakeImportService : IImportService
    {
        public Guid? LastActorUserId { get; private set; }
        public string? LastFileName { get; private set; }
        public Guid? LastExportRunId { get; private set; }
        public Guid? LastLogRunId { get; private set; }
        public ImportResult<AccessImportRunDto> DryRunResult { get; init; } = ImportResult<AccessImportRunDto>.Failure("not_configured", "Not configured.");
        public ImportResult<ImportReportFileDto> ExportResult { get; init; } = ImportResult<ImportReportFileDto>.Failure("not_configured", "Not configured.");
        public ImportResult<IReadOnlyList<AccessImportRunLogEntryDto>> LogResult { get; init; } =
            ImportResult<IReadOnlyList<AccessImportRunLogEntryDto>>.Success([]);

        public Task<IReadOnlyList<AccessImportRunDto>> GetAccessImportRunsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AccessImportRunDto>>([]);
        }

        public Task<ImportResult<ImportReportFileDto>> ExportAccessImportRunReportAsync(Guid runId, CancellationToken cancellationToken)
        {
            LastExportRunId = runId;
            return Task.FromResult(ExportResult);
        }

        public Task<ImportResult<IReadOnlyList<AccessImportRunLogEntryDto>>> GetAccessImportRunLogEntriesAsync(Guid runId, CancellationToken cancellationToken)
        {
            LastLogRunId = runId;
            return Task.FromResult(LogResult);
        }

        public Task<ImportResult<AccessImportRunDto>> DryRunAccessImportAsync(AccessImportDryRunRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastFileName = request.FileName;
            return Task.FromResult(DryRunResult);
        }
    }

    private sealed class FakeImportQuarantineService : IImportQuarantineService
    {
        public Guid? LastAccessImportRunId { get; private set; }
        public Guid? LastResolveId { get; private set; }
        public Guid? LastActorUserId { get; private set; }
        public IReadOnlyList<AccessImportQuarantineItemDto> OpenItems { get; init; } = [];
        public ImportResult<AccessImportQuarantineItemDto> ResolveResult { get; init; } =
            ImportResult<AccessImportQuarantineItemDto>.Failure("not_configured", "Not configured.");

        public Task<IReadOnlyList<AccessImportQuarantineItemDto>> GetOpenItemsAsync(Guid? accessImportRunId, CancellationToken cancellationToken)
        {
            LastAccessImportRunId = accessImportRunId;
            return Task.FromResult(OpenItems);
        }

        public Task<ImportResult<AccessImportQuarantineItemDto>> RegisterAsync(RegisterImportQuarantineItemRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ImportResult<AccessImportQuarantineItemDto>.Failure("not_supported", "Not supported."));
        }

        public Task<ImportResult<AccessImportQuarantineItemDto>> ResolveAsync(Guid id, ResolveImportQuarantineItemRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastResolveId = id;
            LastActorUserId = actorUserId;
            return Task.FromResult(ResolveResult);
        }
    }
}
