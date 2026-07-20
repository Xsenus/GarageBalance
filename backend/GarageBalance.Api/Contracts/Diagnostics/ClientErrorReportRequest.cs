using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Contracts.Diagnostics;

public sealed class ClientErrorReportRequest
{
    [Required, MinLength(8), MaxLength(80)]
    public required string ClientErrorId { get; init; }

    [Required, MinLength(1), MaxLength(200)]
    public required string ErrorName { get; init; }

    [Required, MinLength(1), MaxLength(4000)]
    public required string Message { get; init; }

    [MaxLength(8000)]
    public string? ComponentStack { get; init; }

    [MaxLength(300)]
    public string? Route { get; init; }
}
