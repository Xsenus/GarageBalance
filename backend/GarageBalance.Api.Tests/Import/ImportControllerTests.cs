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

    private static ImportController CreateController(FakeImportService service, Guid? actorUserId = null)
    {
        var controller = new ImportController(service);
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

    private sealed class FakeImportService : IImportService
    {
        public Guid? LastActorUserId { get; private set; }
        public string? LastFileName { get; private set; }
        public Guid? LastExportRunId { get; private set; }
        public ImportResult<AccessImportRunDto> DryRunResult { get; init; } = ImportResult<AccessImportRunDto>.Failure("not_configured", "Not configured.");
        public ImportResult<ImportReportFileDto> ExportResult { get; init; } = ImportResult<ImportReportFileDto>.Failure("not_configured", "Not configured.");

        public Task<IReadOnlyList<AccessImportRunDto>> GetAccessImportRunsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AccessImportRunDto>>([]);
        }

        public Task<ImportResult<ImportReportFileDto>> ExportAccessImportRunReportAsync(Guid runId, CancellationToken cancellationToken)
        {
            LastExportRunId = runId;
            return Task.FromResult(ExportResult);
        }

        public Task<ImportResult<AccessImportRunDto>> DryRunAccessImportAsync(AccessImportDryRunRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastFileName = request.FileName;
            return Task.FromResult(DryRunResult);
        }
    }
}
