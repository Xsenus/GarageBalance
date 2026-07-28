using System.Security.Claims;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.ReportsRead)]
[Route("api/reports/garage-quick-lists")]
public sealed class GarageReportQuickListsController(IGarageReportQuickListService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GarageReportQuickListDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GarageReportQuickListDto>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<GarageReportQuickListDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GarageReportQuickListDto>> Create(
        UpsertGarageReportQuickListRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetAll), result.Value)
            : ToProblem(result.ErrorCode!, result.ErrorMessage!);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<GarageReportQuickListDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GarageReportQuickListDto>> Update(
        Guid id,
        UpsertGarageReportQuickListRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToProblem(result.ErrorCode!, result.ErrorMessage!);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromBody] DeleteGarageReportQuickListRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToProblem(result.ErrorCode!, result.ErrorMessage!);
    }

    private ActionResult ToProblem(string code, string message)
    {
        var status = code switch
        {
            "garage_quick_list_not_found" => StatusCodes.Status404NotFound,
            "garage_quick_list_name_conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, ApiProblemDetails.Create(code, message, status));
    }

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }
}
