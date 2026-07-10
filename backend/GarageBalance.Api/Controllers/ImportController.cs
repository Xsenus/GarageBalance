using System.Security.Claims;
using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.ImportRun)]
[Route("api/import/access")]
public sealed class ImportController(IImportService importService, IImportQuarantineService importQuarantineService) : ControllerBase
{
    [HttpGet("reader/status")]
    [ProducesResponseType<AccessImportReaderStatusDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccessImportReaderStatusDto>> GetAccessImportReaderStatus(CancellationToken cancellationToken)
    {
        return Ok(await importService.GetAccessImportReaderStatusAsync(cancellationToken));
    }

    [HttpGet("runs")]
    [ProducesResponseType<IReadOnlyList<AccessImportRunDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccessImportRunDto>>> GetAccessImportRuns(
        [FromQuery] AccessImportRunListRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await importService.GetAccessImportRunsAsync(request, cancellationToken));
    }

    [HttpGet("quarantine")]
    [ProducesResponseType<IReadOnlyList<AccessImportQuarantineItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccessImportQuarantineItemDto>>> GetOpenQuarantineItems(
        [FromQuery] Guid? accessImportRunId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        return Ok(await importQuarantineService.GetOpenItemsAsync(accessImportRunId, cancellationToken, limit));
    }

    [HttpPost("runs/{id:guid}/report")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportAccessImportRunReport(Guid id, CancellationToken cancellationToken)
    {
        var result = await importService.ExportAccessImportRunReportAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : NotFound(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status404NotFound));
    }

    [HttpPost("runs/{id:guid}/rollback")]
    [ProducesResponseType<AccessImportRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccessImportRunDto>> CancelAccessImportRun(
        Guid id,
        [FromBody] AccessImportRollbackRequest request,
        CancellationToken cancellationToken)
    {
        var result = await importService.RequestAccessImportRollbackAsync(id, request, GetActorUserId(), cancellationToken);
        if (result.Succeeded)
        {
            return Ok(result.Value);
        }

        var statusCode = result.ErrorCode == "import_run_not_found"
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

        return StatusCode(statusCode, ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, statusCode));
    }

    [HttpPost("runs/{id:guid}/apply")]
    [ProducesResponseType<AccessImportRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccessImportRunDto>> RequestAccessImportApply(
        Guid id,
        [FromBody] AccessImportApplyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await importService.RequestAccessImportApplyAsync(id, request, GetActorUserId(), cancellationToken);
        if (result.Succeeded)
        {
            return Ok(result.Value);
        }

        var statusCode = result.ErrorCode == "import_run_not_found"
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

        return StatusCode(statusCode, ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, statusCode));
    }

    [HttpPost("runs/{id:guid}/apply/cancel")]
    [ProducesResponseType<AccessImportRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccessImportRunDto>> CancelAccessImportApplyRequest(
        Guid id,
        [FromBody] AccessImportApplyCancelRequest request,
        CancellationToken cancellationToken)
    {
        var result = await importService.CancelAccessImportApplyRequestAsync(id, request, GetActorUserId(), cancellationToken);
        if (result.Succeeded)
        {
            return Ok(result.Value);
        }

        var statusCode = result.ErrorCode == "import_run_not_found"
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

        return StatusCode(statusCode, ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, statusCode));
    }

    [HttpGet("runs/{id:guid}/log")]
    [ProducesResponseType<IReadOnlyList<AccessImportRunLogEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AccessImportRunLogEntryDto>>> GetAccessImportRunLog(
        Guid id,
        [FromQuery] AccessImportRunLogListRequest request,
        CancellationToken cancellationToken)
    {
        var result = await importService.GetAccessImportRunLogEntriesAsync(id, request, cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : NotFound(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status404NotFound));
    }

    [HttpGet("runs/{id:guid}/created-records")]
    [ProducesResponseType<IReadOnlyList<AccessImportCreatedRecordDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AccessImportCreatedRecordDto>>> GetAccessImportCreatedRecords(
        Guid id,
        [FromQuery] AccessImportCreatedRecordListRequest request,
        CancellationToken cancellationToken)
    {
        var result = await importService.GetAccessImportCreatedRecordsAsync(id, request, cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : NotFound(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status404NotFound));
    }

    [HttpPost("dry-run")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<AccessImportRunDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccessImportRunDto>> DryRunAccessImport([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest(ApiProblemDetails.Create("file_required", "Нужно выбрать файл Access для проверки.", StatusCodes.Status400BadRequest));
        }

        await using var stream = file.OpenReadStream();
        var result = await importService.DryRunAccessImportAsync(new AccessImportDryRunRequest(file.FileName, stream), GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetAccessImportRuns), new { id = result.Value!.Id }, result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPatch("quarantine/{id:guid}/resolve")]
    [ProducesResponseType<AccessImportQuarantineItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccessImportQuarantineItemDto>> ResolveQuarantineItem(
        Guid id,
        ResolveImportQuarantineItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await importQuarantineService.ResolveAsync(id, request, GetActorUserId(), cancellationToken);
        if (result.Succeeded)
        {
            return Ok(result.Value);
        }

        var statusCode = result.ErrorCode == "import_quarantine_item_not_found"
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

        return StatusCode(statusCode, ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, statusCode));
    }

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }
}
