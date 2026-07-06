using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace GarageBalance.Api.Application.Workflows;

public sealed record FormStateDto(
    string Scope,
    JsonElement Payload,
    DateTimeOffset UpdatedAtUtc,
    Guid? UpdatedByUserId);

public sealed record UpsertFormStateRequest(
    [Required] JsonElement Payload,
    [MaxLength(500)] string? Summary = null);
