using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Options;

public sealed record EntraOptions
{
    public const string SectionName = "Entra";

    [Required]
    public string TenantId { get; init; } = "";

    [Required]
    public string Audience { get; init; } = "";
}
