using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Contracts.Settings;

public sealed record CreateDatabaseBackupRequest(
    [ActionComment, MaxLength(500)] string? Reason);
