using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Options;

public sealed record JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = "";

    [Required]
    public string Audience { get; init; } = "";

    // Must not live in appsettings.json. Set via dotnet user-secrets locally,
    // environment variable or Azure Key Vault in production.
    [Required, MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters.")]
    public string SigningKey { get; init; } = "";

    [Range(1, 1440, ErrorMessage = "Jwt:AccessTokenLifetimeMinutes must be between 1 and 1440.")]
    public int AccessTokenLifetimeMinutes { get; init; } = 15;

    [Range(1, 90, ErrorMessage = "Jwt:RefreshTokenLifetimeDays must be between 1 and 90.")]
    public int RefreshTokenLifetimeDays { get; init; } = 7;
}
