using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Contracts.Settings;

public sealed record DeleteDatabaseBackupRequest(
    [property: Required, MinLength(3), MaxLength(500)] string Reason);
