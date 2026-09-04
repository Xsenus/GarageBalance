using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Contracts.Settings;

public sealed record ResetDatabaseRequest(
    [property: Required, MaxLength(200)] string Password,
    [property: Required, MaxLength(100)] string Confirmation,
    [property: Required, MinLength(3), MaxLength(500)] string Reason);
