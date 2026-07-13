using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Route("api/suggestions")]
[Authorize(Policy = SystemPermissions.DictionariesRead)]
public sealed class SuggestionsController(IDadataSuggestionService dadataSuggestionService) : ControllerBase
{
    [HttpGet("parties")]
    [ProducesResponseType<IReadOnlyList<DadataPartySuggestionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<DadataPartySuggestionDto>>> SuggestParties([FromQuery] string? query, [FromQuery] int count = 8, CancellationToken cancellationToken = default)
    {
        if (ValidateQuery(query, count) is { } validationError)
        {
            return validationError;
        }

        var result = await dadataSuggestionService.SuggestPartiesAsync(query!, count, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToSuggestionError(result.ErrorCode, result.ErrorMessage);
    }

    [HttpGet("addresses")]
    [ProducesResponseType<IReadOnlyList<DadataAddressSuggestionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<DadataAddressSuggestionDto>>> SuggestAddresses([FromQuery] string? query, [FromQuery] int count = 8, CancellationToken cancellationToken = default)
    {
        if (ValidateQuery(query, count) is { } validationError)
        {
            return validationError;
        }

        var result = await dadataSuggestionService.SuggestAddressesAsync(query!, count, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToSuggestionError(result.ErrorCode, result.ErrorMessage);
    }

    private static BadRequestObjectResult? ValidateQuery(string? query, int count)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2 || query.Trim().Length > 300)
        {
            return new BadRequestObjectResult(ApiProblemDetails.Create("suggestion_query_invalid", "Введите от 2 до 300 символов.", StatusCodes.Status400BadRequest));
        }

        return count is < 1 or > 10
            ? new BadRequestObjectResult(ApiProblemDetails.Create("suggestion_count_invalid", "Количество подсказок должно быть от 1 до 10.", StatusCodes.Status400BadRequest))
            : null;
    }

    private ObjectResult ToSuggestionError(string? errorCode, string? errorMessage)
    {
        var statusCode = errorCode == "dadata_not_configured" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status502BadGateway;
        return StatusCode(statusCode, ApiProblemDetails.Create(errorCode, errorMessage, statusCode));
    }
}
