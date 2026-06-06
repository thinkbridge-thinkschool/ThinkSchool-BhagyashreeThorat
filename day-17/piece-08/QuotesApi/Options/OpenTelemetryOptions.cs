using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Options;

public sealed record OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    [Required]
    public string ServiceName { get; init; } = "QuotesApi";

    public string? OtlpEndpoint { get; init; }
}
