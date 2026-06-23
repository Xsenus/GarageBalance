using System.Security.Claims;
using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.ImportRun)]
[Route("api/import/access")]
public sealed class ImportController(IImportService importService) : ControllerBase
{
    [HttpGet("runs")]
    [ProducesResponseType<IReadOnlyList<AccessImportRunDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccessImportRunDto>>> GetAccessImportRuns(CancellationToken cancellationToken)
    {
        return Ok(await importService.GetAccessImportRunsAsync(cancellationToken));
    }

    [HttpGet("runs/{id:guid}/report")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportAccessImportRunReport(Guid id, CancellationToken cancellationToken)
    {
        var result = await importService.ExportAccessImportRunReportAsync(id, cancellationToken);
        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : NotFound(new ProblemDetails { Title = result.ErrorCode, Detail = result.ErrorMessage });
    }

    [HttpPost("dry-run")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<AccessImportRunDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccessImportRunDto>> DryRunAccessImport([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "file_required",
                Detail = "Нужно выбрать файл Access для проверки."
            });
        }

        await using var stream = file.OpenReadStream();
        var result = await importService.DryRunAccessImportAsync(new AccessImportDryRunRequest(file.FileName, stream), GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetAccessImportRuns), new { id = result.Value!.Id }, result.Value)
            : BadRequest(new ProblemDetails { Title = result.ErrorCode, Detail = result.ErrorMessage });
    }

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }
}
