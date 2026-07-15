using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Contracts.Settings;

public sealed record CreateDatabaseBackupRequest(
    [property: Required, MinLength(3), MaxLength(500)] string Reason);
