using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Contracts.Diagnostics;

public sealed record ClientErrorReportRequest(
    [property: Required, MinLength(8), MaxLength(80)] string ClientErrorId,
    [property: Required, MinLength(1), MaxLength(200)] string ErrorName,
    [property: Required, MinLength(1), MaxLength(4000)] string Message,
    [property: MaxLength(8000)] string? ComponentStack,
    [property: MaxLength(300)] string? Route);
