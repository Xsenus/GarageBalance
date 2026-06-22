namespace GarageBalance.Api.Application.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "GarageBalance";
    public string Audience { get; set; } = "GarageBalance";
    public string SigningKey { get; set; } = "change-this-development-key-before-real-data-32";
    public int AccessTokenMinutes { get; set; } = 60;
}
