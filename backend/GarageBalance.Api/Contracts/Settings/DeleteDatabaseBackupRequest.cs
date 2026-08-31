using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Contracts.Settings;

public sealed record DeleteDatabaseBackupRequest(
    [ActionComment, MaxLength(500)] string? Reason);
